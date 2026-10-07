using System.IO.Compression;
using GameSync.Core.Safety;

namespace GameSync.Core.Sync;

/// <summary>A save folder someone kept by hand: its name, where its files are now, and where it came from.</summary>
public sealed record FoundSave(string Name, string Folder, string Source);

/// <summary>
/// BAK-19: finds the save folders someone kept by hand next to a game's live save folder, the way Bloodborne players keep
/// "Before Orphan\SPRJ0005" beside the live "SPRJ0005". Each folder or .zip that holds a copy of the live folder, or the
/// save's files directly, is one named save, named after the folder. Nothing it looks at is changed; a .zip is unpacked
/// into a temporary folder, with every path checked first.
/// </summary>
public static class NamedSaveFinder
{
    private const long MaxZipBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string[] OtherArchives = [".rar", ".7z", ".tar", ".gz"];

    /// <param name="folder">Where the kept folders are, such as Bloodborne's CUSA00207.</param>
    /// <param name="liveFolder">The live save folder they're copies of, such as CUSA00207\SPRJ0005.</param>
    /// <param name="tempFolder">Where .zip files are unpacked; the caller deletes it afterwards. Null only counts them: each .zip is
    /// found as itself, unopened (KAN-61's count of the copies beside a live save).</param>
    public static (IReadOnlyList<FoundSave> Found, IReadOnlyList<string> Skipped) Find(string folder, string liveFolder, string? tempFolder)
    {
        var live = Path.TrimEndingDirectorySeparator(Path.GetFullPath(liveFolder));
        var leaf = Path.GetFileName(live);
        var liveFiles = Directory.Exists(live) ? RelativeFiles(live) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<FoundSave>();
        var skipped = new List<string>();

        foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (full.Equals(live, StringComparison.OrdinalIgnoreCase) || live.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(full);
            var wrapped = Path.Combine(full, leaf);

            // A copy kept one folder further in, like "Before sus\ded beast\SPRJ0005", with or without one directly inside.
            var nested = Directory.EnumerateDirectories(full).Order(StringComparer.OrdinalIgnoreCase)
                .Where(variant => !Path.GetFullPath(variant).Equals(wrapped, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(variant, leaf)))
                .ToList();
            if (Directory.Exists(wrapped) || nested.Count > 0)
            {
                if (Directory.Exists(wrapped))
                {
                    found.Add(new FoundSave(name, wrapped, Path.GetRelativePath(folder, wrapped)));
                }

                foreach (var variant in nested)
                {
                    var inner = Path.Combine(variant, leaf);
                    found.Add(new FoundSave($"{name} / {Path.GetFileName(variant)}", inner, Path.GetRelativePath(folder, inner)));
                }
            }
            else if (RelativeFiles(full).Overlaps(liveFiles))
            {
                // The save's files directly, like "SPRJ0005 - Copy".
                found.Add(new FoundSave(name, full, Path.GetRelativePath(folder, full)));
            }
            else
            {
                skipped.Add($"{name}: no copy of {leaf} in it.");
            }

            foreach (var archive in Directory.EnumerateFiles(full).Where(IsOtherArchive))
            {
                skipped.Add($"{Path.GetRelativePath(folder, archive)}: GameSync can't open {Path.GetExtension(archive)} files. Unpack it into a folder to import it.");
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (found.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    name += " (zip)";
                }

                if (tempFolder is null)
                {
                    found.Add(new FoundSave(name, file, Path.GetFileName(file)));
                    continue;
                }

                var target = Path.Combine(tempFolder, $"zip-{found.Count}");
                if (Unzip(file, leaf, liveFiles, target) is { } problem)
                {
                    skipped.Add($"{Path.GetFileName(file)}: {problem}");
                }
                else
                {
                    found.Add(new FoundSave(name, target, Path.GetFileName(file)));
                }
            }
            else if (IsOtherArchive(file))
            {
                skipped.Add($"{Path.GetFileName(file)}: GameSync can't open {Path.GetExtension(file)} files. Unpack it into a folder to import it.");
            }
        }

        return (found, skipped);
    }

    /// <summary>Unpacks the zip's copy of the live folder into <paramref name="target"/>; returns why not, or null when done.</summary>
    private static string? Unzip(string zipPath, string leaf, IReadOnlySet<string> liveFiles, string target)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
            var mapped = new List<(ZipArchiveEntry Entry, string Relative)>();
            foreach (var entry in files)
            {
                var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var at = Array.FindIndex(parts, p => p.Equals(leaf, StringComparison.OrdinalIgnoreCase));
                if (at >= 0 && at < parts.Length - 1)
                {
                    mapped.Add((entry, string.Join('/', parts[(at + 1)..])));
                }
            }

            if (mapped.Count == 0)
            {
                // No folder named like the live one: the files sit at the top, or under one folder.
                var tops = files.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var strip = tops.Count == 1 && files.All(e => e.FullName.Replace('\\', '/').Contains('/')) ? tops[0].Length + 1 : 0;
                mapped = files.Select(e => (e, e.FullName.Replace('\\', '/')[strip..])).ToList();
            }

            if (!mapped.Any(m => liveFiles.Contains(m.Relative)))
            {
                return $"it doesn't hold a copy of {leaf}.";
            }

            if (mapped.Sum(m => m.Entry.Length) > MaxZipBytes)
            {
                return "it unpacks to more than 2 GB, which is too big for a save.";
            }

            foreach (var (entry, relative) in mapped)
            {
                // Checked like any path from outside (R6): no "..", drive letters or streams can escape the folder.
                RestorePathGuard.CheckRelative(relative);
            }

            foreach (var (entry, relative) in mapped)
            {
                var destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
            }

            return null;
        }
        catch (UnsafePathException e)
        {
            return $"it has an unsafe path in it ({e.Message}), so nothing in it was imported.";
        }
        catch (InvalidDataException)
        {
            return "it isn't a readable zip file.";
        }
    }

    private static HashSet<string> RelativeFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsOtherArchive(string file) => OtherArchives.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
}
