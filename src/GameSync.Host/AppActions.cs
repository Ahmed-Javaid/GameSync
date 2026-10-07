using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>What the app's screens ask of the engine, the same work as the command line's verbs.</summary>
public static class AppActions
{
    /// <summary>
    /// KAN-88: how long a person's action waits to hear what's new in the cloud before going on with what this PC knows,
    /// so a named save or a restore takes no more than about 2 seconds whatever Google Drive does meanwhile.
    /// </summary>
    internal static readonly TimeSpan PullBudget = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// KAN-88: a person's action in the app does its work on this PC and the agent uploads it beside its rounds, so
    /// nothing the person does waits on the network; the app asks the agent for the upload when the action is done.
    /// </summary>
    /// <param name="progress">KAN-80: what the action brings down from the cloud, as it comes (a restore of another PC's save).</param>
    internal static SyncOptions ForAction(Func<GameId, bool> isRunning, IProgress<TransferProgress>? progress = null) =>
        new() { IsRunning = isRunning, DeferUploads = true, PullBudget = PullBudget, Progress = progress };

    /// <summary>
    /// PLAY-02, PLAY-03: Play from the launcher, as <c>gamesync launch</c>: a game that syncs gets the check before playing
    /// (a newer save from another PC, the game open elsewhere), then starts the way its store does; a game found but not
    /// synced just starts. What goes wrong, and what the check warns about, goes to <paramref name="output"/>.
    /// </summary>
    /// <returns>True once the game is on its way; false when it couldn't start, which <paramref name="output"/> says why.</returns>
    public static async Task<bool> PlayAsync(string dataDir, GameId game, IAgentOutput output)
    {
        try
        {
            return await Cli.LaunchAsync(dataDir, [game.Value], output) == 0;
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException)
        {
            output.NeedsYou("GameSync", e.Message);
            return false;
        }
    }

    /// <summary>
    /// FIND-06 from a game's page, as <c>gamesync confirm</c>: Sync these saves pins what the scan found as the game's
    /// rules, or, when another PC's saves recorded theirs, takes those up so both PCs take the same files (PC-04, R8).
    /// The rules pass the same checks as the command line's; the game syncs from the agent's next round.
    /// </summary>
    /// <param name="backupOnly">
    /// KAN-63: keep its saves without syncing them between PCs (back up only): what Save as…, Back up now and Import kept
    /// saves do first for a game not syncing yet. Sync these saves, later, makes it sync between PCs.
    /// </param>
    /// <returns>True when the game now syncs, or is kept.</returns>
    public static async Task<bool> SyncGameAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct, bool backupOnly = false)
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
            var defaults = Core.Games.GameDefaults.Load(engine.State);
            var confirmed = theirs is not null ? Library.Adopt(entry, theirs.Rules!, defaults) : Library.Confirm(entry, backupOnly ? Core.Games.GameMode.BackupOnly : null, defaults);
            if (backupOnly && confirmed.Confirmed is { } rules)
            {
                confirmed = confirmed with { Confirmed = rules with { Mode = Core.Games.GameMode.BackupOnly } };
            }

            var portable = confirmed.Confirmed!;
            if (Cli.Problems(portable, engine.Here.Resolver.Resolve(portable), engine.Here) is [var problem, ..])
            {
                output.NeedsYou(entry.DisplayTitle, $"Its saves can't sync yet: {problem}");
                return false;
            }

            engine.Library.SaveAll([confirmed]);
            if (backupOnly && !entry.StoreCloud)
            {
                output.Say($"GameSync keeps {entry.DisplayTitle}'s saves from now on, backed up on this PC and in the cloud; they don't sync between your PCs until you choose Sync these saves.");
                return true;
            }

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
    public static async Task BackUpNowAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct)
    {
        if (await KeepAsync(dataDir, game, output, ct))
        {
            await WithServiceAsync(dataDir, output, ct, service => service.BackupNowAsync(game, ct));
        }
    }

    /// <summary>
    /// BAK-18: New named save…, as <c>gamesync save</c>: this PC's save as it is now, kept under a name on every PC. Null
    /// once it's kept, or why it couldn't be, for the dialog to say (KAN-77).
    /// </summary>
    public static async Task<string?> SaveAsAsync(string dataDir, GameId game, string name, IAgentOutput output, CancellationToken ct) =>
        await KeepAsync(dataDir, game, output, ct)
            ? await WithServiceAsync(dataDir, output, ct, async service => [await service.SaveAsAsync(game, name, ct)])
            : "GameSync couldn't start keeping its saves on this PC; the log says why.";

    /// <summary>
    /// KAN-63: a game not syncing yet is kept first (backed up only, not synced between PCs), so its saves can be named,
    /// backed up and imported before the person chooses to sync them. True when the game syncs or is kept.
    /// </summary>
    public static async Task<bool> KeepAsync(string dataDir, GameId game, IAgentOutput output, CancellationToken ct)
    {
        bool known;
        using (var engine = Engine.OpenForSetup(dataDir))
        {
            known = engine.Games.Any(g => g.Id == game);
        }

        return known || await SyncGameAsync(dataDir, game, output, ct, backupOnly: true);
    }

    /// <summary>
    /// Restore from a game's page, a named save or any version, as <c>gamesync restore</c>: this PC's files are kept as a
    /// version first (BAK-08), files go only into the game's own folders (R6) after your antivirus has seen them (R3), and a file the game holds stops it with nothing changed (KAN-91). Null once
    /// it's in place, or why it couldn't be, for the page to say (KAN-51).
    /// </summary>
    public static Task<string?> RestoreAsync(string dataDir, GameId game, VersionId version, string? name, IAgentOutput output, CancellationToken ct) =>
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
    /// <param name="progress">How far it is (KAN-80): each copy read, then each one kept.</param>
    public static async Task<ImportReport> KeptSavesAsync(string dataDir, GameId game, string folder, string? root, bool apply, IAgentOutput output, CancellationToken ct,
        IProgress<WorkProgress>? progress = null)
    {
        if (!await KeepAsync(dataDir, game, output, ct))
        {
            throw new InvalidOperationException("Its saves can't be kept yet; the log says why.");
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var running = new RunningGames(engine);
        var (service, _) = await engine.OpenServiceAsync(ForAction(running.IsRunning), ct, recover: false);
        var report = await service.ImportSavesAsync(game, folder, apply, root, ct, progress);
        if (apply)
        {
            output.Say($"Imported {report.Added} kept {(report.Added == 1 ? "save" : "saves")} from {folder} as named saves; the folders weren't changed.");
        }

        return report;
    }

    /// <summary>
    /// KAN-87: a named save or any version as a plain folder in <paramref name="parent"/>, named after it; what came down
    /// from the cloud first shows on the game's saves (KAN-80). The folder made, or why it couldn't be.
    /// </summary>
    public static async Task<(string? Folder, string? Problem)> ExportFolderAsync(string dataDir, GameId game, VersionId version, string? name, string parent,
        IAgentOutput output, CancellationToken ct)
    {
        var relay = new TransferRelay(output);
        try
        {
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
            using var engine = Engine.Open(dataDir);
            var (service, _) = await engine.OpenServiceAsync(ForAction(new RunningGames(engine).IsRunning, relay), ct, recover: false);
            relay.TitleOf = id => service.Streams.FirstOrDefault(s => s.Game.Id == id)?.Definition.Title;
            var export = await service.ExportFolderAsync(game, version, parent, name, ct);
            relay.End([], null);
            var title = relay.TitleOf(game) ?? game.Value;
            foreach (var note in export.Notes)
            {
                output.Say($"{title}: {note}");
            }

            output.Say($"{title}: exported {(name is null ? "a save" : $"'{name}'")} as a folder, {export.Folder}.");
            return (export.Folder, null);
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException or CloudException or UnauthorizedAccessException
            or BlockedException)
        {
            relay.Stop(TransferState.Failed, TransferRelay.Failed(e.Message, null));
            return (null, e.Message);
        }
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
    /// <returns>Null when the job ran; otherwise why it couldn't, which the log shows too.</returns>
    private static async Task<string?> WithServiceAsync(string dataDir, IAgentOutput output, CancellationToken ct, Func<SyncService, Task<IReadOnlyList<GameResult>>> job)
    {
        // KAN-80: what a restore brings down from the cloud shows on the game's saves as it comes.
        var relay = new TransferRelay(output);
        try
        {
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
            using var engine = Engine.Open(dataDir);
            var running = new RunningGames(engine);
            var (service, _) = await engine.OpenServiceAsync(ForAction(running.IsRunning, relay), ct, recover: false);
            relay.TitleOf = game => service.Streams.FirstOrDefault(s => s.Game.Id == game)?.Definition.Title;
            var results = await job(service);
            relay.End(results, null);
            Agent.Report(output, results);
            return null;
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException or IOException or CloudException or UnauthorizedAccessException
            or Core.Scanning.InvalidGameDefinitionException or BlockedException or Core.Safety.UnsafePathException)
        {
            // A restore the safety rules turn away (R1, R3, R6, R7, R16) says why on the game, as any other refusal does,
            // rather than leaving the app's job.
            relay.Stop(TransferState.Failed, TransferRelay.Failed(e.Message, null));
            output.NeedsYou("GameSync", e.Message);
            return e.Message;
        }
    }
}
