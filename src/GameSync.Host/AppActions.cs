using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>What the app's screens ask of the engine, the same work as the command line's verbs.</summary>
public static class AppActions
{
    /// <summary>
    /// PLAY-02, PLAY-03: Play from the launcher, as <c>gamesync launch</c>: a game that syncs gets the check before playing
    /// (a newer save from another PC, the game open elsewhere), then starts the way its store does; a game found but not
    /// synced just starts. What goes wrong, and what the check warns about, goes to <paramref name="output"/>.
    /// </summary>
    public static async Task PlayAsync(string dataDir, GameId game, IAgentOutput output)
    {
        try
        {
            await Cli.LaunchAsync(dataDir, [game.Value], output);
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException)
        {
            output.NeedsYou("GameSync", e.Message);
        }
    }

    /// <summary>
    /// FIND-06 from a game's page, as <c>gamesync confirm</c>: Sync these saves pins what the scan found as the game's
    /// rules, or, when another PC's saves recorded theirs, takes those up so both PCs take the same files (PC-04, R8).
    /// The rules pass the same checks as the command line's; the game syncs from the agent's next round.
    /// </summary>
    /// <returns>True when the game now syncs.</returns>
    public static async Task<bool> SyncGameAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct)
    {
        try
        {
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
            using var engine = Engine.Open(dataDir);
            var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null)
                ?? throw new InvalidOperationException($"GameSync doesn't know the game '{game}' on this PC.");
            var history = new LocalHistory(Cli.HistoryFolder(engine.State, dataDir));
            var theirs = entry.Confirmed is null
                ? (await history.Log.ListAsync(game, ct)).Where(v => v.Rules is not null && v.Device.Id != engine.Device.Id).MaxBy(v => v.CreatedUtc)
                : null;
            var confirmed = theirs is not null ? Library.Adopt(entry, theirs.Rules!) : Library.Confirm(entry);
            var portable = confirmed.Confirmed!;
            if (Cli.Problems(portable, engine.Here.Resolver.Resolve(portable), engine.Here) is [var problem, ..])
            {
                output.NeedsYou(entry.DisplayTitle, $"Its saves can't sync yet: {problem}");
                return false;
            }

            engine.Library.SaveAll([confirmed]);
            var mode = portable.Mode == Core.Games.GameMode.BackupOnly ? $" {StoreNames.SyncedBy(entry.Store)}; GameSync keeps a backup of every version." : "";
            output.Say(theirs is not null
                ? $"{entry.DisplayTitle} syncs from now on, with {theirs.Device.Name}'s save rules, so both PCs take the same files.{mode}"
                : $"{entry.DisplayTitle} syncs from now on.{mode}");
            return true;
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException)
        {
            output.NeedsYou("GameSync", e.Message);
            return false;
        }
    }

    /// <summary>BAK-16: Back up now, from a game's history, as <c>gamesync backup</c>; never downloads.</summary>
    public static Task BackUpNowAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, service => service.BackupNowAsync(game, ct));

    /// <summary>BAK-18: Save as…, as <c>gamesync save</c>: this PC's save as it is now, kept under a name on every PC.</summary>
    public static Task SaveAsAsync(string dataDir, GameId game, string name, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service => [await service.SaveAsAsync(game, name, ct)]);

    /// <summary>
    /// Restore from a game's page, a named save or any version, as <c>gamesync restore</c>: this PC's files are kept as a
    /// version first (BAK-08), files go only into the game's own folders (R3), and never while the game runs.
    /// </summary>
    public static Task RestoreAsync(string dataDir, GameId game, VersionId version, string? name, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service => [await service.RestoreAsync(game, version, ct, name)]);

    /// <summary>BAK-12: keeps a save held for review as the game's current one, as <c>gamesync approve</c> does.</summary>
    public static Task ApproveAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service => [await service.ApproveAsync(game, ct)]);

    /// <summary>
    /// SYNC-10, SYNC-11: settles a waiting conflict the person's way, as <c>gamesync resolve</c> does: this PC's save, or
    /// the cloud's (<paramref name="cloudVersion"/>, when more than one PC's is current). The other stays pinned in history.
    /// </summary>
    public static Task ResolveAsync(string dataDir, GameId game, bool keepThisPc, VersionId? cloudVersion, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service => [await service.ResolveAsync(game, keepThisPc, cloudVersion, ct)]);

    /// <summary>BAK-18: gives a named save another name, on every PC, as <c>gamesync rename-save</c> does.</summary>
    public static Task RenameSaveAsync(string dataDir, GameId game, string name, string newName, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service =>
        {
            await service.RenameSaveAsync(game, name, newName, ct);
            output.Say($"Renamed the named save '{name}' to '{newName}'.");
            return [];
        });

    /// <summary>BAK-18: takes a save's name away, as <c>gamesync forget-save</c> does; the save stays in the history.</summary>
    public static Task ForgetSaveAsync(string dataDir, GameId game, string name, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service =>
        {
            await service.ForgetSaveAsync(game, name, ct);
            output.Say($"The name '{name}' is gone; the save stays in the history.");
            return [];
        });

    /// <summary>
    /// BAK-19: save folders kept by hand, as <c>gamesync import-saves</c> brings them in: without <paramref name="apply"/>,
    /// what they'd become; with it, they become named saves. The folders themselves are never changed. What goes wrong
    /// is thrown, for the dialog to say.
    /// </summary>
    /// <param name="root">The game's place they're copies of, when it has more than one.</param>
    public static async Task<ImportReport> KeptSavesAsync(string dataDir, GameId game, string folder, string? root, bool apply, IAgentOutput output, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var running = new RunningGames(engine);
        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = running.IsRunning }, ct, recover: false);
        var report = await service.ImportSavesAsync(game, folder, apply, root, ct);
        if (apply)
        {
            output.Say($"Imported {report.Added} kept {(report.Added == 1 ? "save" : "saves")} from {folder} as named saves; the folders weren't changed.");
        }

        return report;
    }

    /// <summary>SYNC-04: switches to the save that lost the game's last conflict, as <c>gamesync swap</c> does.</summary>
    public static Task SwapAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, async service => [await service.SwapAsync(game, ct)]);

    /// <summary>Syncs one game again, for a game whose files were in use or whose folder was missing (SYNC-02).</summary>
    public static Task RetryAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct) =>
        WithServiceAsync(dataDir, output, ct, service => service.SyncAsync([game], ct));

    /// <summary>
    /// A job on one game, the way a command runs it: it waits while the agent finishes a sync (BG-09), opens the sync
    /// service, does the job, and says how it went as the agent does.
    /// </summary>
    private static async Task WithServiceAsync(string dataDir, IAgentOutput output, CancellationToken ct, Func<SyncService, Task<IReadOnlyList<GameResult>>> job)
    {
        try
        {
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
            using var engine = Engine.Open(dataDir);
            var running = new RunningGames(engine);
            var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = running.IsRunning }, ct, recover: false);
            Agent.Report(output, await job(service));
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException or CloudException or UnauthorizedAccessException)
        {
            output.NeedsYou("GameSync", e.Message);
        }
    }
}
