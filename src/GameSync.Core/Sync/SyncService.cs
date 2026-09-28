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
    public async Task<IReadOnlyList<GamePlan>> PlanAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct)
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
                : await TryPlanAsync(stream, treatAsInSession: false, view, warnings, ct));
        }

        return plans;
    }

    /// <summary>
    /// Runs each plan exactly as planned (SYNC-14); a game that changed since planning is planned again, and its result
    /// says so. Afterwards each game's outbox uploads, and the backup folder is trimmed to its limits.
    /// </summary>
    public async Task<IReadOnlyList<GameResult>> ExecuteAsync(IReadOnlyList<GamePlan> plans, CancellationToken ct)
    {
        var online = plans.Any(p => p.Error is null && p.Cloud.Problem is null);
        if (online)
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

                // BG-08: while the game runs, not even its outbox uploads.
                if (plan.Decision?.Action == SyncAction.Playing || IsRunning(plan.Stream.Game.Id))
                {
                    var playing = new GameResult(plan.Stream.Id, plan.Title, SyncAction.Playing, GameStatus.Playing, "Playing: it syncs once the game closes.");
                    _state.SetStatus(plan.Stream.Id, GameStatus.Playing, playing.Message);
                    _state.FinishJob(job);
                    results.Add(playing);
                    continue;
                }

                // Whatever earlier runs left in the outbox goes first.
                (CloudException? Problem, IReadOnlyList<string> Warnings) upload = plan.Cloud.Problem is null
                    ? await TryPushAsync(plan.Stream, ct)
                    : (plan.Cloud.Problem, []);

                var current = plan;
                var fresh = await PlanStreamAsync(plan.Stream, plan.TreatAsInSession, plan.Cloud, plan.Warnings, ct);
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

                if (plan.Cloud.Problem is null && upload.Problem is null)
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

        if (online)
        {
            await TryWriteRestoreKitAsync(ct);
        }

        await PruneHistoryAsync(ct);
        return results;
    }

    public async Task<IReadOnlyList<GameResult>> SyncAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct) =>
        await ExecuteAsync(await PlanAsync(only, ct), ct);

    /// <summary>Finishes restores and jobs a crash or reboot interrupted (BAK-08, BAK-13). Call once when GameSync starts.</summary>
    public async Task<IReadOnlyList<GameResult>> RecoverAsync(CancellationToken ct)
    {
        foreach (var journal in RestoreJournal.RecoverAll(_dataDir))
        {
            _state.SetBase(journal.Game, journal.Target);
            _state.Log(journal.Game, "info", "Finished a restore that was interrupted.");
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
        var plans = await PlanAsync([game], ct);
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
    public async Task<GameResult> RestoreAsync(GameId streamId, VersionId version, CancellationToken ct, string? name = null)
    {
        var shown = name is null ? version.Value : $"'{name}'";
        var stream = Find(streamId);
        ThrowIfRunning(stream, $"restoring {shown}");
        if (SharedSaves().TryGetValue(stream.Game.Id, out var clash))
        {
            throw new InvalidGameDefinitionException($"{stream.Game.Title}: {clash} Nothing was restored.");
        }

        await TryPullAsync(stream, ct);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var target = versions.FirstOrDefault(v => v.Id == version)
            ?? throw new InvalidOperationException($"{stream.Definition.Title} has no version {version}.");
        if (thinned.Contains(version))
        {
            throw new InvalidOperationException($"Version {version} was thinned; its files are gone.");
        }

        var snapshot = ScanOrThrowIfUnavailable(stream);
        var state = _state.GetState(stream.Id);
        await KeepIfNotStoredAsync(stream, snapshot.Files, versions, VersionOrigin.KeptBeforeRestore,
            $"{Device.Name}'s save before restoring {shown}", state.Base?.Id, ct);

        var heads = VersionGraph.Heads(versions);
        var current = await AppendAsync(stream, target.Files, VersionKind.Normal, VersionOrigin.Restore,
            heads.FirstOrDefault()?.Id, heads.Skip(1).Select(h => h.Id).ToList(), pinned: false, $"Restored from {shown}", ct);
        await RestoreFilesAsync(stream, current, snapshot.Files, ct);

        _state.Log(stream.Id, "info", $"Restored {shown} ({version}) as {current.Id}.");
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
            _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {Device.Name}'s save as {kept.Id}.");
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
            ? await AppendAsync(stream, winner.Files, VersionKind.Normal, VersionOrigin.Resolve, winner.Id,
                heads.Where(h => h.Id != winner.Id).Select(h => h.Id).ToList(), pinned: false, $"Chosen in a conflict on {Device.Name}", ct)
            : winner;
        await RestoreFilesAsync(stream, current, snapshot.Files, ct);
        _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {winner.Device.Name}'s save ({winner.Id}).");
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
        await TryPushAsync(stream, ct);
    }

    /// <summary>Needs the cloud: an unpin made only on this PC would come back with the next sync.</summary>
    public async Task UnpinAsync(GameId streamId, VersionId version, CancellationToken ct)
    {
        var stream = Find(streamId);
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        if (versions.Any(v => v.Id == version && v.Pinned))
        {
            throw new InvalidOperationException($"{version} was pinned when it was made (a conflict side or a pre-update save) and stays pinned.");
        }

        await _log.RemovePinAsync(stream.Id, version, ct);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(GameId streamId, CancellationToken ct)
    {
        var stream = Find(streamId);
        await TryPullAsync(stream, ct);
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

        _state.Log(preview.Game, "info", $"Thinned {preview.Versions.Count} old versions by hand.");
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
            _state.Log(stream.Id, "warn", $"Couldn't update the plain copy of the newest save in the cloud: {e.Message}");
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

    private async Task<GamePlan> TryPlanAsync(SyncStream stream, bool treatAsInSession, CloudView view, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        try
        {
            return await PlanStreamAsync(stream, treatAsInSession, view, warnings, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException and not SimulatedCrashException)
        {
            return new GamePlan { Stream = stream, Error = e, TreatAsInSession = treatAsInSession, Cloud = view };
        }
    }

    private async Task<GamePlan> PlanStreamAsync(SyncStream stream, bool treatAsInSession, CloudView view, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        // BAK-10, BG-08: a running game's files aren't even read until it closes.
        if (IsRunning(stream.Game.Id))
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
        var snapshot = _scanner.Scan(stream.Definition, stream.Includes);
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
            CloudReachable = view.Problem is null,
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
                created = await UploadAsync(stream, plan.Local, decision.UploadKind, decision.UploadOrigin, decision.UploadParent,
                    decision.Supersedes, pinned: false, label: null, ct);
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
        };
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
            var level = status is GameStatus.Conflict or GameStatus.NotAvailable or GameStatus.SavesMissing or GameStatus.UploadPending ? "warn" : "info";
            _state.Log(stream.Id, level, result.Notice is null ? message : $"{message} {result.Notice}");
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
        _state.Log(stream.Id, "error", message);
        return new GameResult(stream.Id, stream.Definition.Title, null, status, message) { CloudProblem = (e as CloudException)?.Kind ?? plan.Cloud.Problem?.Kind };
    }

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
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label, CancellationToken ct, Func<FileEntry, Stream>? open = null)
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
        return await AppendAsync(stream, files, kind, origin, parent, supersedes, pinned, label, ct);
    }

    private async Task<VersionRecord> AppendAsync(SyncStream stream, IReadOnlyList<FileEntry> files, VersionKind kind, VersionOrigin origin,
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var version = new VersionRecord
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
        await _log.AppendAsync(stream.Id, version, ct);
        return version;
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

        var journal = new RestoreJournal { Id = journalId, Game = stream.Id, Target = target, Ops = ops };
        journal.Save(_dataDir);
        try
        {
            foreach (var (file, full) in plannedTargets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await StageAsync(stream.Id, file, $"{full}.gs-new-{suffix}", ct);
            }

            foreach (var (file, full) in plannedTargets.Where(p => IsRegistryExport(p.File)))
            {
                CheckRegistryExport(stream.Game, file, $"{full}.gs-new-{suffix}");
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
        journal.Commit();
        _state.SetBase(stream.Id, target);
        journal.Delete(_dataDir);
        if (registryKeys)
        {
            try
            {
                ApplyPendingRegistry(stream.Game);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Security.SecurityException)
            {
                // The files are restored; the keys are written back before the game's next scan.
                _state.Log(stream.Id, "warn", $"Couldn't write the restored registry keys back yet: {e.Message}");
            }
        }
    }

    private async Task StageAsync(GameId game, FileEntry file, string staged, CancellationToken ct)
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
            throw new BlockedException($"'{file.Path}' is damaged in the cloud: its contents don't match its hash. Nothing was restored.");
        }

        if (ProgramFileDetector.IsProgramFile(staged))
        {
            throw new BlockedException($"'{file.Path}' is a program, not a save. Nothing was restored.");
        }

        if (_malware.ScanFile(staged) == ScanVerdict.Detected)
        {
            throw new BlockedException($"Your antivirus flagged '{file.Path}'. Nothing was restored.");
        }

        File.SetLastWriteTimeUtc(staged, file.ModifiedUtc);
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
        var snapshot = _scanner.Scan(stream.Definition, stream.Includes);
        if (snapshot.Problems.Count > 0)
        {
            throw new InvalidOperationException($"{stream.Definition.Title}: {snapshot.Problems[0].Message}. Nothing was changed.");
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
