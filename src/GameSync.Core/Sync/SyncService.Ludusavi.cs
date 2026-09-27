using System.IO.Compression;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>ONB-04: Ludusavi's backups brought into a game's history.</summary>
public sealed partial class SyncService
{
    /// <summary>
    /// Brings a game's latest Ludusavi backup in as a named save set aside in history: pinned, never current by itself,
    /// restorable by its name. It takes exactly the files the game's own rules would take from the backup's copy of each
    /// save folder. Without <paramref name="apply"/>, only says what would happen. Ludusavi's folder is never changed.
    /// </summary>
    public Task<ImportReport> ImportLudusaviAsync(GameId game, LudusaviBackupSet set, bool apply, CancellationToken ct) =>
        ImportLudusaviAsync(Main(game), set, apply, ct);

    /// <summary>The same for a game this run doesn't sync yet, such as one confirmed a moment ago. Its definition must be resolved and checked.</summary>
    public Task<ImportReport> ImportLudusaviAsync(GameDefinition game, LudusaviBackupSet set, bool apply, CancellationToken ct) =>
        ImportLudusaviAsync(_streams.FirstOrDefault(s => !s.PerDevice && s.Game.Id == game.Id) ?? StreamsFor(game).First(), set, apply, ct);

    private async Task<ImportReport> ImportLudusaviAsync(SyncStream stream, LudusaviBackupSet set, bool apply, CancellationToken ct)
    {
        if (set.Latest is not { } latest)
        {
            return new ImportReport([], [$"{set.Title}: Ludusavi's folder has no backup in it."], 0);
        }

        var name = $"Ludusavi backup ({latest.WhenUtc.ToLocalTime():yyyy-MM-dd})";
        var temp = Path.Combine(Path.GetTempPath(), "GameSync", $"ludusavi-{Guid.NewGuid():N}");
        try
        {
            var copy = latest.IsZip ? ExtractZip(Path.Combine(set.Folder, latest.Name), temp) : set.CopyOf(latest);

            // Each save folder as the backup holds it.
            var roots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, folder) in stream.Definition.Roots)
            {
                if (set.Locate(folder, copy) is { } backedUp)
                {
                    roots[key] = backedUp;
                }
            }

            var backup = stream.Definition with { Roots = roots, Rules = stream.Definition.Rules.Where(r => roots.ContainsKey(r.Root)).ToList() };
            var snapshot = _scanner.Scan(backup, stream.Includes);
            if (snapshot.Files.Count == 0)
            {
                return new ImportReport([], [$"{set.Title}: none of the files in Ludusavi's backup are ones its save rules take."], 0);
            }

            // A preview of a game new to the cloud reads only this PC, so nothing is made in the cloud for it.
            if (apply || _streams.Contains(stream))
            {
                await TryPullAsync(stream, ct);
            }

            var (versions, _) = await LoadVersionsAsync(stream, ct);
            var same = versions.Where(v => FileSet.SameContent(v.Files, snapshot.Files)).MaxBy(v => v.CreatedUtc);
            var item = new ImportItem(name, copy, snapshot.Files.Count, FileSet.TotalSize(snapshot.Files), NewestChange(snapshot.Files),
                same is null ? null : $"version {same.Id}");
            if (!apply || same is not null)
            {
                return new ImportReport([item], snapshot.Warnings, 0);
            }

            var version = await UploadAsync(stream, snapshot.Files, VersionKind.Kept, VersionOrigin.Imported, null, [], pinned: false, label: name, ct,
                open: file => File.OpenRead(LocalPath(backup, file)));
            await _log.SetPinAsync(stream.Id, new PinRecord(version.Id, name, DateTime.UtcNow, Device, Named: true), ct);
            _state.Log(stream.Id, "info", $"Brought in Ludusavi's backup from {latest.WhenUtc.ToLocalTime():yyyy-MM-dd HH:mm} as '{name}'.");
            var result = await AfterManualAsync(stream, new GameResult(stream.Id, stream.Definition.Title, SyncAction.Upload, StatusFor(stream),
                $"Brought in '{name}'."), ct);
            return new ImportReport([item], [.. snapshot.Warnings, .. result.Warnings], 1);
        }
        finally
        {
            LocalHistory.TryDelete(temp);
        }
    }

    /// <summary>A zipped backup, unpacked to a temporary folder; every entry's path is checked like any path from outside (R6).</summary>
    private static string ExtractZip(string zipPath, string temp)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
        {
            var relative = entry.FullName.Replace('\\', '/');
            RestorePathGuard.CheckRelative(relative);
            var target = RestorePathGuard.Resolve(temp, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
            File.SetLastWriteTimeUtc(target, entry.LastWriteTime.UtcDateTime);
        }

        return temp;
    }
}
