using System.Security.Cryptography;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.State;

namespace GameSync.Core.Scanning;

/// <summary>A root folder that couldn't be read. A missing drive or folder never reads as "no saves" (SYNC-12).</summary>
public sealed record RootProblem(string RootKey, string Folder, string Message, bool DriveMissing);

public sealed record Snapshot(IReadOnlyList<FileEntry> Files, IReadOnlyList<string> Warnings, IReadOnlyList<RootProblem> Problems)
{
    /// <summary>R2: the program files found in the save folders, by their full paths, which are never backed up (R1).</summary>
    public IReadOnlyList<string> Programs { get; init; } = [];
}

/// <summary>A save file another program holds open; the game waits and every other game carries on (SYNC-02).</summary>
public sealed class FileInUseException(string path, Exception inner)
    : IOException($"{path} is in use by another program.", inner)
{
    public string FilePath { get; } = path;
}

public sealed class SnapshotScanner(SensitivePathGuard guard, StateStore? hashCache = null)
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>Reads the files of <paramref name="game"/> whose category passes <paramref name="includeCategory"/>.</summary>
    public Snapshot Scan(GameDefinition game, Func<SaveCategory, bool> includeCategory)
    {
        var files = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var problems = new List<RootProblem>();
        var programs = new List<string>();
        var checkedRoots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in game.Rules)
        {
            if (!includeCategory(rule.Category) || (rule.Category == SaveCategory.Screenshots && !game.IncludeScreenshots))
            {
                continue;
            }

            if (!game.Roots.TryGetValue(rule.Root, out var folder))
            {
                throw new InvalidGameDefinitionException($"{game.Title}: a rule uses the root '{rule.Root}', which isn't defined.");
            }

            if (RootResolver.IsUnresolved(folder))
            {
                // Missing, like an unplugged drive: never "no saves" (SYNC-12).
                if (!problems.Any(p => p.RootKey == rule.Root))
                {
                    problems.Add(new RootProblem(rule.Root, folder, RootResolver.DescribeUnresolved(game.Title, folder), DriveMissing: true));
                }

                continue;
            }

            if (checkedRoots.Add(rule.Root) && guard.CheckRoot(folder) is { } refusal)
            {
                throw new InvalidGameDefinitionException($"{game.Title}: {refusal}");
            }

            if (!Directory.Exists(folder))
            {
                var drive = Path.GetPathRoot(Path.GetFullPath(folder));
                var driveMissing = drive is not null && !Directory.Exists(drive);
                var message = driveMissing ? $"Drive {drive!.TrimEnd('\\')} is not connected" : $"Save folder not found: {folder}";
                if (!problems.Any(p => p.RootKey == rule.Root))
                {
                    problems.Add(new RootProblem(rule.Root, folder, message, driveMissing));
                }

                continue;
            }

            var include = new Glob(rule.Include);
            var excludes = rule.Exclude.Select(p => new Glob(p)).ToList();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            };

            foreach (var fullPath in Directory.EnumerateFiles(folder, "*", options))
            {
                var relative = Path.GetRelativePath(folder, fullPath).Replace('\\', '/');
                if (!include.IsMatch(relative) || excludes.Any(e => e.IsMatch(relative)) || DefaultExcludes.Staged.IsMatch(relative))
                {
                    continue;
                }

                if (rule.UseDefaultExcludes && DefaultExcludes.All.Any(e => e.IsMatch(relative)))
                {
                    continue;
                }

                var portable = $"{rule.Root}/{relative}";
                if (files.ContainsKey(portable))
                {
                    continue;
                }

                if (guard.IsBlockedFile(fullPath))
                {
                    warnings.Add($"Skipped {fullPath}: it's inside a protected folder.");
                    continue;
                }

                if (ProgramFileDetector.HasBlockedExtension(fullPath) || ReadEntry(fullPath, portable, rule.Category) is not { } entry)
                {
                    warnings.Add($"Program file in the save folder, not backed up: {fullPath}");
                    programs.Add(fullPath);
                    continue;
                }

                files[portable] = entry;
            }
        }

        return new Snapshot(files.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(), warnings, problems)
        {
            Programs = programs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>Hashes one file, or returns null when its first bytes show it's a program (R1).</summary>
    private FileEntry? ReadEntry(string fullPath, string portable, SaveCategory category)
    {
        var info = new FileInfo(fullPath);
        var modified = info.LastWriteTimeUtc;
        if (hashCache?.TryGetCachedHash(fullPath, info.Length, modified.Ticks) is { } cached)
        {
            return new FileEntry(portable, info.Length, modified, cached, category);
        }

        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            var head = new byte[ProgramFileDetector.HeaderBytes];
            var headLength = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (ProgramFileDetector.LooksLikeProgram(head.AsSpan(0, headLength)))
            {
                return null;
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(head, 0, headLength);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.AppendData(buffer, 0, read);
            }

            var hash = BlobId.FromHash(sha.GetHashAndReset());
            hashCache?.CacheHash(fullPath, info.Length, modified.Ticks, hash);
            return new FileEntry(portable, info.Length, modified, hash, category);
        }
        catch (IOException e) when (e.HResult is SharingViolation or LockViolation)
        {
            throw new FileInUseException(fullPath, e);
        }
    }
}

public sealed class InvalidGameDefinitionException(string message) : Exception(message);
