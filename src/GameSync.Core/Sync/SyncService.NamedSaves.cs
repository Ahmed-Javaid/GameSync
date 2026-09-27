using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>A named save (BAK-18): its name, the version it names, and when the save itself was made.</summary>
public sealed record NamedSave(string Name, VersionRecord Version, DateTime SavedUtc, string DeviceName, bool Uploaded);

/// <summary>One kept folder an import found, and what happens to it. <paramref name="SameAs"/> names the save that already holds exactly its files.</summary>
public sealed record ImportItem(string Name, string Source, int Files, long Bytes, DateTime SavedUtc, string? SameAs);

public sealed record ImportReport(IReadOnlyList<ImportItem> Items, IReadOnlyList<string> Skipped, int Added);

/// <summary>
/// Named saves (BAK-18, BAK-19): a save kept under a name like "Before Lady Maria", as a pinned version that syncs to
/// every PC and is never thinned. Names can be changed or removed; removing one leaves the version in history.
/// </summary>
public sealed partial class SyncService
{
    private const int MaxNameLength = 100;

    /// <summary>
    /// Keeps this PC's save as it is now under <paramref name="name"/>. When nothing else changed, it's also the new
    /// current save; when the other PC changed the game too, it's set aside with its name and the next sync decides as
    /// usual. Asking for it is the user's say-so, so a change made outside play isn't held.
    /// </summary>
    public async Task<GameResult> SaveAsAsync(GameId game, string name, CancellationToken ct)
    {
        var stream = Main(game);
        name = CheckName(name);
        await TryPullAsync(stream, ct);
        var snapshot = ScanOrThrowIfUnavailable(stream);
        if (snapshot.Files.Count == 0)
        {
            throw new InvalidOperationException($"{stream.Definition.Title} has no save files on this PC to keep.");
        }

        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var state = _state.GetState(stream.Id);
        var existing = versions
            .Where(v => !thinned.Contains(v.Id) && FileSet.SameContent(v.Files, snapshot.Files))
            .OrderByDescending(v => v.Kind == VersionKind.Normal)
            .ThenByDescending(v => v.CreatedUtc)
            .FirstOrDefault();

        VersionRecord named;
        SyncAction action;
        string message;
        if (existing is not null)
        {
            named = existing;
            action = SyncAction.None;
            message = $"Named this save '{name}'.";
        }
        else
        {
            var heads = VersionGraph.Heads(versions);
            var cloudUnchanged = heads.Count == 0 ||
                (heads.Count == 1 && state.Base is { } basis && FileSet.SameContent(heads[0].Files, basis.Files) && state.Status != GameStatus.Conflict);
            if (cloudUnchanged)
            {
                named = await UploadAsync(stream, snapshot.Files, VersionKind.Normal, VersionOrigin.Manual, heads.FirstOrDefault()?.Id, [],
                    pinned: false, label: name, ct);
                _state.SetBase(stream.Id, named);
                message = $"Saved as '{name}'. It's the current save too.";
            }
            else
            {
                named = await UploadAsync(stream, snapshot.Files, VersionKind.Kept, VersionOrigin.Manual, state.Base?.Id, [],
                    pinned: false, label: name, ct);
                message = $"Saved as '{name}'. The cloud has a different save too, so the next sync decides which one continues; this one keeps its name either way.";
            }

            action = SyncAction.Upload;
        }

        await _log.SetPinAsync(stream.Id, new PinRecord(named.Id, name, DateTime.UtcNow, Device, Named: true), ct);
        _state.Log(stream.Id, "info", $"Named save '{name}' is {named.Id}.");
        return await AfterManualAsync(stream, new GameResult(stream.Id, stream.Definition.Title, action, StatusFor(stream), message)
        {
            NewVersion = named.Id,
            Warnings = snapshot.Warnings,
        }, ct);
    }

    /// <summary>The game's named saves from every PC, newest save first.</summary>
    public async Task<IReadOnlyList<NamedSave>> NamedSavesAsync(GameId game, CancellationToken ct)
    {
        var stream = Main(game);
        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var byId = versions.Where(v => !thinned.Contains(v.Id)).ToDictionary(v => v.Id);
        var pending = _history.PendingVersions(stream.Id).ToHashSet();
        var names = DeviceNames();
        return (await _log.ListPinsAsync(stream.Id, ct))
            .Where(p => p.Named && byId.ContainsKey(p.Version))
            .Select(p =>
            {
                var version = byId[p.Version];
                return new NamedSave(p.Label, version, SavedAt(version), names.GetValueOrDefault(version.Device.Id, version.Device.Name), !pending.Contains(version.Id));
            })
            .OrderByDescending(s => s.SavedUtc)
            .ToList();
    }

    /// <summary>Makes a named save current again, keeping this PC's files first, like any restore (BAK-08).</summary>
    public async Task<GameResult> RestoreNamedAsync(GameId game, string name, CancellationToken ct)
    {
        var save = await FindNamedAsync(Main(game), name, ct);
        return await RestoreAsync(save.Version.Game, save.Version.Id, ct, save.Name);
    }

    public async Task RenameSaveAsync(GameId game, string name, string newName, CancellationToken ct)
    {
        var stream = Main(game);
        var save = await FindNamedAsync(stream, name, ct);
        newName = CheckName(newName);
        await _log.SetPinAsync(stream.Id, new PinRecord(save.Version.Id, newName, DateTime.UtcNow, Device, Named: true), ct);
        await TryPushAsync(stream, ct);
        _state.Log(stream.Id, "info", $"Renamed the named save '{save.Name}' to '{newName}'.");
    }

    /// <summary>Removes a save's name. The version stays in history; unnamed, it can be thinned like any other. Needs the cloud.</summary>
    public async Task ForgetSaveAsync(GameId game, string name, CancellationToken ct)
    {
        var stream = Main(game);
        var save = await FindNamedAsync(stream, name, ct);
        await _log.RemovePinAsync(stream.Id, save.Version.Id, ct);
        _state.Log(stream.Id, "info", $"Removed the name '{save.Name}' from {save.Version.Id}; the version stays in history.");
    }

    /// <summary>
    /// BAK-19: brings in save folders kept by hand next to the live one, as named saves set aside in history. Without
    /// <paramref name="apply"/>, only says what would happen. The kept folders and the current save are never changed.
    /// </summary>
    /// <param name="folder">Where the kept folders are, such as Bloodborne's CUSA00207.</param>
    /// <param name="rootKey">Which of the game's save folders they're copies of, when it has more than one.</param>
    public async Task<ImportReport> ImportSavesAsync(GameId game, string folder, bool apply, string? rootKey, CancellationToken ct)
    {
        var stream = Main(game);
        var definition = stream.Definition;
        var key = rootKey
            ?? (definition.Roots.Count == 1 ? definition.Roots.Keys.Single()
                : definition.Roots.ContainsKey("saves") ? "saves"
                : throw new InvalidOperationException($"{definition.Title} has several save folders; say which one the kept folders are copies of."));
        if (!definition.Roots.TryGetValue(key, out var liveFolder))
        {
            throw new InvalidOperationException($"{definition.Title} has no save folder '{key}'.");
        }

        if (RootResolver.IsUnresolved(liveFolder))
        {
            throw new InvalidOperationException(RootResolver.DescribeUnresolved(definition.Title, liveFolder) + ".");
        }

        if (!Directory.Exists(folder))
        {
            throw new InvalidOperationException($"{folder} doesn't exist.");
        }

        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var stored = versions.Where(v => !thinned.Contains(v.Id)).ToList();
        var pins = (await _log.ListPinsAsync(stream.Id, ct)).ToDictionary(p => p.Version);
        var temp = Path.Combine(Path.GetTempPath(), "GameSync", $"import-{Guid.NewGuid():N}");
        try
        {
            var (found, skippedByFinder) = NamedSaveFinder.Find(folder, liveFolder, temp);
            var skipped = skippedByFinder.ToList();
            var candidates = new List<(FoundSave Save, IReadOnlyList<FileEntry> Files, VersionRecord? Existing, string? SameAs)>();
            foreach (var save in found)
            {
                var copy = definition with
                {
                    Roots = new Dictionary<string, string> { [key] = save.Folder },
                    Rules = definition.Rules.Where(r => r.Root == key).ToList(),
                };
                var snapshot = _scanner.Scan(copy, stream.Includes);
                skipped.AddRange(snapshot.Warnings.Select(w => $"{save.Name}: {w}"));
                if (snapshot.Files.Count == 0)
                {
                    skipped.Add($"{save.Name}: no save files in it once logs and caches are left out.");
                    continue;
                }

                var existing = stored.Where(v => FileSet.SameContent(v.Files, snapshot.Files)).MaxBy(v => v.CreatedUtc);
                var twin = candidates.FirstOrDefault(c => c.Existing is null && c.SameAs is null && FileSet.SameContent(c.Files, snapshot.Files));
                var sameAs = existing is not null
                    ? pins.TryGetValue(existing.Id, out var pin) && pin.Named ? $"'{pin.Label}'" : $"version {existing.Id}"
                    : twin.Save is not null ? $"'{twin.Save.Name}'" : null;
                candidates.Add((save, snapshot.Files, existing, sameAs));
            }

            var items = candidates
                .Select(c => new ImportItem(c.Save.Name, c.Save.Source, c.Files.Count, FileSet.TotalSize(c.Files), NewestChange(c.Files), c.SameAs))
                .ToList();
            if (!apply)
            {
                return new ImportReport(items, skipped, 0);
            }

            // Oldest first, so history reads in the order the saves were made.
            var added = 0;
            foreach (var (save, files, existing, sameAs) in candidates.OrderBy(c => NewestChange(c.Files)))
            {
                if (existing is not null)
                {
                    // Already in history: it only gets the name, unless it has one.
                    if (!(pins.TryGetValue(existing.Id, out var pin) && pin.Named))
                    {
                        var named = new PinRecord(existing.Id, save.Name, DateTime.UtcNow, Device, Named: true);
                        await _log.SetPinAsync(stream.Id, named, ct);
                        pins[existing.Id] = named;
                    }

                    continue;
                }

                if (sameAs is not null)
                {
                    // Holds the same files as another folder in this import; one version keeps one name.
                    continue;
                }

                var version = await UploadAsync(stream, files, VersionKind.Kept, VersionOrigin.Imported, null, [], pinned: false, label: save.Name, ct,
                    open: file => File.OpenRead(Path.Combine(save.Folder, file.Path[(key.Length + 1)..].Replace('/', Path.DirectorySeparatorChar))));
                var record = new PinRecord(version.Id, save.Name, DateTime.UtcNow, Device, Named: true);
                await _log.SetPinAsync(stream.Id, record, ct);
                pins[version.Id] = record;
                stored.Add(version);
                added++;
            }

            _state.Log(stream.Id, "info", $"Imported {added} named saves from {folder}.");
            var result = await AfterManualAsync(stream, new GameResult(stream.Id, definition.Title, SyncAction.Upload, StatusFor(stream),
                $"Imported {added} named saves."), ct);
            return new ImportReport(items, [.. skipped, .. result.Warnings], added);
        }
        finally
        {
            LocalHistory.TryDelete(temp);
        }
    }

    private async Task<NamedSave> FindNamedAsync(SyncStream stream, string name, CancellationToken ct)
    {
        var wanted = name.Trim();
        var saves = await NamedSavesAsync(stream.Game.Id, ct);
        var exact = saves.Where(s => s.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            return exact[0];
        }

        if (exact.Count > 1)
        {
            throw new InvalidOperationException(
                $"{exact.Count} named saves are called '{wanted}': {string.Join(", ", exact.Select(s => s.Version.Id.Value))}. Restore one by its version id, or rename one first.");
        }

        var partial = saves.Where(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1
            ? partial[0]
            : throw new InvalidOperationException(partial.Count == 0
                ? $"{stream.Definition.Title} has no named save called '{wanted}'. Run 'gamesync saves {stream.Game.Id}' to list them."
                : $"'{wanted}' matches {partial.Count} named saves: {string.Join(", ", partial.Select(s => $"'{s.Name}'"))}. Use the full name.");
    }

    private static string CheckName(string name)
    {
        name = name.Trim();
        return name.Length is 0 or > MaxNameLength || name.Any(char.IsControl)
            ? throw new InvalidOperationException($"A save's name is 1 to {MaxNameLength} characters, on one line.")
            : name;
    }

    /// <summary>When the save itself was made: its newest file, not when GameSync stored it.</summary>
    private static DateTime SavedAt(VersionRecord version) => version.Files.Count > 0 ? NewestChange(version.Files) : version.CreatedUtc;

    private static DateTime NewestChange(IReadOnlyList<FileEntry> files) => files.Count > 0 ? files.Max(f => f.ModifiedUtc) : DateTime.MinValue;
}
