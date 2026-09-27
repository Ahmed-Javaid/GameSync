using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;

namespace GameSync.Core.Sync;

/// <summary>
/// Plans and runs syncs one game at a time. A failure stays on its own game (SYNC-02); uploads write file contents
/// before the version record (BAK-12); restores stage, check and journal every file before swapping (BAK-08).
/// </summary>
public sealed class SyncService
{
    private readonly IReadOnlyList<SyncStream> _streams;
    private readonly IBlobStore _blobs;
    private readonly IVersionLog _log;
    private readonly StateStore _state;
    private readonly SnapshotScanner _scanner;
    private readonly IMalwareScanner _malware;
    private readonly string _dataDir;

    public SyncService(
        IEnumerable<GameDefinition> games,
        IBlobStore blobs,
        IVersionLog log,
        StateStore state,
        SnapshotScanner scanner,
        IMalwareScanner malware,
        DeviceInfo device,
        string dataDir)
    {
        Device = device;
        _blobs = blobs;
        _log = log;
        _state = state;
        _scanner = scanner;
        _malware = malware;
        _dataDir = dataDir;
        _streams = games.SelectMany(StreamsFor).ToList();
    }

    public DeviceInfo Device { get; }

    public IReadOnlyList<SyncStream> Streams => _streams;

    // ---- planning and syncing ----

    public async Task<IReadOnlyList<GamePlan>> PlanAsync(IReadOnlyCollection<GameId>? only, CancellationToken ct)
    {
        var plans = new List<GamePlan>();
        foreach (var stream in Select(only))
        {
            plans.Add(await TryPlanAsync(stream, treatAsInSession: false, ct));
        }

        return plans;
    }

    /// <summary>Runs each plan exactly as planned (SYNC-14). A game that changed since planning is planned again, and its result says so.</summary>
    public async Task<IReadOnlyList<GameResult>> ExecuteAsync(IReadOnlyList<GamePlan> plans, CancellationToken ct)
    {
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

                var current = plan;
                var fresh = await PlanStreamAsync(plan.Stream, plan.TreatAsInSession, ct);
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
                results.Add(Fail(plan.Stream, e));
            }
        }

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

        var games = jobs.Select(j => j.Game).Distinct().ToList();
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
        var plan = await TryPlanAsync(stream, treatAsInSession: true, ct);
        return (await ExecuteAsync([plan], ct))[0];
    }

    /// <summary>
    /// Makes <paramref name="version"/> current: this PC's files are kept first unless already stored, the version's
    /// files become a new current version in the cloud, then they're swapped into place. Swap and undo use this too.
    /// </summary>
    public async Task<GameResult> RestoreAsync(GameId streamId, VersionId version, CancellationToken ct)
    {
        var stream = Find(streamId);
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
            $"{Device.Name}'s save before restoring {version}", state.Base?.Id, ct);

        var heads = VersionGraph.Heads(versions);
        var current = await AppendAsync(stream, target.Files, VersionKind.Normal, VersionOrigin.Restore,
            heads.FirstOrDefault()?.Id, heads.Skip(1).Select(h => h.Id).ToList(), pinned: false, $"Restored from {version}", ct);
        await RestoreFilesAsync(stream, current, snapshot.Files, ct);

        var status = StatusFor(stream);
        _state.SetStatus(stream.Id, status, $"Restored {version}.");
        _state.Log(stream.Id, "info", $"Restored version {version} as {current.Id}.");
        return new GameResult(stream.Id, stream.Definition.Title, SyncAction.Download, status, $"Restored {version}. Your previous files are kept in history.")
        {
            NewVersion = current.Id,
        };
    }

    /// <summary>SYNC-04: switches to the save that lost the game's most recent conflict.</summary>
    public async Task<GameResult> SwapAsync(GameId game, CancellationToken ct)
    {
        var stream = Main(game);
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
            _state.SetStatus(stream.Id, GameStatus.Synced, $"Conflict resolved: kept {Device.Name}'s save.");
            _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {Device.Name}'s save as {kept.Id}.");
            return new GameResult(stream.Id, stream.Definition.Title, SyncAction.Upload, GameStatus.Synced,
                $"Kept {Device.Name}'s save. The cloud's is pinned in history.") { NewVersion = kept.Id };
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
        _state.SetStatus(stream.Id, GameStatus.Synced, $"Conflict resolved: kept {winner.Device.Name}'s save.");
        _state.Log(stream.Id, "info", $"Conflict resolved by hand: kept {winner.Device.Name}'s save ({winner.Id}).");
        return new GameResult(stream.Id, stream.Definition.Title, SyncAction.Download, GameStatus.Synced,
            $"Kept {winner.Device.Name}'s save. {Device.Name}'s is pinned in history.");
    }

    public async Task PinAsync(GameId streamId, VersionId version, string label, CancellationToken ct)
    {
        var stream = Find(streamId);
        await _log.SetPinAsync(stream.Id, new PinRecord(version, label, DateTime.UtcNow, Device), ct);
    }

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
        var (versions, _) = await LoadVersionsAsync(stream, ct);
        var thinned = await _log.ListThinnedAsync(stream.Id, ct);
        var pins = (await _log.ListPinsAsync(stream.Id, ct)).ToDictionary(p => p.Version);
        var heads = VersionGraph.Heads(versions).Select(h => h.Id).ToHashSet();
        var baseId = _state.GetState(stream.Id).Base?.Id;
        return versions
            .Where(v => !thinned.Contains(v.Id))
            .OrderByDescending(v => v.CreatedUtc)
            .Select(v => new HistoryEntry(v, heads.Contains(v.Id), v.Id == baseId, v.Pinned || pins.ContainsKey(v.Id),
                pins.TryGetValue(v.Id, out var pin) ? pin.Label : v.Label))
            .ToList();
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

    /// <summary>Thins exactly what <paramref name="preview"/> listed, or refuses if the history changed since.</summary>
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

    // ---- planning internals ----

    private async Task<GamePlan> TryPlanAsync(SyncStream stream, bool treatAsInSession, CancellationToken ct)
    {
        try
        {
            return await PlanStreamAsync(stream, treatAsInSession, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException and not SimulatedCrashException)
        {
            return new GamePlan { Stream = stream, Error = e, TreatAsInSession = treatAsInSession };
        }
    }

    private async Task<GamePlan> PlanStreamAsync(SyncStream stream, bool treatAsInSession, CancellationToken ct)
    {
        var state = _state.GetState(stream.Id);
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
        });

        return new GamePlan
        {
            Stream = stream,
            Decision = decision,
            Local = snapshot.Files,
            Warnings = snapshot.Warnings.Concat(problems).ToList(),
            Base = state.Base,
            Versions = versions,
            TreatAsInSession = treatAsInSession,
            Fingerprint = Fingerprint(snapshot.Files, versions) + $"|{state.Base?.Id}|{state.Status}|{state.Reinstalled}",
        };
    }

    /// <summary>Reads the game's versions and drops any that fail the same checks a restore would apply (R8).</summary>
    private async Task<(IReadOnlyList<VersionRecord> Valid, IReadOnlyList<string> Problems)> LoadVersionsAsync(SyncStream stream, CancellationToken ct)
    {
        var valid = new List<VersionRecord>();
        var problems = new List<string>();
        foreach (var version in await _log.ListAsync(stream.Id, ct))
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

        return (valid, problems);
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
        VersionRecord? created = null;

        switch (decision.Action)
        {
            case SyncAction.None:
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
                break;
            case SyncAction.NeedsYou:
                // Nothing moves either way (SYNC-11), but this PC's side is kept safe in history while it waits.
                await KeepIfNotStoredAsync(stream, plan.Local, plan.Versions, VersionOrigin.KeptInConflict,
                    $"{Device.Name}'s save while a conflict waits for you", plan.Base?.Id, ct);
                status = GameStatus.Conflict;
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

        _state.SetStatus(stream.Id, status, decision.Notice ?? decision.Reason);
        if (decision.Action is not SyncAction.None)
        {
            _state.Log(stream.Id, status is GameStatus.Conflict or GameStatus.NotAvailable or GameStatus.SavesMissing ? "warn" : "info",
                decision.Notice is null ? decision.Reason : $"{decision.Reason} {decision.Notice}");
        }

        return new GameResult(stream.Id, stream.Definition.Title, decision.Action, status, decision.Reason)
        {
            Notice = decision.Notice,
            Warnings = plan.Warnings,
            NewVersion = created?.Id,
        };
    }

    private GameResult Fail(SyncStream stream, Exception e)
    {
        var (status, message) = e switch
        {
            FileInUseException inUse => (GameStatus.FilesInUse, $"Can't read {inUse.FilePath}: another program has it open. Other games carry on."),
            BlockedException or UnsafePathException => (GameStatus.Blocked, e.Message),
            BlobMismatchException => (GameStatus.Error, $"{e.Message} It'll be tried again."),
            InvalidGameDefinitionException => (GameStatus.Error, e.Message),
            _ => (GameStatus.Error, e.Message),
        };
        _state.SetStatus(stream.Id, status, message);
        _state.Log(stream.Id, "error", message);
        return new GameResult(stream.Id, stream.Definition.Title, null, status, message);
    }

    // ---- uploads (BAK-12: contents first, record last) ----

    private async Task<VersionRecord> UploadAsync(SyncStream stream, IReadOnlyList<FileEntry> files, VersionKind kind, VersionOrigin origin,
        VersionId? parent, IReadOnlyList<VersionId> supersedes, bool pinned, string? label, CancellationToken ct)
    {
        foreach (var file in files.DistinctBy(f => f.Hash))
        {
            if (await _blobs.ExistsAsync(stream.Id, file.Hash, ct))
            {
                continue;
            }

            var fullPath = LocalPath(stream.Definition, file);
            await using var content = OpenForReading(fullPath);
            await _blobs.PutAsync(stream.Id, file.Hash, content, ct);
        }

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

        foreach (var file in currentLocal.Where(f => stream.Includes(f.Category)))
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
        }
        catch (Exception e) when (e is not SimulatedCrashException)
        {
            journal.Abort();
            journal.Delete(_dataDir);
            throw;
        }

        journal = journal with { Ready = true };
        journal.Save(_dataDir);
        CrashPoints.Hit(CrashPoints.AfterStagingBeforeSwap);
        journal.Commit();
        _state.SetBase(stream.Id, target);
        journal.Delete(_dataDir);
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

    private IEnumerable<SyncStream> Select(IReadOnlyCollection<GameId>? only) =>
        only is null ? _streams : _streams.Where(s => only.Contains(s.Id) || only.Contains(s.Game.Id));

    private SyncStream Main(GameId game) =>
        _streams.FirstOrDefault(s => !s.PerDevice && s.Game.Id == game) ?? throw new InvalidOperationException($"There's no game '{game}'.");

    private SyncStream Find(GameId id) =>
        _streams.FirstOrDefault(s => s.Id == id) ?? Main(id);

    private static GameStatus StatusFor(SyncStream stream) =>
        stream.Definition.Mode == GameMode.BackupOnly ? GameStatus.BackupOnly : GameStatus.Synced;

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
