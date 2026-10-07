using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

public sealed record SyncOptions
{
    /// <summary>How much of the history's file contents stays on this PC (BAK-17).</summary>
    public HistoryLimits HistoryLimits { get; init; } = new();

    /// <summary>Recorded with this PC in the cloud's device list (PC-01).</summary>
    public string AppVersion { get; init; } = "dev";

    /// <summary>How far this PC's clock may be off the cloud's before newest-wins turns off (SYNC-08).</summary>
    public TimeSpan ClockTolerance { get; init; } = TimeSpan.FromMinutes(2);

    public IProgress<TransferProgress>? Progress { get; init; }

    /// <summary>Where registry saves are read and written (FIND-10); without it, registry exports already on disk still sync.</summary>
    public IRegistryStore? Registry { get; init; }

    /// <summary>Whether any of a game's programs runs now (BAK-10); without it, no game counts as running.</summary>
    public Func<GameId, bool>? IsRunning { get; init; }

    /// <summary>This PC's clock, which a test can move on.</summary>
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// A note for the saves this run backs up and makes current, as a game's history and the Versions tab show them
    /// ("Daily backup", MGR-08); held saves keep their own. Null for a sync after play.
    /// </summary>
    public string? UploadLabel { get; init; }

    /// <summary>
    /// KAN-88: this run does its work on this PC and leaves the upload (the outbox, the plain latest copy, this PC's
    /// device record, the restore kit) to <see cref="SyncService.UploadAsync"/>, which runs beside it, so the person's
    /// next move never waits on the network. False, as the command line runs, uploads before it returns.
    /// </summary>
    public bool DeferUploads { get; init; }

    /// <summary>
    /// KAN-88: held around every upload, so two never run at once, in this process or another (the upload lock); an
    /// upload waits for one already under way. Null holds nothing.
    /// </summary>
    public Func<CancellationToken, Task<IDisposable>>? UploadGate { get; init; }

    /// <summary>
    /// KAN-88: how long a person's action (a named save, a restore, an import, a game's saves) waits to hear what's
    /// new in the cloud before going on with what this PC knows, as it does offline. Null waits as long as it takes,
    /// as a sync, which decides from it, always does.
    /// </summary>
    public TimeSpan? PullBudget { get; init; }

    /// <summary>
    /// R16: why a game's saves stay with the account that made them (it has an anti-cheat, or plays only online), so a
    /// save someone shared is never restored for it; null when they can move. Without it, nothing is refused.
    /// </summary>
    public Func<GameId, string?>? StaysWithAccount { get; init; }
}

/// <summary>
/// Plans and runs syncs one game at a time. Everything lands in the backup folder on this PC first and uploads from
/// there, so a sync works offline and nothing waits on the network (PC-05). A failure stays on its own game (SYNC-02);
/// uploads write file contents before the version record (BAK-12); restores stage, check and journal every file
/// before swapping (BAK-08).
/// </summary>
public sealed partial class SyncService
{
    private const int Parallelism = 4;

    private readonly IReadOnlyList<SyncStream> _streams;
    private readonly LocalFirstStore _store;
    private readonly LocalHistory _history;
    private readonly ICloud _cloud;
    private readonly IBlobStore _blobs;
    private readonly IVersionLog _log;
    private readonly StateStore _state;
    private readonly SnapshotScanner _scanner;
    private readonly IMalwareScanner _malware;
    private readonly SyncOptions _options;
    private readonly string _dataDir;
    private readonly List<string> _notices = [];
    private string? _cloudWhere;

    public SyncService(
        IEnumerable<GameDefinition> games,
        LocalHistory history,
        ICloud cloud,
        StateStore state,
        SnapshotScanner scanner,
        IMalwareScanner malware,
        DeviceInfo device,
        string dataDir,
        SyncOptions? options = null)
    {
        Device = device;
        _history = history;
        _cloud = cloud;
        _store = new LocalFirstStore(history, cloud);
        _blobs = _store;
        _log = _store;
        _state = state;
        _scanner = scanner;
        _malware = malware;
        _dataDir = dataDir;
        _options = options ?? new SyncOptions();
        _streams = games.SelectMany(StreamsFor).ToList();
    }

    public DeviceInfo Device { get; }

    public IReadOnlyList<SyncStream> Streams => _streams;

    /// <summary>What's worth saying once per run rather than per game: offline, a wrong clock, Drive filling up.</summary>
    public IReadOnlyList<string> Notices => _notices;

    // ---- planning and syncing ----

    /// <summary>Copies what's new in the cloud into the backup folder, then plans each game without acting (SYNC-14).</summary>
    /// <param name="whilePlaying">KAN-91: a person's Back up now: a running game's files are read now too.</param>
    public async Task<IReadOnlyList<GamePlan>> PlanAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct, bool whilePlaying = false)
    {
        var streams = Select(only).ToList();
        var views = await PrepareAsync(streams, ct);
        var shared = SharedSaves();
        var plans = new List<GamePlan>();
        foreach (var stream in streams)
        {
            var (view, warnings, problem) = views[stream.Id];
            if (problem is null && shared.TryGetValue(stream.Game.Id, out var clash))
            {
                problem = new InvalidGameDefinitionException($"{stream.Game.Title}: {clash}");
            }

            plans.Add(problem is not null
                ? new GamePlan { Stream = stream, Error = problem, Cloud = view }
                : await TryPlanAsync(stream, treatAsInSession: false, view, warnings, ct, whilePlaying));
        }

        return plans;
    }

    /// <summary>
    /// Runs each plan exactly as planned (SYNC-14); a game that changed since planning is planned again, and its result
    /// says so. Afterwards each game's outbox uploads, and the backup folder is trimmed to its limits.
    /// </summary>
    public async Task<IReadOnlyList<GameResult>> ExecuteAsync(IReadOnlyList<GamePlan> plans, CancellationToken ct)
    {
        // KAN-88: uploading here, the run holds the upload gate throughout; when the upload comes after, it touches
        // nothing in the cloud but what deciding needs.
        var defer = _options.DeferUploads;
        using var gate = defer ? null : await UploadGateAsync(ct);
        var online = plans.Any(p => p.Error is null && p.Cloud.Problem is null);
        if (online && !defer)
        {
            await TryAnnounceDeviceAsync(ct);
        }

        var results = new List<GameResult>();
        foreach (var plan in plans)
        {
            var job = _state.StartJob("sync", plan.Stream.Id);
            try
            {
                if (plan.Error is not null)
                {
                    throw plan.Error;
                }

                // BG-08: while the game runs, not even its outbox uploads; a person's Back up now keeps its save here (KAN-91).
                var playingNow = IsRunning(plan.Stream.Game.Id);
                if (plan.Decision?.Action == SyncAction.Playing || (playingNow && !plan.WhilePlaying))
                {
                    var playing = new GameResult(plan.Stream.Id, plan.Title, SyncAction.Playing, GameStatus.Playing, "Playing: it syncs once the game closes.");
                    _state.SetStatus(plan.Stream.Id, GameStatus.Playing, playing.Message);
                    _state.FinishJob(job);
                    results.Add(playing);
                    continue;
                }

                // Whatever earlier runs left in the outbox goes first; a game being played keeps it until it closes.
                var uploads = !defer && !playingNow;
                (CloudException? Problem, IReadOnlyList<string> Warnings) upload = plan.Cloud.Problem is null && uploads
                    ? await TryPushAsync(plan.Stream, ct)
                    : (plan.Cloud.Problem, []);

                var current = plan;
                var fresh = await PlanStreamAsync(plan.Stream, plan.TreatAsInSession, plan.Cloud, plan.Warnings, ct, plan.WhilePlaying);
                var changed = fresh.Fingerprint != plan.Fingerprint;
                if (changed)
                {
                    current = fresh;
                }

                var result = await RunAsync(current, ct);
                if (changed)
                {
                    result = result with { Message = $"Changed since the plan, so it was planned again: {result.Message}" };
                }

                if (plan.Cloud.Problem is null && upload.Problem is null && uploads)
                {
                    upload = await TryPushAsync(plan.Stream, ct);
                    if (upload.Problem is null)
                    {
                        await TryWriteLatestAsync(plan.Stream, ct);
                    }
                }

                result = Settle(plan.Stream, result with { Warnings = [.. result.Warnings, .. upload.Warnings] },
                    plan.Cloud.Problem is null ? upload.Problem : null, log: true) with { CloudProblem = (plan.Cloud.Problem ?? upload.Problem)?.Kind };
                _state.FinishJob(job);
                results.Add(result);
            }
            catch (SimulatedCrashException)
            {
                throw;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _state.FailJob(job, e.Message);
                results.Add(Fail(plan, e));
            }
        }

        if (online && !defer)
        {
            await TryWriteRestoreKitAsync(ct);
        }

        await PruneHistoryAsync(ct);
        return results;
    }

    /// <summary>
    /// KAN-88: the upload half of runs that left it to come after (<see cref="SyncOptions.DeferUploads"/>). Each game's
    /// outbox goes up, oldest first, then its plain latest copy, and its status settles: uploaded, or waiting with the
    /// reason; then this PC's device record and the restore kit. It holds the upload gate, not the engine, so syncs and
    /// the person's actions carry on beside it, and a game being played waits for its session to end (BG-08).
    /// </summary>
    /// <returns>A result for each game that had something to upload, or that couldn't.</returns>
    public async Task<IReadOnlyList<GameResult>> UploadAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct)
    {
        using var gate = await UploadGateAsync(ct);
        var results = new List<GameResult>();
        var reached = false;
        foreach (var stream in Select(only))
        {
            if (IsRunning(stream.Game.Id))
            {
                continue;
            }

            var waiting = _history.HasPending(stream.Id);
            var (problem, warnings) = await TryPushAsync(stream, ct);
            if (problem is null)
            {
                reached |= waiting;
                await TryWriteLatestAsync(stream, ct);
            }

            if (waiting || problem is not null)
            {
                results.Add(SettleUpload(stream, problem, warnings));
            }
        }

        if (reached)
        {
            await TryAnnounceDeviceAsync(ct);
            await TryWriteRestoreKitAsync(ct);
        }

        return results;
    }

    /// <summary>
    /// A game's status once its upload has run: still waiting, with why (said once in the log, not at every retry), or
    /// back to synced once nothing waits; anything else it was (a conflict, a held save) stays.
    /// </summary>
    private GameResult SettleUpload(SyncStream stream, CloudException? problem, IReadOnlyList<string> warnings)
    {
        var current = _state.GetState(stream.Id);
        var status = current.Status ?? StatusFor(stream);
        var message = current.Detail ?? "";
        if (_history.HasPending(stream.Id))
        {
            if (problem is not null)
            {
                message = $"Kept on this PC; the upload waits: {Describe(problem)}";
                if (status is GameStatus.Synced or GameStatus.BackupOnly or GameStatus.UploadPending)
                {
                    status = GameStatus.UploadPending;
                }

                if (current.Detail != message)
                {
                    _state.Log(stream.Id, "warn", message, EventTags.Cloud);
                }
            }
        }
        else if (status == GameStatus.UploadPending)
        {
            status = StatusFor(stream);
            message = "Uploaded to the cloud.";
        }

        _state.SetStatus(stream.Id, status, message);
        return new GameResult(stream.Id, stream.Definition.Title, SyncAction.None, status, message)
        {
            Warnings = warnings,
            CloudProblem = problem?.Kind,
        };
    }

    private async Task<IDisposable?> UploadGateAsync(CancellationToken ct) =>
        _options.UploadGate is { } gate ? await gate(ct) : null;

    /// <summary>A change of names or pins reaches the cloud now, or with the upload that comes after (KAN-88).</summary>
    private async Task UploadUnlessDeferredAsync(SyncStream stream, CancellationToken ct)
    {
        if (_options.DeferUploads)
        {
            return;
        }

        using var gate = await UploadGateAsync(ct);
        await TryPushAsync(stream, ct);
    }

    public async Task<IReadOnlyList<GameResult>> SyncAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct) =>
        await ExecuteAsync(await PlanAsync(only, ct), ct);

    /// <summary>Finishes restores and jobs a crash or reboot interrupted (BAK-08, BAK-13). Call once when GameSync starts.</summary>
    public async Task<IReadOnlyList<GameResult>> RecoverAsync(CancellationToken ct)
    {
        foreach (var journal in RestoreJournal.RecoverAll(_dataDir))
        {
            // A restore's version is recorded once its files are in place (KAN-91): one a crash cut off gets it now.
            if (_streams.Any(s => s.Id == journal.Game) && await _log.GetAsync(journal.Game, journal.Target.Id, ct) is null)
            {
                await _log.AppendAsync(journal.Game, journal.Target, ct);
            }

            _state.SetBase(journal.Game, journal.Target);
            _state.Log(journal.Game, "info", "Finished a restore that was interrupted.", EventTags.Restore);
            journal.Delete(_dataDir);
        }

        var jobs = _state.GetUnfinishedJobs();
        foreach (var job in jobs)
        {
            _state.SupersedeJob(job.Id);
        }

        var games = jobs.Select(j => j.Game).Distinct().Where(g => _streams.Any(s => s.Id == g)).ToList();
        return games.Count == 0 ? [] : await SyncAsync(games, ct);
    }

    // ---- manual actions ----

    /// <summary>BAK-16: backs up any game now, Backup-only ones included. It never downloads.</summary>
    public async Task<IReadOnlyList<GameResult>> BackupNowAsync(GameId game, CancellationToken ct)
    {
        // KAN-91: it works while the game runs, as New named save does: the save as it is now is kept here, and goes up
        // once the game closes.
        var plans = await PlanAsync([game], ct, whilePlaying: true);
        var results = new List<GameResult>();
        foreach (var plan in plans)
        {
            if (plan.Decision?.Action is SyncAction.Download or SyncAction.AdoptHead)
            {
                results.Add(new GameResult(plan.Stream.Id, plan.Title, SyncAction.None, _state.GetState(plan.Stream.Id).Status ?? GameStatus.Synced,
                    "Nothing new to back up here; the cloud has a newer save. Sync to get it."));
                continue;
            }

            results.AddRange(await ExecuteAsync([plan], ct));
        }

        return results;
    }

    /// <summary>BAK-11: the held changes count as made during play and sync normally, conflicts included.</summary>
    public async Task<GameResult> ApproveAsync(GameId game, CancellationToken ct)
    {
        var stream = Main(game);
        var (view, warnings, problem) = (await PrepareAsync([stream], ct))[stream.Id];
        var plan = problem is not null
            ? new GamePlan { Stream = stream, Error = problem, Cloud = view, TreatAsInSession = true }
            : await TryPlanAsync(stream, treatAsInSession: true, view, warnings, ct);
        return (await ExecuteAsync([plan], ct))[0];
    }

    /// <summary>
    /// Makes <paramref name="version"/> current: this PC's files are kept first unless already stored, the version's
    /// files become a new current version, then they're swapped into place. Swap and undo use this too.
    /// </summary>
    /// <param name="name">A named save's name, used in place of the version id in what's shown and logged.</param>
    /// <param name="pulled">The cloud was just asked (finding a named save), so it isn't asked again.</param>
    public async Task<GameResult> RestoreAsync(GameId streamId, VersionId version, CancellationToken ct, string? name = null, bool pulled = false)
    {
        var shown = name is null ? version.Value : $"'{name}'";
        var stream = Find(streamId);

        // KAN-91 (the owner, 2 Oct 2026): a restore goes ahead while the game runs, as many games take a save back from
        // their menu; a file the game holds stops it, and it says which, with nothing changed.
        if (SharedSaves().TryGetValue(stream.Game.Id, out var clash))
        {
            throw new InvalidGameDefinitionException($"{stream.Game.Title}: {clash} Nothing was restored.");
        }

        if (!pulled)
        {
            await TryPullForActionAsync(stream, ct);
        }
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var target = versions.FirstOrDefault(v => v.Id == version)
            ?? throw new InvalidOperationException($"{stream.Definition.Title} has no version {version}.");
        if (thinned.Contains(version))
        {
            throw new InvalidOperationException($"Version {version} was thinned; its files are gone.");
        }

        // R16: a save someone shared never goes into a game whose saves stay with the account that made them, even one
        // brought in before GameSync knew it had an anti-cheat (a game not installed then).
        if (target.SharedFrom is not null && _options.StaysWithAccount?.Invoke(stream.Game.Id) is { } staysWith)
        {
            throw new BlockedException($"{stream.Definition.Title}: that save came from a shared zip. {staysWith} Nothing was restored.");
        }

        // The save there now is kept first (a rule that never breaks): one the game holds can't be read, so nothing changes.
        Snapshot snapshot;
        try
        {
            snapshot = ScanOrThrowIfUnavailable(stream);
        }
        catch (FileInUseException e)
        {
            throw InUse(stream, e.FilePath, "restored", e);
        }

        var state = _state.GetState(stream.Id);
        await KeepIfNotStoredAsync(stream, snapshot.Files, versions, VersionOrigin.KeptBeforeRestore,
            $"{Device.Name}'s save before restoring {shown}", state.Base?.Id, ct);

        var heads = VersionGraph.Heads(versions);
        var current = NewVersion(stream, target.Files, VersionKind.Normal, VersionOrigin.Restore,
            heads.FirstOrDefault()?.Id, heads.Skip(1).Select(h => h.Id).ToList(), pinned: false, $"Restored from {shown}");
        await RestoreFilesAsync(stream, current, snapshot.Files, ct);

        // In the log's plain, per-game voice (MGR-09): which save, not its version's ID.
        var restored = name is null ? $"the save from {target.Files.Select(f => f.ModifiedUtc).DefaultIfEmpty(target.CreatedUtc).Max().ToLocalTime().ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)}" : shown;
        _state.Log(stream.Id, "info", $"Restored {restored}; the files it replaced stay in its history.", EventTags.Restore);
        var result = new GameResult(stream.Id, stream.Definition.Title, SyncAction.Download, StatusFor(stream),
            $"Restored {shown}. Your previous files are kept in history.")
        {
            NewVersion = current.Id,
            Warnings = AccountWarning(stream, target) is { } warning ? [warning] : [],
        };
        return await AfterManualAsync(stream, result, ct);
    }

    /// <summary>SYNC-04: switches to the save that lost the game's most recent conflict.</summary>
    public async Task<GameResult> SwapAsync(GameId game, CancellationToken ct)
    {
        var stream = Main(game);
        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var pins = await _log.ListPinsAsync(stream.Id, ct);
        var losers = versions
            .Where(v => v.Origin == VersionOrigin.KeptInConflict)
            .Select(v => (v.Id, When: v.CreatedUtc))
            .Concat(pins.Where(p => p.FromConflict).Select(p => (Id: p.Version, When: p.CreatedUtc)))
            .OrderByDescending(x => x.When)
            .ToList();
        if (losers.Count == 0)
        {
            throw new InvalidOperationException($"{stream.Definition.Title} has no conflict to swap.");
        }

        return await RestoreAsync(stream.Id, losers[0].Id, ct);
    }

    /// <summary>SYNC-11: the user decides a waiting conflict. The side not chosen stays pinned in history.</summary>
    public async Task<GameResult> ResolveAsync(GameId game, bool keepThisPc, VersionId? chosenCloudVersion, CancellationToken ct)
    {
        var stream = Main(game);
        ThrowIfRunning(stream, "settling the conflict");
        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var heads = VersionGraph.Heads(versions);
        var snapshot = ScanOrThrowIfUnavailable(stream);
        var state = _state.GetState(stream.Id);
        var now = DateTime.UtcNow;
        if (heads.Count == 0)
        {
            throw new InvalidOperationException($"{stream.Definition.Title} has nothing in the cloud to choose between.");
        }

        if (keepThisPc)
        {
            if (snapshot.Files.Count == 0)
            {
                throw new InvalidOperationException($"{stream.Definition.Title} has no save files on this PC to keep.");
            }

            foreach (var head in heads)
            {
                await _log.SetPinAsync(stream.Id, new PinRecord(head.Id, $"{head.Device.Name}'s save, not chosen in a conflict", now, Device, FromConflict: true), ct);
            }

            var kept = await UploadAsync(stream, snapshot.Files, VersionKind.Normal, VersionOrigin.Resolve, heads[0].Id,
                heads.Skip(1).Select(h => h.Id).ToList(), pinned: false, label: $"Chosen over the cloud's save on {Device.Name}", ct);
            _state.SetBase(stream.Id, kept);
            _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {Device.Name}'s save; the other stays in history.", EventTags.Conflict);
            var keptResult = new GameResult(stream.Id, stream.Definition.Title, SyncAction.Upload, GameStatus.Synced,
                $"Kept {Device.Name}'s save. The cloud's is pinned in history.") { NewVersion = kept.Id };
            return await AfterManualAsync(stream, keptResult, ct);
        }

        var winner = chosenCloudVersion is { } chosen
            ? heads.FirstOrDefault(h => h.Id == chosen) ?? throw new InvalidOperationException($"{chosen} isn't one of the cloud's current saves.")
            : heads[0];
        await KeepIfNotStoredAsync(stream, snapshot.Files, versions, VersionOrigin.KeptInConflict,
            $"{Device.Name}'s save, not chosen in a conflict", state.Base?.Id, ct);
        foreach (var other in heads.Where(h => h.Id != winner.Id))
        {
            await _log.SetPinAsync(stream.Id, new PinRecord(other.Id, $"{other.Device.Name}'s save, not chosen in a conflict", now, Device, FromConflict: true), ct);
        }

        var current = heads.Count > 1
            ? NewVersion(stream, winner.Files, VersionKind.Normal, VersionOrigin.Resolve, winner.Id,
                heads.Where(h => h.Id != winner.Id).Select(h => h.Id).ToList(), pinned: false, $"Chosen in a conflict on {Device.Name}")
            : winner;
        await RestoreFilesAsync(stream, current, snapshot.Files, ct);
        _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {winner.Device.Name}'s save; the other stays in history.", EventTags.Conflict);
        var result = new GameResult(stream.Id, stream.Definition.Title, SyncAction.Download, GameStatus.Synced,
            $"Kept {winner.Device.Name}'s save. {Device.Name}'s is pinned in history.")
        {
            Warnings = AccountWarning(stream, winner) is { } warning ? [warning] : [],
        };
        return await AfterManualAsync(stream, result, ct);
    }

    public async Task PinAsync(GameId streamId, VersionId version, string label, CancellationToken ct)
    {
        var stream = Find(streamId);
        await _log.SetPinAsync(stream.Id, new PinRecord(version, label, DateTime.UtcNow, Device), ct);
        await UploadUnlessDeferredAsync(stream, ct);
    }

    /// <summary>Gone on this PC at once; the cloud's goes with the next upload, and a pull doesn't bring it back meanwhile.</summary>
    public async Task UnpinAsync(GameId streamId, VersionId version, CancellationToken ct)
    {
        var stream = Find(streamId);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        if (versions.Any(v => v.Id == version && v.Pinned))
        {
            throw new InvalidOperationException($"{version} was pinned when it was made (a conflict side or a pre-update save) and stays pinned.");
        }

        await _log.RemovePinAsync(stream.Id, version, ct);
        await UploadUnlessDeferredAsync(stream, ct);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(GameId streamId, CancellationToken ct)
    {
        var stream = Find(streamId);
        await TryPullForActionAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var pins = (await _log.ListPinsAsync(stream.Id, ct)).ToDictionary(p => p.Version);
        var heads = VersionGraph.Heads(versions).Select(h => h.Id).ToHashSet();
        var baseId = _state.GetState(stream.Id).Base?.Id;
        var pending = _history.PendingVersions(stream.Id).ToHashSet();
        var names = DeviceNames();
        return versions
            .Where(v => !thinned.Contains(v.Id))
            .OrderByDescending(v => v.CreatedUtc)
            .Select(v => new HistoryEntry(v, heads.Contains(v.Id), v.Id == baseId, v.Pinned || pins.ContainsKey(v.Id),
                pins.TryGetValue(v.Id, out var pin) ? pin.Label : v.Label,
                names.GetValueOrDefault(v.Device.Id, v.Device.Name), !pending.Contains(v.Id)))
            .ToList();
    }

    /// <summary>PC-01: every PC that has synced, with its app version and when it was last seen. Offline, the last known list.</summary>
    public async Task<IReadOnlyList<DeviceRecord>> DevicesAsync(CancellationToken ct)
    {
        await TryAnnounceDeviceAsync(ct);
        return _history.LoadDevices().OrderByDescending(d => d.LastSeenUtc).ToList();
    }

    /// <summary>
    /// PC-04: games with saves in the cloud that this PC doesn't sync, such as one installed only on the other PC so far.
    /// Their records are copied into the backup folder on the way, so offline the last known list answers.
    /// </summary>
    public async Task<IReadOnlyList<CloudGame>> OtherGamesAsync(CancellationToken ct)
    {
        IReadOnlySet<GameId> inCloud;
        try
        {
            inCloud = (await _cloud.ListGamesAsync(ct)).ToHashSet();
        }
        catch (CloudException)
        {
            inCloud = new HashSet<GameId>();
        }

        var mine = _streams.Select(s => s.Id).ToHashSet();
        var others = _history.Games().Concat(inCloud).Distinct()
            .Where(id => !mine.Contains(id) && !id.Value.Contains("--pc-", StringComparison.Ordinal))
            .ToList();
        var games = new ConcurrentBag<CloudGame>();
        await Parallel.ForEachAsync(others, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (id, token) =>
        {
            if (inCloud.Contains(id))
            {
                try
                {
                    await _store.PullAsync(id, token);
                }
                catch (CloudException)
                {
                    // This one stays as the backup folder knows it.
                }
            }

            var versions = await _log.ListAsync(id, token);
            if ((VersionGraph.Heads(versions).FirstOrDefault() ?? versions.MaxBy(v => v.CreatedUtc)) is { } newest)
            {
                var rules = versions.Where(v => v.Rules is not null).MaxBy(v => v.CreatedUtc)?.Rules;
                games.Add(new CloudGame(id, rules?.Title ?? id.Value, newest, rules));
            }
        });

        return games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>R8: the save rules the newest save from another PC used, and that PC; null when no other PC's save recorded any.</summary>
    public async Task<(PortableRules Rules, DeviceInfo Device)?> OtherRulesAsync(GameId game, CancellationToken ct)
    {
        try
        {
            if ((await _cloud.ListGamesAsync(ct)).Contains(game))
            {
                await _store.PullAsync(game, ct);
            }
        }
        catch (CloudException)
        {
            // Offline: what the backup folder knows.
        }

        var newest = (await _log.ListAsync(game, ct))
            .Where(v => v.Rules is not null && v.Device is not null && v.Device.Id != Device.Id)
            .MaxBy(v => v.CreatedUtc);
        return newest is null ? null : (newest.Rules!, newest.Device);
    }

    /// <summary>BAK-05: what thinning would remove, keeping the newest <paramref name="keepNewest"/> and everything pinned (BAK-04).</summary>
    public async Task<ThinPreview> PreviewThinAsync(GameId streamId, int keepNewest, CancellationToken ct)
    {
        var stream = Find(streamId);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var pins = (await _log.ListPinsAsync(stream.Id, ct)).Select(p => p.Version).ToHashSet();
        var live = versions.Where(v => !thinned.Contains(v.Id)).ToList();

        var protectedIds = new HashSet<VersionId>(pins);
        protectedIds.UnionWith(live.Where(v => v.Pinned || v.Kind == VersionKind.Held).Select(v => v.Id));
        protectedIds.UnionWith(VersionGraph.Heads(versions).Select(h => h.Id));
        protectedIds.UnionWith(_history.PendingVersions(stream.Id));
        if (_state.GetState(stream.Id).Base?.Id is { } baseId)
        {
            protectedIds.Add(baseId);
        }

        protectedIds.UnionWith(live.GroupBy(v => v.Device.Id).Select(g => g.MaxBy(v => v.CreatedUtc)!.Id));
        protectedIds.UnionWith(live.Where(v => v.Kind == VersionKind.Normal).OrderByDescending(v => v.CreatedUtc).Take(Math.Max(0, keepNewest)).Select(v => v.Id));

        var remove = live.Where(v => !protectedIds.Contains(v.Id)).OrderBy(v => v.CreatedUtc).ToList();
        var removeIds = remove.Select(v => v.Id).ToHashSet();
        var stillNeeded = live.Where(v => !removeIds.Contains(v.Id)).SelectMany(v => v.Files).Select(f => f.Hash).ToHashSet();
        var blobs = remove.SelectMany(v => v.Files).Where(f => !stillNeeded.Contains(f.Hash)).DistinctBy(f => f.Hash).ToList();
        return new ThinPreview(stream.Id, remove, blobs.Select(b => b.Hash).ToList(), blobs.Sum(b => b.Size),
            Fingerprint([], live) + string.Join(',', pins.Order()));
    }

    /// <summary>Thins exactly what <paramref name="preview"/> listed, or refuses if the history changed since. Needs the cloud.</summary>
    public async Task<int> ThinAsync(ThinPreview preview, int keepNewest, CancellationToken ct)
    {
        var again = await PreviewThinAsync(preview.Game, keepNewest, ct);
        if (again.Fingerprint != preview.Fingerprint || !again.Versions.Select(v => v.Id).SequenceEqual(preview.Versions.Select(v => v.Id)))
        {
            throw new InvalidOperationException("The history changed since the preview. Preview again before thinning.");
        }

        foreach (var version in preview.Versions)
        {
            await _log.MarkThinnedAsync(preview.Game, version.Id, ct);
        }

        foreach (var blob in preview.Blobs)
        {
            await _blobs.TrashAsync(preview.Game, blob, ct);
        }

        _state.Log(preview.Game, "info", $"Thinned {preview.Versions.Count} old versions by hand.", EventTags.Thinned);
        return preview.Versions.Count;
    }

    // ---- talking to the cloud ----

    /// <summary>Checks the backup folder and the cloud once, then pulls each game's new records.</summary>
    private async Task<Dictionary<GameId, (CloudView View, IReadOnlyList<string> Warnings, Exception? Problem)>> PrepareAsync(
        IReadOnlyList<SyncStream> streams, CancellationToken ct)
    {
        var prepared = new Dictionary<GameId, (CloudView, IReadOnlyList<string>, Exception?)>();
        _notices.Clear();
        if (_history.UnavailableReason() is { } unavailable)
        {
            _notices.Add(unavailable);
            foreach (var stream in streams)
            {
                prepared[stream.Id] = (CloudView.Reachable, [], new BackupFolderUnavailableException(unavailable));
            }

            return prepared;
        }

        var view = await CheckCloudAsync(ct);
        foreach (var stream in streams)
        {
            var warnings = new List<string>();
            var gameView = view;
            if (view.Problem is null)
            {
                try
                {
                    var pulled = await _store.PullAsync(stream.Id, ct);
                    if (pulled.Requeued > 0)
                    {
                        warnings.Add($"The cloud was missing {pulled.Requeued} versions this PC has; they upload again from the backup folder.");
                    }

                    if (!stream.PerDevice && await PlayingElsewhereAsync(stream.Game.Id, ct) is { } elsewhere)
                    {
                        warnings.Add(elsewhere);
                    }
                }
                catch (CloudException e)
                {
                    gameView = view with { Problem = e };
                }
            }

            prepared[stream.Id] = (gameView, warnings, null);
        }

        return prepared;
    }

    private async Task<CloudView> CheckCloudAsync(CancellationToken ct)
    {
        try
        {
            var info = await _cloud.GetInfoAsync(ct);
            _cloudWhere = info.Where;
            TimeSpan? skew = info.ServerTimeUtc is { } server ? DateTime.UtcNow - server : null;
            if (skew is { } s && s.Duration() > _options.ClockTolerance)
            {
                _notices.Add($"This PC's clock is {DecisionEngine.DescribeSkew(s)} Google's, so newest-wins is off on this PC until the clock is fixed (Windows Settings, Time, Sync now).");
            }

            NoteStorage(info);
            return new CloudView(null, skew);
        }
        catch (CloudException e)
        {
            _notices.Add(Describe(e));
            return new CloudView(e, null);
        }
    }

    /// <summary>CLOUD-05: one warning when the cloud passes 80% full, and another only after it drops below and fills again.</summary>
    private void NoteStorage(CloudInfo info)
    {
        if (info.UsedBytes is not { } used || info.TotalBytes is not { } total || total <= 0)
        {
            return;
        }

        var percent = used * 100 / total;
        var warned = _state.GetSetting("cloud.storageWarned") == "1";
        if (percent >= 80 && !warned)
        {
            _notices.Add($"Google Drive is {percent}% full ({FormatSize(used)} of {FormatSize(total)}). When it's full, uploads wait and your saves stay on this PC.");
            _state.SetSetting("cloud.storageWarned", "1");
        }
        else if (percent < 80 && warned)
        {
            _state.SetSetting("cloud.storageWarned", "0");
        }
    }

    private async Task TryPullAsync(SyncStream stream, CancellationToken ct)
    {
        try
        {
            await _store.PullAsync(stream.Id, ct);
        }
        catch (CloudException)
        {
            // Offline: work from what the backup folder knows.
        }
    }

    /// <summary>
    /// KAN-88: for a person's action, what's new in the cloud within <see cref="SyncOptions.PullBudget"/>; past it, the
    /// action goes on with what this PC knows, as offline. A pull stopped part way leaves whole records only, and the
    /// next one finishes it.
    /// </summary>
    private async Task TryPullForActionAsync(SyncStream stream, CancellationToken ct)
    {
        if (_options.PullBudget is not { } budget)
        {
            await TryPullAsync(stream, ct);
            return;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(budget);
        try
        {
            await TryPullAsync(stream, limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The cloud is slow just now: what this PC knows will do, as offline.
        }
    }

    private async Task<(CloudException? Problem, IReadOnlyList<string> Warnings)> TryPushAsync(SyncStream stream, CancellationToken ct)
    {
        if (!_history.HasPending(stream.Id))
        {
            return (null, []);
        }

        try
        {
            var pushed = await _store.PushAsync(stream.Id, _options.Progress, ct);
            return (null, pushed.Warnings);
        }
        catch (CloudException e)
        {
            return (e, []);
        }
    }

    /// <summary>After a manual action: upload what it made, refresh the plain copy, and settle the game's status.</summary>
    private async Task<GameResult> AfterManualAsync(SyncStream stream, GameResult result, CancellationToken ct)
    {
        if (_options.DeferUploads)
        {
            // KAN-88: done on this PC; the upload follows beside whatever the person does next.
            return Settle(stream, result, null, log: false);
        }

        using var gate = await UploadGateAsync(ct);
        var (problem, warnings) = await TryPushAsync(stream, ct);
        if (problem is null)
        {
            await TryWriteLatestAsync(stream, ct);
        }

        return Settle(stream, result with { Warnings = [.. result.Warnings, .. warnings] }, problem, log: false) with { CloudProblem = problem?.Kind };
    }

    /// <summary>CLOUD-03: the plain <c>latest/</c> copy follows the current version. Each PC writes the ones it made.</summary>
    private async Task TryWriteLatestAsync(SyncStream stream, CancellationToken ct)
    {
        try
        {
            var heads = VersionGraph.Heads(await _log.ListAsync(stream.Id, ct));
            if (heads.Count != 1 || heads[0].Device.Id != Device.Id || _history.PendingVersions(stream.Id).Contains(heads[0].Id))
            {
                return;
            }

            var key = $"latest.{stream.Id}";
            if (_state.GetSetting(key) == $"{heads[0].Id}|{_cloudWhere}")
            {
                return;
            }

            await _cloud.WriteLatestAsync(stream.Id, heads[0], (file, token) => _blobs.GetAsync(stream.Id, file.Hash, token), ct);
            _state.SetSetting(key, $"{heads[0].Id}|{_cloudWhere}");
        }
        catch (Exception e) when (e is CloudException or IOException or UnsafePathException or UnauthorizedAccessException)
        {
            _state.Log(stream.Id, "warn", $"Couldn't update the plain copy of the newest save in the cloud: {e.Message}", EventTags.Cloud);
        }
    }

    private async Task TryWriteRestoreKitAsync(CancellationToken ct)
    {
        var key = $"{RestoreKit.Version}|{_cloudWhere}";
        if (_state.GetSetting("cloud.restoreKit") == key)
        {
            return;
        }

        try
        {
            await _cloud.WriteRestoreKitAsync(ct);
            _state.SetSetting("cloud.restoreKit", key);
        }
        catch (Exception e) when (e is CloudException or IOException or UnauthorizedAccessException)
        {
            // Tried again on the next run.
        }
    }

    /// <summary>PC-01: records this PC in the cloud's device list and mirrors the list, so names show offline.</summary>
    private async Task TryAnnounceDeviceAsync(CancellationToken ct)
    {
        try
        {
            await _cloud.SaveDeviceAsync(new DeviceRecord(Device.Id, Device.Name, _options.AppVersion, DateTime.UtcNow), ct);
            await _history.SaveDevicesAsync(await _cloud.ListDevicesAsync(ct), ct);
        }
        catch (Exception e) when (e is CloudException or IOException)
        {
            // Offline: the last known list stays.
        }
    }

    private Dictionary<DeviceId, string> DeviceNames()
    {
        var names = _history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        names[Device.Id] = Device.Name;
        return names;
    }

    /// <summary>BAK-17: trims the backup folder's file contents to its limits, keeping everything a sync still needs.</summary>
    private async Task PruneHistoryAsync(CancellationToken ct)
    {
        if (_options.HistoryLimits.KeepsEverything)
        {
            return;
        }

        try
        {
            var keep = new Dictionary<GameId, IReadOnlySet<VersionId>>();
            var prefer = new Dictionary<GameId, IReadOnlySet<VersionId>>();
            foreach (var stream in _streams)
            {
                var versions = await _log.ListAsync(stream.Id, ct);
                var ids = VersionGraph.Heads(versions).Select(h => h.Id).ToHashSet();
                ids.UnionWith(versions.Where(v => v.Kind == VersionKind.Held).Select(v => v.Id));
                if (_state.GetState(stream.Id).Base?.Id is { } baseId)
                {
                    ids.Add(baseId);
                }

                keep[stream.Id] = ids;
                prefer[stream.Id] = (await _log.ListPinsAsync(stream.Id, ct)).Select(p => p.Version)
                    .Concat(versions.Where(v => v.Pinned).Select(v => v.Id))
                    .ToHashSet();
            }

            await _history.PruneAsync(keep, _options.HistoryLimits, ct, prefer);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Trimming is tidying; it's tried again after the next sync.
            _notices.Add($"Couldn't trim the backup folder: {e.Message}");
        }
    }

    // ---- planning internals ----

    private async Task<GamePlan> TryPlanAsync(SyncStream stream, bool treatAsInSession, CloudView view, IReadOnlyList<string> warnings, CancellationToken ct,
        bool whilePlaying = false)
    {
        try
        {
            return await PlanStreamAsync(stream, treatAsInSession, view, warnings, ct, whilePlaying) with { WhilePlaying = whilePlaying };
        }
        catch (Exception e) when (e is not OperationCanceledException and not SimulatedCrashException)
        {
            return new GamePlan { Stream = stream, Error = e, TreatAsInSession = treatAsInSession, Cloud = view, WhilePlaying = whilePlaying };
        }
    }

    private async Task<GamePlan> PlanStreamAsync(SyncStream stream, bool treatAsInSession, CloudView view, IReadOnlyList<string> warnings, CancellationToken ct,
        bool whilePlaying = false)
    {
        // KAN-91: a person's Back up now while the game runs reads its files now; what changed was made while playing.
        if (whilePlaying && IsRunning(stream.Game.Id))
        {
            treatAsInSession = true;
        }

        // BAK-10, BG-08: otherwise a running game's files aren't even read until it closes.
        else if (IsRunning(stream.Game.Id))
        {
            return new GamePlan
            {
                Stream = stream,
                Decision = new SyncDecision { Action = SyncAction.Playing, Reason = "Playing: it syncs once the game closes." },
                Warnings = warnings,
                TreatAsInSession = treatAsInSession,
                Cloud = view,
                Fingerprint = "playing",
            };
        }

        var state = _state.GetState(stream.Id);
        ExportRegistry(stream.Game);
        var snapshot = Scan(stream);
        var (versions, problems) = await LoadVersionsAsync(stream, ct);
        var decision = DecisionEngine.Decide(new SyncInputs
        {
            Game = stream.Definition,
            Device = Device,
            Local = snapshot.Files,
            Problems = snapshot.Problems,
            Base = state.Base,
            Reinstalled = state.Reinstalled,
            Versions = versions,
            Sessions = _state.GetSessions(stream.Game.Id),
            TreatChangesAsInSession = treatAsInSession,
            ConflictPending = state.Status == GameStatus.Conflict,
            ConflictReason = state.Status == GameStatus.Conflict ? DecisionEngine.WaitingReason(state.Detail) : null,
            CloudReachable = view.Problem is null,
            NoCloud = view.Problem?.Kind == CloudErrorKind.NotConnected,
            ClockSkew = view.ClockSkew,
            ClockTolerance = _options.ClockTolerance,
        });

        return new GamePlan
        {
            Stream = stream,
            Decision = decision,
            Local = snapshot.Files,
            Warnings = snapshot.Warnings.Concat(problems).Concat(warnings).ToList(),
            Base = state.Base,
            Versions = versions,
            TreatAsInSession = treatAsInSession,
            Cloud = view,
            Fingerprint = Fingerprint(snapshot.Files, versions) + $"|{state.Base?.Id}|{state.Status}|{state.Reinstalled}",
        };
    }

    /// <summary>Reads the game's versions and drops any that fail the same checks a restore would apply (R8).</summary>
    private async Task<(IReadOnlyList<VersionRecord> Valid, IReadOnlyList<string> Problems)> LoadVersionsAsync(SyncStream stream, CancellationToken ct)
    {
        var valid = new List<VersionRecord>();
        var problems = new List<string>();
        var all = await _log.ListAsync(stream.Id, ct);
        foreach (var version in all)
        {
            if (Validate(stream, version) is { } problem)
            {
                problems.Add($"Ignored version {version.Id} from {version.Device?.Name ?? "an unknown PC"}: {problem}");
            }
            else
            {
                valid.Add(version);
            }
        }

        if (!stream.PerDevice && OtherRules(stream, all) is { } other)
        {
            problems.Insert(0, other);
        }

        return (valid, problems);
    }

    /// <summary>
    /// R8: the newest save another PC made used other save rules. This PC never takes them up by itself: it keeps its
    /// own until someone confirms theirs here.
    /// </summary>
    private string? OtherRules(SyncStream stream, IReadOnlyList<VersionRecord> versions)
    {
        var newest = versions.Where(v => v.Rules is not null && v.Device is not null && v.Device.Id != Device.Id).MaxBy(v => v.CreatedUtc);
        if (newest?.Rules is not { } theirs || theirs.SameFilesAs(PortableRules.From(stream.Game)))
        {
            return null;
        }

        return $"{newest.Device.Name} saves this game with other save rules ({string.Join("; ", theirs.Describe())}). " +
            "This PC keeps its own until you confirm theirs here.";
    }

    private static string? Validate(SyncStream stream, VersionRecord version)
    {
        if (version.Game != stream.Id || version.Files is null || version.Device is null)
        {
            return "it's incomplete or belongs to another game.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in version.Files)
        {
            if (file?.Path is null || file.Size < 0)
            {
                return "it has an empty or negative entry.";
            }

            try
            {
                var (root, _) = RestorePathGuard.Split(file.Path);
                if (!stream.Definition.Roots.ContainsKey(root))
                {
                    return $"'{file.Path}' uses a folder this game doesn't have.";
                }
            }
            catch (UnsafePathException e)
            {
                return e.Message;
            }

            if (!seen.Add(file.Path))
            {
                return $"'{file.Path}' appears twice.";
            }
        }

        return null;
    }

    // ---- running a plan ----

    private async Task<GameResult> RunAsync(GamePlan plan, CancellationToken ct)
    {
        var stream = plan.Stream;
        var decision = plan.Decision!;
        var status = StatusFor(stream);
        var warnings = plan.Warnings.ToList();
        VersionRecord? created = null;

        switch (decision.Action)
        {
            case SyncAction.None:
                break;
            case SyncAction.Playing:
                status = GameStatus.Playing;
                break;
            case SyncAction.AdoptHead:
                _state.SetBase(stream.Id, decision.Head!);
                break;
            case SyncAction.Upload:
            case SyncAction.UploadHeld:
                // The run's own note, like the daily backup's, goes on the saves it makes current; a held one says it was held.
                created = await UploadAsync(stream, plan.Local, decision.UploadKind, decision.UploadOrigin, decision.UploadParent,
                    decision.Supersedes, pinned: false, label: decision.UploadKind == VersionKind.Normal ? _options.UploadLabel : null, ct);
                foreach (var pin in decision.Pins)
                {
                    await _log.SetPinAsync(stream.Id, new PinRecord(pin.Version, pin.Label, DateTime.UtcNow, Device, pin.FromConflict), ct);
                }

                if (decision.UploadKind == VersionKind.Normal)
                {
                    _state.SetBase(stream.Id, created);
                }

                break;
            case SyncAction.Download:
                if (decision.KeepLocalFirst is { } keep)
                {
                    await KeepIfNotStoredAsync(stream, plan.Local, plan.Versions, keep.Origin, keep.Label, plan.Base?.Id, ct);
                }

                await RestoreFilesAsync(stream, decision.Head!, plan.Local, ct);
                if (AccountWarning(stream, decision.Head!) is { } accountWarning)
                {
                    warnings.Add(accountWarning);
                }

                break;
            case SyncAction.NeedsYou:
                // Nothing moves either way (SYNC-11), but this PC's side is kept safe in history while it waits.
                await KeepIfNotStoredAsync(stream, plan.Local, plan.Versions, VersionOrigin.KeptInConflict,
                    $"{Device.Name}'s save while a conflict waits for you", plan.Base?.Id, ct);
                status = GameStatus.Conflict;
                break;
            case SyncAction.WaitForCloud:
                if (decision.KeepLocalFirst is { } offlineKeep)
                {
                    await KeepIfNotStoredAsync(stream, plan.Local, plan.Versions, offlineKeep.Origin, offlineKeep.Label, plan.Base?.Id, ct);
                }

                status = GameStatus.NewerInCloud;
                break;
            case SyncAction.Unavailable:
                status = GameStatus.NotAvailable;
                break;
            case SyncAction.SavesMissing:
                status = GameStatus.SavesMissing;
                break;
            case SyncAction.NoSaves:
                status = GameStatus.NoSaves;
                break;
        }

        if (decision.Held)
        {
            status = GameStatus.HeldForReview;
        }

        return new GameResult(stream.Id, stream.Definition.Title, decision.Action, status, decision.Reason)
        {
            Notice = decision.Notice,
            Warnings = warnings,
            NewVersion = created?.Id,
            Moved = decision.Action switch
            {
                SyncAction.Upload or SyncAction.UploadHeld when created is not null => $"Kept {Files(created)}{Played(created.Session)}.",
                SyncAction.Download => $"Brought down {Files(decision.Head!)} from {decision.Head!.Device.Name}.",
                _ => null,
            },
        };
    }

    /// <summary>KAN-40: a version's size and files, as the log says them: "2.1 MB (3 files)".</summary>
    private static string Files(VersionRecord version)
    {
        var bytes = FileSet.TotalSize(version.Files);
        var size = bytes < 1024 * 1024 ? $"{Math.Max(1, (bytes + 1023) / 1024)} KB" : FormatSize(bytes);
        return $"{size} ({(version.Files.Count == 1 ? "1 file" : $"{version.Files.Count} files")})";
    }

    /// <summary>KAN-40: ", after 3 h 10 min of play" for a version made from a play session; empty for any other.</summary>
    private static string Played(SessionInfo? session)
    {
        if (session is null)
        {
            return "";
        }

        var minutes = (int)Math.Round((session.EndUtc - session.StartUtc).TotalMinutes);
        var length = minutes < 1 ? "under a minute"
            : minutes < 60 ? $"{minutes} min"
            : minutes % 60 == 0 ? $"{minutes / 60} h"
            : $"{minutes / 60} h {minutes % 60} min";
        return $", after {length} of play";
    }

    /// <summary>
    /// Records the game's final status. A game with versions still in the outbox shows Upload pending, with why, unless
    /// something more urgent (a conflict, held changes) needs the user first.
    /// </summary>
    private GameResult Settle(SyncStream stream, GameResult result, CloudException? uploadProblem, bool log)
    {
        var status = result.Status;
        var message = result.Message;
        if (_history.HasPending(stream.Id))
        {
            if (status is GameStatus.Synced or GameStatus.BackupOnly or GameStatus.UploadPending or GameStatus.NewerInCloud)
            {
                status = GameStatus.UploadPending;
            }

            if (uploadProblem is not null)
            {
                message = $"{message} Kept on this PC; the upload waits: {Describe(uploadProblem)}";
            }
        }

        _state.SetStatus(stream.Id, status, result.Notice ?? message);
        if (log && result.Action is not SyncAction.None)
        {
            // A conflict newest-wins settled has its Swap, and a held save waits for the person: both are worth a look.
            var level = result.Notice is not null ||
                status is GameStatus.Conflict or GameStatus.HeldForReview or GameStatus.NotAvailable or GameStatus.SavesMissing ||
                (status is GameStatus.UploadPending && uploadProblem is not null)
                ? "warn" : "info";
            var line = string.Join(" ", new[] { message, result.Moved, result.Notice }.OfType<string>());
            _state.Log(stream.Id, level, line, TagOf(stream, result.Action, result.Notice));
        }

        return result with { Status = status, Message = message };
    }

    private GameResult Fail(GamePlan plan, Exception e)
    {
        var stream = plan.Stream;
        var (status, message) = e switch
        {
            FileInUseException inUse => (GameStatus.FilesInUse, $"Can't read {inUse.FilePath}: another program has it open. Other games carry on."),
            BlockedException or UnsafePathException => (GameStatus.Blocked, e.Message),
            CloudException { Kind: CloudErrorKind.Flagged } => (GameStatus.Blocked, $"{e.Message} Nothing was downloaded."),
            CloudException cloud when plan.Decision?.Action is SyncAction.Download =>
                (GameStatus.NewerInCloud, $"A newer save is in the cloud, but the download waits: {Describe(cloud)}"),
            CloudException cloud => (GameStatus.Error, Describe(cloud)),
            BackupFolderUnavailableException => (GameStatus.NotAvailable, e.Message),
            BlobMismatchException => (GameStatus.Error, $"{e.Message} It'll be tried again."),
            InvalidGameDefinitionException => (GameStatus.Error, e.Message),
            _ => (GameStatus.Error, e.Message),
        };
        _state.SetStatus(stream.Id, status, message);
        _state.Log(stream.Id, "error", message, e switch
        {
            FileInUseException => EventTags.InUse,
            BlockedException or UnsafePathException => EventTags.Blocked,
            CloudException => EventTags.Cloud,
            BackupFolderUnavailableException => EventTags.Missing,
            _ => EventTags.Error,
        });
        return new GameResult(stream.Id, stream.Definition.Title, null, status, message) { CloudProblem = (e as CloudException)?.Kind ?? plan.Cloud.Problem?.Kind };
    }

    /// <summary>What a sync's line in the activity log is about, as the Log tab tags it (MGR-09).</summary>
    private static string? TagOf(SyncStream stream, SyncAction? action, string? notice) => action switch
    {
        // Newest wins settled a conflict on its way: the line says which save was kept, and that Swap brings the other back.
        _ when notice is not null => EventTags.Conflict,
        SyncAction.Upload => stream.Definition.Mode == GameMode.BackupOnly ? EventTags.Backup : EventTags.Upload,
        SyncAction.UploadHeld => EventTags.Held,
        SyncAction.Download => EventTags.Download,
        SyncAction.AdoptHead => EventTags.Sync,
        SyncAction.NeedsYou => EventTags.Conflict,
        SyncAction.Unavailable or SyncAction.SavesMissing or SyncAction.NoSaves => EventTags.Missing,
        SyncAction.WaitForCloud => EventTags.Offline,
        SyncAction.Playing => EventTags.Playing,
        _ => null,
    };

    private static string Describe(CloudException e) => e.Kind switch
    {
        CloudErrorKind.SignInExpired => $"{e.Message} Run 'gamesync signin' to sign in again.",
        _ => e.Message,
    };

    /// <summary>PC-03: a save made under another account ID loads in many games, but not in all of them.</summary>
    private static string? AccountWarning(SyncStream stream, VersionRecord version)
    {
        if (version.Accounts is null)
        {
            return null;
        }

        foreach (var (key, theirs) in version.Accounts)
        {
            if (stream.Definition.Accounts.TryGetValue(key, out var ours) && ours != theirs)
            {
                var account = key switch { "steamUser" => "Steam account", "epicUser" => "Epic account", _ => key };
                return $"This save was made with another {account} ({theirs}) than this PC's ({ours}). Some games, like FromSoftware's, won't load saves from another account.";
            }
        }

        return null;
    }

    // ---- storing versions (BAK-12: contents first, record last) ----

    /// <param name="open">Where the files' contents come from; by default this PC's save folders.</param>
    private async Task<VersionRecord> UploadAsync(SyncStream stream, IReadOnlyList<FileEntry> files, VersionKind kind, VersionOrigin origin,
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label, CancellationToken ct, Func<FileEntry, Stream>? open = null,
        string? sharedFrom = null)
    {
        await Parallel.ForEachAsync(files.DistinctBy(f => f.Hash), new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (file, token) =>
            {
                if (await _blobs.ExistsAsync(stream.Id, file.Hash, token))
                {
                    return;
                }

                await using var content = open?.Invoke(file) ?? OpenForReading(LocalPath(stream.Definition, file));
                await _blobs.PutAsync(stream.Id, file.Hash, content, token);
            });

        CrashPoints.Hit(CrashPoints.AfterBlobsBeforeRecord);
        return await AppendAsync(stream, files, kind, origin, parent, supersedes, pinned, label, ct, sharedFrom);
    }

    private async Task<VersionRecord> AppendAsync(SyncStream stream, IReadOnlyList<FileEntry> files, VersionKind kind, VersionOrigin origin,
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label, CancellationToken ct, string? sharedFrom = null)
    {
        var version = NewVersion(stream, files, kind, origin, parent, supersedes, pinned, label) with { SharedFrom = sharedFrom };
        await _log.AppendAsync(stream.Id, version, ct);
        return version;
    }

    /// <summary>
    /// A version not recorded yet: a restore's, which <see cref="RestoreFilesAsync"/> records once its files are in place,
    /// so a restore that stops (a file in use, KAN-91) leaves no version behind for a later sync to bring in by itself.
    /// </summary>
    private VersionRecord NewVersion(SyncStream stream, IReadOnlyList<FileEntry> files, VersionKind kind, VersionOrigin origin,
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label)
    {
        var now = DateTime.UtcNow;
        return new VersionRecord
        {
            Id = VersionId.New(now, Device.Name),
            Game = stream.Id,
            Parent = parent,
            Supersedes = supersedes,
            Kind = kind,
            Origin = origin,
            Device = Device,
            CreatedUtc = now,
            Session = _state.GetSessions(stream.Game.Id).LastOrDefault(),
            Pinned = pinned,
            Label = label,
            Accounts = stream.Definition.Accounts.Count > 0 ? stream.Definition.Accounts : null,
            Rules = PortableRules.From(stream.Game),
            Files = files,
        };
    }

    /// <summary>Stores this PC's files as a pinned Kept version, unless some version already holds exactly them.</summary>
    private async Task KeepIfNotStoredAsync(SyncStream stream, IReadOnlyList<FileEntry> local, IReadOnlyList<VersionRecord> versions,
        VersionOrigin origin, string label, VersionId? parent, CancellationToken ct)
    {
        if (local.Count == 0 || versions.Any(v => FileSet.SameContent(v.Files, local)))
        {
            return;
        }

        await UploadAsync(stream, local, VersionKind.Kept, origin, parent, [], pinned: true, label, ct);
    }

    // ---- restores (BAK-08, BAK-09, R1, R3, R6, R8) ----

    private async Task RestoreFilesAsync(SyncStream stream, VersionRecord target, IReadOnlyList<FileEntry> currentLocal, CancellationToken ct)
    {
        var definition = stream.Definition;
        var journalId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{stream.Id.Value}";
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        var aside = Path.Combine(_dataDir, "restore-aside", journalId);
        var incoming = target.Files.Where(f => stream.Includes(f.Category)).ToList();

        // Every path is checked before anything is written.
        var plannedTargets = incoming.Select(f => (File: f, Full: RestorePath(definition, f))).ToList();
        var targetPaths = plannedTargets.Select(p => p.Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ops = new List<JournalOp>();
        var n = 0;
        foreach (var (_, full) in plannedTargets)
        {
            ops.Add(new JournalOp("replace", full, $"{full}.gs-new-{suffix}", Path.Combine(aside, $"{n++}-{Path.GetFileName(full)}")));
        }

        // A version taken with fewer rules than this PC's knows nothing about the other files, so they stay (R8).
        foreach (var file in currentLocal.Where(f => stream.Includes(f.Category) && (target.Rules is null || target.Rules.Takes(f.Path))))
        {
            var full = LocalPath(definition, file);
            if (!targetPaths.Contains(full))
            {
                ops.Add(new JournalOp("remove", full, null, Path.Combine(aside, $"{n++}-{Path.GetFileName(full)}")));
            }
        }

        // KAN-80: what this PC doesn't have yet comes down first, four files at a time, saying how far it is; nothing is
        // written into the game's folders until all of it is here.
        await _store.FetchAsync(stream.Id, incoming, _options.Progress, ct);

        var journal = new RestoreJournal { Id = journalId, Game = stream.Id, Target = target, Ops = ops };
        journal.Save(_dataDir);
        var unscanned = 0;
        try
        {
            foreach (var (file, full) in plannedTargets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (await StageAsync(stream.Id, file, $"{full}.gs-new-{suffix}", ct) == ScanVerdict.Unavailable)
                {
                    unscanned++;
                }
            }

            foreach (var (file, full) in plannedTargets.Where(p => IsRegistryExport(p.File)))
            {
                CheckRegistryExport(stream.Game, file, $"{full}.gs-new-{suffix}");
            }

            // KAN-91: a file the game holds open can't be swapped, so the restore stops here, before anything moves.
            foreach (var op in ops)
            {
                ThrowIfHeld(stream, op.Target);
            }
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            journal.Abort();
            journal.Delete(_dataDir);
            throw;
        }

        // Set before the swap, so a restore cut off after it still writes its registry keys back next time.
        var registryKeys = plannedTargets.Any(p => IsRegistryExport(p.File));
        if (registryKeys)
        {
            _state.SetSetting(RegistryPending(stream.Game.Id), "1");
        }

        journal = journal with { Ready = true };
        journal.Save(_dataDir);
        CrashPoints.Hit(CrashPoints.AfterStagingBeforeSwap);
        try
        {
            journal.Commit(undoOnFailure: true);
        }
        catch (SwapStoppedException e) when (e.Undone)
        {
            // A file was opened a moment ago: what was moved is back where it was, and nothing changed.
            journal.Abort();
            journal.Delete(_dataDir);
            if (registryKeys)
            {
                _state.SetSetting(RegistryPending(stream.Game.Id), "");
            }

            throw InUse(stream, e.Target, "restored", e);
        }

        // A restore's own version is recorded now that its files are in place (KAN-91); a download's is there already.
        if (await _log.GetAsync(stream.Id, target.Id, ct) is null)
        {
            await _log.AppendAsync(stream.Id, target, ct);
        }

        _state.SetBase(stream.Id, target);
        journal.Delete(_dataDir);

        // R3: a file no antivirus answered for (none installed or running, or one too big to hand it) is restored, and the
        // game's log says so; Windows' own real-time scan still checks files as they're written.
        if (unscanned > 0)
        {
            _state.Log(stream.Id, "warn",
                $"No antivirus answered for {(unscanned == 1 ? "1 file" : $"{unscanned} files")} of this restore, so {(unscanned == 1 ? "it was" : "they were")} put in place without that check.",
                EventTags.Restore);
        }

        if (registryKeys)
        {
            try
            {
                ApplyPendingRegistry(stream.Game);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Security.SecurityException)
            {
                // The files are restored; the keys are written back before the game's next scan.
                _state.Log(stream.Id, "warn", $"Couldn't write the restored registry keys back yet: {e.Message}", EventTags.Restore);
            }
        }
    }

    /// <summary>KAN-91: stops a restore before its swap when the game (or anything) holds one of the files it would move.</summary>
    private static void ThrowIfHeld(SyncStream stream, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException e) when (e.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
        {
            throw InUse(stream, path, "restored", e);
        }
    }

    /// <summary>
    /// KAN-91: a file the game holds, said plainly: which one (from its save folder), that nothing changed, and what to do:
    /// "Bloodborne GOTY has SPRJ0005\userdata0000 open, so nothing was restored and its save is as it was. …".
    /// </summary>
    private static InvalidOperationException InUse(SyncStream stream, string path, string what, Exception inner)
    {
        var shown = stream.Game.Roots.Values.Where(r => !RootResolver.IsUnresolved(r))
            .Select(r => Path.GetFullPath(r))
            .Where(r => path.StartsWith(Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(r => Path.Join(Path.GetFileName(Path.TrimEndingDirectorySeparator(r)), Path.GetRelativePath(r, path)))
            .FirstOrDefault() ?? Path.GetFileName(path);
        return new InvalidOperationException(
            $"{stream.Game.Title} has {shown} open, so nothing was {what} and its save is as it was. Go back to the game's main menu, or quit it, then try again.", inner);
    }

    private Task<ScanVerdict?> StageAsync(GameId game, FileEntry file, string staged, CancellationToken ct) => StageAsync(game, file, staged, restoring: true, ct);

    /// <param name="restoring">False when packing a shared zip, which leaves a program out itself rather than stopping.</param>
    /// <returns>What the antivirus said of a file being restored (R3); null when nothing is restored.</returns>
    private async Task<ScanVerdict?> StageAsync(GameId game, FileEntry file, string staged, bool restoring, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await _blobs.GetAsync(game, file.Hash, ct))
        await using (var target = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            await target.FlushAsync(ct);
            target.Flush(flushToDisk: true);
        }

        if (BlobId.FromHash(sha.GetHashAndReset()) != file.Hash)
        {
            throw new BlockedException(restoring
                ? $"'{file.Path}' is damaged in the cloud: its contents don't match its hash. Nothing was restored."
                : $"'{file.Path}' is damaged in the cloud: its contents don't match its hash, so the zip wasn't made.");
        }

        if (!restoring)
        {
            return null;
        }

        if (ProgramFileDetector.IsProgramFile(staged))
        {
            throw new BlockedException($"'{file.Path}' is a program, not a save. Nothing was restored.");
        }

        var verdict = _malware.ScanFile(staged);
        if (verdict == ScanVerdict.Detected)
        {
            throw new BlockedException($"Your antivirus flagged '{file.Path}'. Nothing was restored.");
        }

        File.SetLastWriteTimeUtc(staged, file.ModifiedUtc);
        return verdict;
    }

    /// <summary>Where a file from a version goes on this PC, after every check a path from the cloud must pass.</summary>
    private static string RestorePath(GameDefinition definition, FileEntry file)
    {
        var (rootKey, relative) = RestorePathGuard.Split(file.Path);
        if (!definition.Roots.TryGetValue(rootKey, out var root))
        {
            throw new BlockedException($"'{file.Path}' uses a folder {definition.Title} doesn't have.");
        }

        var covered = definition.Rules.Any(rule =>
            rule.Root == rootKey &&
            new Glob(rule.Include).IsMatch(relative) &&
            !rule.Exclude.Any(e => new Glob(e).IsMatch(relative)));
        if (!covered)
        {
            throw new BlockedException($"'{file.Path}' isn't covered by {definition.Title}'s save rules.");
        }

        if (ProgramFileDetector.HasBlockedExtension(relative))
        {
            throw new BlockedException($"'{file.Path}' is a program file, never restored.");
        }

        return RestorePathGuard.Resolve(root, relative);
    }

    private static string LocalPath(GameDefinition definition, FileEntry file)
    {
        var (rootKey, relative) = RestorePathGuard.Split(file.Path);
        return Path.Combine(definition.Roots[rootKey], relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static FileStream OpenForReading(string fullPath)
    {
        try
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        catch (IOException e) when (e.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
        {
            throw new FileInUseException(fullPath, e);
        }
    }

    private Snapshot ScanOrThrowIfUnavailable(SyncStream stream)
    {
        if (_history.UnavailableReason() is { } unavailable)
        {
            throw new InvalidOperationException(unavailable);
        }

        ExportRegistry(stream.Game);
        var snapshot = Scan(stream);
        if (snapshot.Problems.Count > 0)
        {
            throw new InvalidOperationException($"{stream.Definition.Title}: {snapshot.Problems[0].Message}. Nothing was changed.");
        }

        return snapshot;
    }

    /// <summary>The setting that holds the programs last found in a game's save folders (R2), one full path a line.</summary>
    public static string ProgramsKey(GameId stream) => "programs." + stream.Value;

    /// <summary>
    /// Reads the game's files, and keeps the programs found among them with the game (R2, design system version 51): its
    /// saves page shows them under their place, and its log says so once, when one is first found, not at every backup.
    /// They're never backed up (R1).
    /// </summary>
    private Snapshot Scan(SyncStream stream)
    {
        var snapshot = _scanner.Scan(stream.Definition, stream.Includes);
        var key = ProgramsKey(stream.Id);
        var before = _state.GetSetting(key) is { Length: > 0 } kept
            ? kept.Split('\n').ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in snapshot.Programs.Where(p => !before.Contains(p)))
        {
            _state.Log(stream.Id, "warn", $"Left out {Path.GetFileName(program)}: a program, never backed up or synced. It's in {Path.GetDirectoryName(program)}.",
                EventTags.Backup);
        }

        // A folder that couldn't be read (an unplugged drive) leaves what was found there as it was.
        if (snapshot.Problems.Count == 0 && !before.SetEquals(snapshot.Programs))
        {
            _state.SetSetting(key, string.Join('\n', snapshot.Programs));
        }

        return snapshot;
    }

    // ---- streams ----

    private IEnumerable<SyncStream> StreamsFor(GameDefinition game)
    {
        // FIND-10: registry keys are exported into a folder of this PC's own, which joins the game as one more root.
        if (game.Registry.Count > 0 && !game.Roots.ContainsKey(GameDefinition.RegistryRoot))
        {
            game = game with
            {
                Roots = new Dictionary<string, string>(game.Roots, StringComparer.Ordinal) { [GameDefinition.RegistryRoot] = RegistryFolder(game.Id) },
                Rules =
                [
                    .. game.Rules,
                    .. game.Registry.Select(r => new SaveRule
                    {
                        Root = GameDefinition.RegistryRoot,
                        Include = RegistryFile.FileName(r.Key),
                        Category = r.Category,
                        UseDefaultExcludes = false,
                    }),
                ],
            };
        }

        var shared = new HashSet<SaveCategory> { SaveCategory.Save };
        if (game.SyncConfig)
        {
            shared.Add(SaveCategory.Config);
        }

        yield return new SyncStream(game, game, shared, PerDevice: false);

        var own = new HashSet<SaveCategory>();
        if (!game.SyncConfig && game.Rules.Any(r => r.Category == SaveCategory.Config))
        {
            own.Add(SaveCategory.Config);
        }

        if (game.IncludeScreenshots && game.Rules.Any(r => r.Category == SaveCategory.Screenshots))
        {
            own.Add(SaveCategory.Screenshots);
        }

        if (own.Count > 0)
        {
            var id = GameId.Parse($"{game.Id.Value}--pc-{Device.Id.Value[^8..]}");
            var perDevice = game with { Id = id, Title = $"{game.Title} (settings on {Device.Name})", Mode = GameMode.BackupOnly };
            yield return new SyncStream(perDevice, game, own, PerDevice: true);
        }
    }

    /// <summary>FOLD-11: games that share a save file or registry key with another, and what they share.</summary>
    private IReadOnlyDictionary<GameId, string> SharedSaves() => RuleOverlap.Find(_streams.Where(s => !s.PerDevice).Select(s => s.Game).ToList());

    private IEnumerable<SyncStream> Select(IReadOnlyCollection<GameId>? only) =>
        only is null ? _streams : _streams.Where(s => only.Contains(s.Id) || only.Contains(s.Game.Id));

    private SyncStream Main(GameId game) =>
        _streams.FirstOrDefault(s => !s.PerDevice && s.Game.Id == game) ?? throw new InvalidOperationException($"There's no game '{game}'.");

    private SyncStream Find(GameId id) =>
        _streams.FirstOrDefault(s => s.Id == id) ?? Main(id);

    private static GameStatus StatusFor(SyncStream stream) =>
        stream.Definition.Mode == GameMode.BackupOnly ? GameStatus.BackupOnly : GameStatus.Synced;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    private static string Fingerprint(IReadOnlyList<FileEntry> local, IReadOnlyList<VersionRecord> versions)
    {
        var text = new StringBuilder();
        foreach (var file in local.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(file.Path).Append('=').Append(file.Hash.Value).Append(';');
        }

        foreach (var version in versions.OrderBy(v => v.Id))
        {
            text.Append(version.Id.Value).Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
