using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>A version exported as a plain folder (KAN-87): where it went, and what was left out and why.</summary>
public sealed record FolderExport(string Folder, IReadOnlyList<string> Notes);

/// <summary>Exporting a save as a plain folder.</summary>
public sealed partial class SyncService
{
    /// <summary>
    /// KAN-87 (the owner, 1 Oct 2026: "I wanted to know where it's saved"): a named save or any version as a plain folder,
    /// for a person who wants to see it or keep it by hand. A folder named after it goes in <paramref name="parent"/>,
    /// holding each of the game's save folders under its own name with the files as the game keeps them, each with its
    /// own date: <c>D:\Saves\befo ludwig\SPRJ0005\userdata0000</c>. A version only the cloud keeps comes down first. A
    /// program is left out and said so (R1), and nothing is written outside the new folder (R6). It's written beside its
    /// place and only named once whole, so a cancelled or failed export leaves nothing behind; it never writes over
    /// anything, taking "befo ludwig (2)" when the name is taken.
    /// </summary>
    /// <param name="name">What to call the folder: the named save's name; the save's date when null.</param>
    public async Task<FolderExport> ExportFolderAsync(GameId game, VersionId version, string parent, string? name, CancellationToken ct)
    {
        var stream = Main(game);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var record = versions.FirstOrDefault(v => v.Id == version)
            ?? throw new InvalidOperationException($"{stream.Definition.Title} has no version {version} on this PC.");
        if ((await _log.ListThinnedAsync(stream.Id, ct)).Contains(version))
        {
            throw new InvalidOperationException($"That save of {stream.Definition.Title} was thinned; its files are gone.");
        }

        if (!Directory.Exists(parent))
        {
            throw new InvalidOperationException($"{parent} isn't there.");
        }

        var saved = SavedAt(record).ToLocalTime();
        var baseName = FolderName(name ?? $"{stream.Definition.Title} {saved:yyyy-MM-dd HH.mm}", record.Id.Value);
        var target = Path.Combine(parent, baseName);
        for (var n = 2; Directory.Exists(target) || File.Exists(target); n++)
        {
            target = Path.Combine(parent, $"{baseName} ({n})");
        }

        // KAN-80: a save made on another PC comes down first, saying how far it is.
        await _store.FetchAsync(stream.Id, record.Files, _options.Progress, ct);

        var folders = RootFolders(stream, record.Files);
        var part = $"{target}.part-{Guid.NewGuid().ToString("N")[..6]}";
        var inside = Path.GetFullPath(part) + Path.DirectorySeparatorChar;
        var notes = new List<string>();
        Directory.CreateDirectory(part);
        try
        {
            foreach (var file in record.Files)
            {
                ct.ThrowIfCancellationRequested();
                var (rootKey, relative) = RestorePathGuard.Split(file.Path);
                var path = Path.GetFullPath(Path.Combine(part, folders[rootKey], relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"{file.Path} was left out: its path leads outside the folder.");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await StageAsync(stream.Id, file, path, restoring: false, ct);
                if (ProgramFileDetector.IsProgramFile(path))
                {
                    File.Delete(path);
                    notes.Add($"{file.Path} was left out: it's a program, not a save.");
                    continue;
                }

                File.SetLastWriteTimeUtc(path, file.ModifiedUtc);
            }

            Directory.Move(part, target);
        }
        catch
        {
            LocalHistory.TryDelete(part);
            throw;
        }

        var what = name is null ? $"the save from {saved:d MMM HH:mm}" : $"'{name}'";
        _state.Log(stream.Id, "info", $"Exported {what} as a folder: {target}.", EventTags.Share);
        return new FolderExport(target, notes);
    }

    /// <summary>
    /// Each save folder of a version by the name it has on this PC (SPRJ0005, SaveGames), each name once; a folder this
    /// PC can't place goes by its key, and the registry's export by Registry.
    /// </summary>
    private static Dictionary<string, string> RootFolders(SyncStream stream, IEnumerable<FileEntry> files)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in files.Select(f => RestorePathGuard.Split(f.Path).RootKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var leaf = key == GameDefinition.RegistryRoot ? "Registry"
                : stream.Definition.Roots.TryGetValue(key, out var root) && !RootResolver.IsUnresolved(root)
                    ? Path.GetFileName(Path.TrimEndingDirectorySeparator(root))
                    : key;
            var name = FolderName(leaf, key);
            var unique = name;
            for (var n = 2; !taken.Add(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            folders[key] = unique;
        }

        return folders;
    }
}
