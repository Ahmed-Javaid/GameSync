using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>
/// One game's saves at a glance, for the save manager's table (design system → SaveManagerScreen): where they are on
/// this PC, how many versions are kept and the space they take, and when the current one was made and where.
/// </summary>
/// <param name="Folder">The first place it keeps saves, on this PC; null when this PC can't place it.</param>
public sealed record GameSaveSummary(GameId Id, string? Folder, int Versions, long HistoryBytes, DateTime? LastBackupUtc, string? LastBackupPc);

/// <summary>Reads every game's saves at a glance from a data folder: the backup folder's copy of the records, so it's quick and works offline.</summary>
public static class SaveOverview
{
    public static async Task<IReadOnlyList<GameSaveSummary>> ReadAsync(string dataDir, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        var history = new LocalHistory(Cli.HistoryFolder(engine.State, dataDir));
        var pcs = history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        var summaries = new List<GameSaveSummary>();
        foreach (var game in engine.Games)
        {
            ct.ThrowIfCancellationRequested();
            var thinned = await history.Log.ListThinnedAsync(game.Id, ct);
            var versions = (await history.Log.ListAsync(game.Id, ct)).Where(v => !thinned.Contains(v.Id)).ToList();
            var heads = VersionGraph.Heads(versions).Select(v => v.Id).ToHashSet();
            var current = versions.Where(v => heads.Contains(v.Id)).MaxBy(v => v.CreatedUtc);
            summaries.Add(new GameSaveSummary(
                game.Id,
                FolderOf(game),
                versions.Count,
                versions.SelectMany(v => v.Files).DistinctBy(f => f.Hash).Sum(f => f.Size),
                current is null ? null : current.Files.Count > 0 ? current.Files.Max(f => f.ModifiedUtc) : current.CreatedUtc,
                current is null ? null : pcs.GetValueOrDefault(current.Device.Id, current.Device.Name)));
        }

        // Games found but not syncing yet: where the scan found their saves.
        foreach (var entry in engine.Library.All().Where(e => e.State == LibraryState.Found && e.MergedInto is null && summaries.All(s => s.Id != e.Id)))
        {
            var found = entry.Proposals.OrderByDescending(p => p.Category == SaveCategory.Save).ThenByDescending(p => p.Files).FirstOrDefault();
            var folder = found is null ? null
                : engine.Here.Resolver.Resolve(new GameDefinition { Id = entry.Id, Title = entry.DisplayTitle, Roots = new Dictionary<string, string> { ["r"] = found.Root }, Rules = [] }).Roots["r"];
            summaries.Add(new GameSaveSummary(entry.Id, folder is null || RootResolver.IsUnresolved(folder) ? null
                : Directory.Exists(folder) ? GameDetails.Deepest(folder, found!.Include) : folder, 0, 0, null, null));
        }

        return summaries;
    }

    /// <summary>Where a game's saves are on this PC: its first save rule's folder, as deep as its pattern names.</summary>
    private static string? FolderOf(GameDefinition game)
    {
        var rule = game.Rules.Where(r => r.Root != GameDefinition.RegistryRoot)
            .OrderByDescending(r => r.Category == SaveCategory.Save)
            .FirstOrDefault(r => game.Roots.GetValueOrDefault(r.Root) is { } f && !RootResolver.IsUnresolved(f));
        if (rule is null || game.Roots.GetValueOrDefault(rule.Root) is not { } folder)
        {
            return null;
        }

        return Directory.Exists(folder) ? GameDetails.Deepest(folder, rule.Include) : folder;
    }
}
