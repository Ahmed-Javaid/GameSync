using System.Globalization;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Sessions;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>Where the agent says what it did: the terminal for <c>gamesync agent</c>, the app's window, tray icon and notifications.</summary>
public interface IAgentOutput
{
    void Say(string line);

    /// <summary>BG-05: something that needs the person: a conflict, saves missing, a blocked file, an expired sign-in, saves that moved.</summary>
    void NeedsYou(string title, string message);

    /// <summary>What <paramref name="title"/> needed the person for is over, so the next problem is news again.</summary>
    void Fine(string title)
    {
    }

    /// <summary>BG-05: news the person asked for that needs nothing of them, such as the daily backup's line.</summary>
    void Tell(string title, string message)
    {
        Say($"{title}: {message}");
    }

    /// <summary>Called every round: shows what was held back while a fullscreen game ran (BG-06).</summary>
    void Flush()
    {
    }

    /// <summary>BG-07: the agent starts (true) or finishes (false) work that reads saves or talks to the cloud.</summary>
    void Working(bool busy)
    {
    }

    /// <summary>A sync finished, with each game's result.</summary>
    void Synced(IReadOnlyList<GameResult> results)
    {
    }

    /// <summary>A game started (true) or stopped (false) being played.</summary>
    void Played(GameId game, string title, bool playing)
    {
    }

    /// <summary>KAN-80: an upload or a download of a game's saves moved on, or ended.</summary>
    void Transfer(TransferUpdate update)
    {
    }
}

/// <summary>
/// The agent (design.md → Background work). It notices games being played, however they were started (PLAY-05), and
/// around each session marks the game as playing for the other PCs (PLAY-07), records the session, syncs the game once
/// it has closed, and checks its saves didn't move (FIND-07). Between sessions it retries what waits to upload (BG-04)
/// and keeps the save before a game update runs (BAK-06). While a game that syncs plays, it only watches (BG-08). Every
/// other game installed here is watched too, so Home shows what's playing and its play counts (PLAY-12).
/// </summary>
internal sealed class Agent(string dataDir, IAgentOutput output) : IDisposable
{
    public static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GamesEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BuildsEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromHours(1);

    private static readonly TimeSpan ProgramsEvery = TimeSpan.FromHours(1);

    private readonly StateStore _state = new(dataDir);
    private readonly SessionTracker _tracker = new();
    private readonly ProcessWatcher _watcher = new();
    private readonly Dictionary<GameId, SaveActivity> _activity = [];
    private readonly QuietFolderTracker _quiet = new();
    private readonly Dictionary<GameId, (IReadOnlyList<string> Folders, SaveActivity Activity)> _quietFolders = [];
    private readonly Dictionary<GameId, SessionInfo> _ended = [];
    private readonly Dictionary<GameId, (string Folder, GamePrograms Programs, DateTime At)> _known = [];

    // FIND-04: the sessions learn mode is watching, for games GameSync hasn't found saves for.
    private readonly Dictionary<GameId, LearnWatch> _learning = [];
    private IReadOnlyList<GamePrograms> _programs = [];
    private Dictionary<GameId, (string Title, IReadOnlyList<string> Folders)> _games = [];
    private Dictionary<GameId, string> _titles = [];
    private IReadOnlyList<GameProcess> _running = [];
    private DateTime _gamesAt = DateTime.MinValue;
    private DateTime _buildsAt = DateTime.MinValue;
    private DateTime _syncAt = DateTime.MinValue;
    private DateTime _retryAt = DateTime.MinValue;
    private TimeSpan _retryWait = FirstRetry;
    private int _syncSoon;
    private int _watchSoon;

    // KAN-88: in the app, uploads run beside the rounds under their own lock, so a long one never holds back the
    // person's actions, the check before playing, or the rounds noticing a game start.
    private readonly object _uploadsLock = new();
    private Task _uploads = Task.CompletedTask;
    private CancellationTokenSource? _uploadsStop;
    private int _uploadSoon = 1;
    private DateTime _uploadAt = DateTime.MaxValue;
    private TimeSpan _uploadWait = FirstRetry;
    private volatile IReadOnlySet<GameId> _busy = new HashSet<GameId>();
    private volatile string? _pausedFor;
    private volatile bool _holding;
    private int _working;

    public const string DailyRequestKey = "daily.requested";

    public static string DoneKey(GameId game) => $"session.done.{game}";

    /// <summary>The games playing now, with when each session started.</summary>
    public IReadOnlyDictionary<GameId, DateTime> Playing => _tracker.Playing;

    /// <summary>BG-08: a game that syncs is being played, so nothing reads the disk for GameSync meanwhile. Any thread.</summary>
    public bool HoldsDiskWork => _holding;

    /// <summary>Sync now, from the window or the tray: every game syncs at the next round with nothing playing (BG-08). Any thread.</summary>
    public void SyncSoon() => Interlocked.Exchange(ref _syncSoon, 1);

    /// <summary>The games are read again at the next round, so a game just located or found is watched at once (PLAY-12). Any thread.</summary>
    public void WatchSoon() => Interlocked.Exchange(ref _watchSoon, 1);

    /// <summary>
    /// KAN-88: syncs do their work on this PC and the uploads run on their own beside the rounds (the app), pausing while
    /// a game that syncs is played. False syncs and uploads within the round, as a terminal agent always has.
    /// </summary>
    public bool UploadsInBackground { get; init; }

    /// <summary>KAN-88: something waits to upload (a person's action in the app): it goes up at the next round, whatever the wait after a failure. Any thread.</summary>
    public void UploadSoon() => Interlocked.Exchange(ref _uploadSoon, 1);

    /// <summary>The uploads under way in the background, if any (tests wait for them).</summary>
    internal Task UploadsUnderWay
    {
        get
        {
            lock (_uploadsLock)
            {
                return _uploads;
            }
        }
    }

    public void Dispose()
    {
        // Stopping: an upload under way stops where it is; the outbox keeps the rest for the next start.
        Task uploads;
        lock (_uploadsLock)
        {
            _uploadsStop?.Cancel();
            uploads = _uploads;
        }

        try
        {
            uploads.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Stopped part way, as it should.
        }

        foreach (var activity in _activity.Values.Concat(_quietFolders.Values.Select(q => q.Activity)))
        {
            activity.Dispose();
        }

        // A session learn mode was watching can't finish now: the next one is watched again.
        foreach (var watch in _learning.Values)
        {
            LearnMode.Abandon(dataDir, watch);
        }

        _learning.Clear();
        _state.Dispose();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var leftovers = true;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (leftovers)
                {
                    RefreshGames(DateTime.UtcNow);
                    CloseLeftoverSessions(DateTime.UtcNow);
                    leftovers = false;
                }

                await TickAsync(DateTime.UtcNow, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                output.Say($"! {e.Message}");
            }

            try
            {
                await Task.Delay(TickEvery, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Stopping (sign-out, shutdown): sessions still open are recorded up to now, and sync at the next start; a folder
        // still changing, up to its last change.
        foreach (var game in _tracker.Playing.Keys.ToList())
        {
            if (_tracker.EndByHand(game, DateTime.UtcNow, _running) is { } ended)
            {
                Record(ended);
            }
        }

        foreach (var ended in _quiet.EndAll())
        {
            RecordQuiet(ended);
        }
    }

    /// <summary>One round: find the games' processes, move sessions on, and between sessions do what waits.</summary>
    public async Task TickAsync(DateTime nowUtc, CancellationToken ct)
    {
        var asked = Interlocked.Exchange(ref _watchSoon, 0) == 1;
        if (asked || nowUtc - _gamesAt >= GamesEvery)
        {
            RefreshGames(nowUtc);
        }

        _running = _watcher.Find(_programs);
        foreach (var game in _state.GetSettings("session.done.").Keys.Select(k => GameId.Parse(k["session.done.".Length..])).ToList())
        {
            _state.SetSetting(DoneKey(game), "");
            if (_tracker.EndByHand(game, nowUtc, _running) is { } ended)
            {
                // What of it still runs, such as an emulator still closing, isn't the game for the app's actions either.
                _state.SetSetting(RunningGames.EndedByHandKey(game), nowUtc.ToString("O", CultureInfo.InvariantCulture));
                Record(ended);
            }
        }

        output.Flush();
        var writes = _activity.Where(a => a.Value.LastWriteUtc is not null).ToDictionary(a => a.Key, a => a.Value.LastWriteUtc!.Value);
        foreach (var change in _tracker.Tick(nowUtc, _running, writes))
        {
            switch (change)
            {
                case SessionStarted started:
                    await StartedAsync(started, ct);
                    break;
                case SessionEnded ended:
                    Record(ended);
                    break;
            }
        }

        // LIB-13: the person's own folders with no program here are in use while they change, and sync once quiet.
        var quietWrites = _quietFolders.Where(q => q.Value.Activity.LastWriteUtc is not null).ToDictionary(q => q.Key, q => q.Value.Activity.LastWriteUtc!.Value);
        foreach (var change in _quiet.Tick(nowUtc, quietWrites))
        {
            switch (change)
            {
                case SessionStarted started:
                    _state.SetSetting(RunningGames.QuietOpenKey(started.Game), started.StartUtc.ToString("O", CultureInfo.InvariantCulture));
                    output.Say($"{Title(started.Game)}: changing since {started.StartUtc.ToLocalTime():HH:mm:ss}; it syncs once it's been quiet for 5 minutes.");
                    break;
                case SessionEnded ended:
                    RecordQuiet(ended);
                    break;
            }
        }

        _busy = _tracker.Playing.Keys.Concat(Live.Select(p => p.Game)).Concat(_quiet.Active.Keys).ToHashSet();

        // BG-08: no disk work while a game that syncs plays. A game that doesn't sync is only watched for Home (PLAY-12),
        // as it was before GameSync watched it at all: a program taken for a game, such as software running all day
        // before Steam has said it's software, never holds every sync back.
        _holding = _tracker.Playing.Keys.Any(Syncs);
        if (_tracker.Playing.Keys.Where(Syncs).Select(Title).FirstOrDefault() is { } playing)
        {
            StopUploads(playing);
            return;
        }

        if (_ended.Count > 0)
        {
            await WorkAsync(() => SyncEndedAsync(ct));
        }

        // BG-02: the scheduled daily run hands itself to the agent when it's running.
        if (_state.GetSetting(DailyRequestKey) is { Length: > 0 })
        {
            _state.SetSetting(DailyRequestKey, "");
            await WorkAsync(() => Daily.RunAsync(dataDir, output, IsRunningNow, ct, deferUploads: UploadsInBackground));
            Synced();
            _syncAt = nowUtc + SyncEvery;
        }

        if (Interlocked.Exchange(ref _syncSoon, 0) == 1)
        {
            _syncAt = DateTime.MinValue;
        }

        if (nowUtc >= _syncAt)
        {
            await SyncAllAsync(nowUtc, ct);
        }
        else if (nowUtc >= _retryAt)
        {
            await RetryAsync(nowUtc, ct);
        }

        if (nowUtc - _buildsAt >= BuildsEvery)
        {
            _buildsAt = nowUtc;
            await CheckBuildsAsync(ct);
        }

        if (UploadsInBackground)
        {
            StartUploads(nowUtc, ct);
        }
    }

    /// <summary>
    /// KAN-88: what waits in the outbox goes up beside the rounds: after the round's syncs, after the person's action,
    /// and after a failure once the wait is over (1 minute, then twice as long each time up to an hour, BG-04). One runs
    /// at a time; one asked for meanwhile follows it.
    /// </summary>
    private void StartUploads(DateTime nowUtc, CancellationToken ct)
    {
        lock (_uploadsLock)
        {
            if (!_uploads.IsCompleted)
            {
                return;
            }

            var asked = Interlocked.Exchange(ref _uploadSoon, 0) == 1;
            if (!asked && nowUtc < _uploadAt)
            {
                return;
            }

            _uploadsStop?.Dispose();
            _uploadsStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stop = _uploadsStop.Token;
            _uploads = Task.Run(() => UploadAsync(stop), CancellationToken.None);
        }
    }

    /// <summary>BG-08: a game that syncs started (<paramref name="playing"/>); the upload under way stops where it is and goes on once it closes.</summary>
    private void StopUploads(string playing)
    {
        lock (_uploadsLock)
        {
            if (!_uploads.IsCompleted)
            {
                _pausedFor = playing;
                _uploadsStop?.Cancel();
                Interlocked.Exchange(ref _uploadSoon, 1);
            }
        }
    }

    private async Task UploadAsync(CancellationToken ct)
    {
        Working(true);
        _pausedFor = null;

        // KAN-80: each game's upload shows on its saves as it goes, and how it ended.
        var titles = _games.ToDictionary(g => g.Key, g => g.Value.Title);
        var relay = new TransferRelay(output) { TitleOf = titles.GetValueOrDefault };
        try
        {
            // With no cloud connected yet there's nothing to upload to, and connecting one asks for the upload.
            var results = await Uploads.RunAsync(dataDir, game => _busy.Contains(game), ct, relay);
            if (results.Count > 0)
            {
                Report(output, results);
            }

            // Failed: try again after the wait, doubling each time. Done: nothing until there's more to upload.
            var failed = results.Any(r => r.CloudProblem is not null);
            DateTime retryAt;
            lock (_uploadsLock)
            {
                _uploadWait = failed ? (_uploadAt == DateTime.MaxValue ? FirstRetry : TimeSpan.FromTicks(Math.Min(_uploadWait.Ticks * 2, LongestRetry.Ticks))) : FirstRetry;
                _uploadAt = failed ? DateTime.UtcNow + _uploadWait : DateTime.MaxValue;
                retryAt = _uploadAt;
            }

            relay.End(results, failed ? retryAt.ToLocalTime() : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A game started, or the app is stopping: the outbox keeps what's left.
            if (_pausedFor is { } playing)
            {
                relay.Stop(TransferState.Paused, $"Paused while you play {playing}; it goes on once you quit.");
            }
        }
        catch (Exception e)
        {
            output.Say($"! Uploading: {e.Message}");
            DateTime retryAt;
            lock (_uploadsLock)
            {
                _uploadWait = TimeSpan.FromTicks(Math.Min(_uploadWait.Ticks * 2, LongestRetry.Ticks));
                _uploadAt = DateTime.UtcNow + _uploadWait;
                retryAt = _uploadAt;
            }

            relay.Stop(TransferState.Failed, TransferRelay.Failed(e.Message, retryAt.ToLocalTime()));
        }
        finally
        {
            Working(false);
        }
    }

    /// <summary>BG-07: the tray icon shows work while any is under way: the round's, the uploads beside it, or both.</summary>
    private void Working(bool busy)
    {
        var count = busy ? Interlocked.Increment(ref _working) : Interlocked.Decrement(ref _working);
        if (busy ? count == 1 : count == 0)
        {
            output.Working(busy);
        }
    }

    /// <summary>What a round's sync waits for again soon: a download (BG-04), and an upload unless the uploads run on their own.</summary>
    private bool Waits(GameStatus? status) =>
        status is GameStatus.NewerInCloud || (status is GameStatus.UploadPending && !UploadsInBackground);

    /// <summary>The round's syncs, with the uploads after them when those run on their own (KAN-88).</summary>
    private SyncOptions Options() => new() { IsRunning = IsRunningNow, DeferUploads = UploadsInBackground };

    /// <summary>The round synced something: in the app, its uploads go next.</summary>
    private void Synced()
    {
        if (UploadsInBackground)
        {
            UploadSoon();
        }
    }

    /// <summary>Work that reads saves or talks to the cloud, with the tray icon showing it (BG-07).</summary>
    private async Task WorkAsync(Func<Task> work)
    {
        Working(true);
        try
        {
            await work();
        }
        finally
        {
            Working(false);
        }
    }

    /// <summary>
    /// Which games sync and their save folders, and the programs of every game installed here (PLAY-12), read again now
    /// and then so new ones are watched. A game's programs are looked for again within the hour, or when it moves.
    /// </summary>
    private void RefreshGames(DateTime nowUtc)
    {
        using var engine = Engine.Open(dataDir);
        using var art = new Core.Art.ArtCache(dataDir);
        var watched = engine.Watched(art.IsSoftware);
        var programs = new List<GamePrograms>();
        foreach (var (id, _, folder, _) in watched)
        {
            if (!_known.TryGetValue(id, out var known) || !string.Equals(known.Folder, folder, StringComparison.OrdinalIgnoreCase) || nowUtc - known.At >= ProgramsEvery)
            {
                known = (folder, GamePrograms.For(id, folder), nowUtc);
                _known[id] = known;
            }

            if (known.Programs.Names.Count > 0)
            {
                programs.Add(known.Programs);
            }
        }

        foreach (var gone in _known.Keys.Where(k => watched.All(w => w.Id != k)).ToList())
        {
            _known.Remove(gone);
        }

        _programs = programs;

        // A game still playing that's no longer watched (Steam has just said it's software) keeps its name for the session's end.
        var titles = watched.ToDictionary(w => w.Id, w => w.Title);
        foreach (var playing in _tracker.Playing.Keys.Where(g => !titles.ContainsKey(g) && _titles.ContainsKey(g)))
        {
            titles[playing] = _titles[playing];
        }

        _titles = titles;
        _games = engine.Games.ToDictionary(
            g => g.Id,
            g => (g.Title, (IReadOnlyList<string>)g.Roots.Values.Where(f => !RootResolver.IsUnresolved(f) && Directory.Exists(f)).ToList()));
        WatchOwnFolders(engine.Library.All());
        _gamesAt = nowUtc;
    }

    /// <summary>
    /// LIB-13: each folder of the person's own that syncs and has no program on this PC is watched all the time, as a
    /// playing game's saves are, since its quiet spells are its sessions.
    /// </summary>
    private void WatchOwnFolders(IReadOnlyList<LibraryEntry> library)
    {
        var own = library.Where(e => e is { IsOwnFolder: true, State: LibraryState.Synced, MergedInto: null } && _games.ContainsKey(e.Id))
            .ToDictionary(e => e.Id, e => _games[e.Id].Folders);
        foreach (var (game, folders) in own)
        {
            if (!_quietFolders.TryGetValue(game, out var watched) || !watched.Folders.SequenceEqual(folders, StringComparer.OrdinalIgnoreCase))
            {
                watched.Activity?.Dispose();
                _quietFolders[game] = (folders, new SaveActivity(folders));
            }
        }

        foreach (var gone in _quietFolders.Keys.Where(g => !own.ContainsKey(g)).ToList())
        {
            _quietFolders[gone].Activity.Dispose();
            _quietFolders.Remove(gone);
        }
    }

    private string Title(GameId game) => _games.TryGetValue(game, out var known) ? known.Title : _titles.GetValueOrDefault(game, game.Value);

    /// <summary>The game's saves sync, so its session marks it as playing for the other PCs and it syncs when it closes.</summary>
    private bool Syncs(GameId game) => _games.ContainsKey(game);

    /// <summary>
    /// A session the agent had open when it stopped without closing it (a crash, a power cut): it's recorded from its
    /// start to the last save written since, and synced; if the game still runs, watching picks it up again.
    /// </summary>
    private void CloseLeftoverSessions(DateTime nowUtc)
    {
        // A folder of the person's own that was still changing (LIB-13) is closed the same way.
        foreach (var (key, value) in _state.GetSettings("session.open.").Concat(_state.GetSettings(RunningGames.QuietOpenPrefix)))
        {
            _state.SetSetting(key, "");
            var id = key.StartsWith(RunningGames.QuietOpenPrefix, StringComparison.Ordinal) ? key[RunningGames.QuietOpenPrefix.Length..] : key["session.open.".Length..];
            if (GameId.TryParse(id, out var game) &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
            {
                var session = new SessionInfo(start, SaveActivity.LastWrite(_games.TryGetValue(game, out var known) ? known.Folders : [], start, nowUtc));
                _state.AddSession(game, session);
                _ended[game] = session;
            }
        }
    }

    /// <summary>
    /// A session starts: it's kept open in this PC's state, so Home shows the game as playing (PLAY-12). A game whose saves
    /// sync is also marked as playing, here and for the other PCs (PLAY-07); one that doesn't sync only has its play counted.
    /// </summary>
    private async Task StartedAsync(SessionStarted started, CancellationToken ct)
    {
        var game = started.Game;
        _state.SetSetting(RunningGames.OpenSessionKey(game), started.StartUtc.ToString("O", CultureInfo.InvariantCulture));
        _activity[game] = new SaveActivity(_games.TryGetValue(game, out var known) ? known.Folders : []);
        output.Say($"{Title(game)}: playing since {started.StartUtc.ToLocalTime():HH:mm:ss}.");
        StartLearning(game, started.StartUtc);
        output.Played(game, Title(game), playing: true);
        if (!Syncs(game))
        {
            return;
        }

        _state.SetStatus(game, GameStatus.Playing, $"Playing since {started.StartUtc.ToLocalTime():HH:mm}.");
        try
        {
            await Markers.SetAsync(dataDir, game, started.StartUtc, ct);
        }
        catch (Exception e) when (e is UsageException or CloudException or IOException or InvalidOperationException)
        {
            output.Say($"! {Title(game)}: the other PCs couldn't be told it's playing: {e.Message}");
        }
    }

    /// <summary>The session goes into this PC's state at once, so its changes count as made during play; the sync waits until nothing plays.</summary>
    private void Record(SessionEnded ended)
    {
        if (_activity.Remove(ended.Game, out var activity))
        {
            activity.Dispose();
        }

        _state.AddSession(ended.Game, ended.Session);
        _state.SetSetting(RunningGames.OpenSessionKey(ended.Game), "");
        if (Syncs(ended.Game))
        {
            _ended[ended.Game] = ended.Session;

            // The game's log says how long it was played, before the line of the sync that follows (MGR-09).
            _state.Log(ended.Game, "info", $"Played {Launcher.DurationText(ended.Session.EndUtc - ended.Session.StartUtc)}.", EventTags.Session);
        }

        var minutes = Math.Max(1, (int)Math.Round((ended.Session.EndUtc - ended.Session.StartUtc).TotalMinutes));
        output.Say($"{Title(ended.Game)}: session over after {minutes} min{(ended.ByHand ? ", ended by hand" : "")}.");
        FinishLearning(ended.Game, ended.Session);
        output.Played(ended.Game, Title(ended.Game), playing: false);
    }

    /// <summary>
    /// FIND-04: a game GameSync hasn't found saves for starts, so learn mode watches where it writes (no admin, nothing
    /// touches the game). Never for a game with an anti-cheat (R15); a problem only means this session isn't watched.
    /// </summary>
    private void StartLearning(GameId game, DateTime startUtc)
    {
        try
        {
            if (!_learning.ContainsKey(game) && LearnMode.Begin(dataDir, game, _known.TryGetValue(game, out var known) ? known.Folder : null, startUtc) is { } watch)
            {
                _learning[game] = watch;
                output.Say($"{Title(game)}: learn mode is watching where it saves.");
            }
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            output.Say($"! {Title(game)}: learn mode couldn't watch this session: {e.Message}");
        }
    }

    /// <summary>The session learn mode watched is over: the places it wrote to are kept for the person, who is told.</summary>
    private void FinishLearning(GameId game, SessionInfo session)
    {
        if (!_learning.Remove(game, out var watch))
        {
            return;
        }

        try
        {
            var finds = LearnMode.Finish(dataDir, watch, session);
            if (finds.Places.Count == 0)
            {
                output.Say($"{Title(game)}: learn mode saw nothing written that looks like a save; it watches again next time.");
                return;
            }

            output.Say($"{Title(game)}: learn mode found {finds.Places.Count} place{(finds.Places.Count == 1 ? "" : "s")} it saved to.");
            output.Tell($"{Title(game)}: learn mode found where it saves", "Open GameSync to choose what to sync. Nothing syncs until you do.");
            WatchSoon();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            output.Say($"! {Title(game)}: learn mode couldn't keep what it found: {e.Message}");
        }
    }

    /// <summary>
    /// LIB-13: a folder of the person's own has been quiet for 5 minutes (or GameSync is stopping): its spell of changes is
    /// recorded as a session, so they count as made in one, and it syncs once nothing plays.
    /// </summary>
    private void RecordQuiet(SessionEnded ended)
    {
        _state.AddSession(ended.Game, ended.Session);
        _state.SetSetting(RunningGames.QuietOpenKey(ended.Game), "");
        if (Syncs(ended.Game))
        {
            _ended[ended.Game] = ended.Session;
        }

        output.Say($"{Title(ended.Game)}: quiet since {ended.Session.EndUtc.ToLocalTime():HH:mm:ss}; syncing what changed.");
    }

    /// <summary>The games whose sessions ended sync, their markers go, and each is checked for saves that moved.</summary>
    private async Task SyncEndedAsync(CancellationToken ct)
    {
        var sessions = new Dictionary<GameId, SessionInfo>(_ended);
        _ended.Clear();
        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        using var engine = Engine.Open(dataDir);
        var games = sessions.Keys.Where(g => engine.Games.Any(d => d.Id == g)).ToList();
        if (games.Count == 0)
        {
            return;
        }

        var (service, _) = await engine.OpenServiceAsync(Options(), ct);
        var results = await service.SyncAsync(games, ct);
        await Markers.ClearAsync(engine, service, IsRunningNow, ct);
        Report(output, results);
        Synced();
        foreach (var game in games)
        {
            CheckMovedSaves(engine, game, sessions[game], results);
        }

        if (results.Any(r => Waits(r.Status)))
        {
            _retryAt = DateTime.UtcNow + FirstRetry;
        }
    }

    /// <summary>
    /// Every 15 minutes with nothing playing, and at the start: every game syncs, so what another PC played comes down
    /// with no click. It's the daily run's safety net, more often: a change made outside play is only held, and a save is
    /// only brought down when this PC's is unchanged.
    /// </summary>
    private async Task SyncAllAsync(DateTime nowUtc, CancellationToken ct)
    {
        _syncAt = nowUtc + SyncEvery;
        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        using var engine = Engine.Open(dataDir);
        if (engine.Games.Count == 0)
        {
            return;
        }

        await WorkAsync(async () =>
        {
            var (service, recovered) = await engine.OpenServiceAsync(Options(), ct);
            var results = recovered.Concat(await service.SyncAsync(null, ct)).ToList();
            await Markers.ClearAsync(engine, service, IsRunningNow, ct);
            Report(output, results);
            Synced();
            (_retryWait, _retryAt) = results.Any(r => Waits(r.Status))
                ? (FirstRetry, nowUtc + FirstRetry)
                : (FirstRetry, _syncAt);
        });
    }

    /// <summary>
    /// BG-04: games whose upload or download waits (offline, Drive full) try again after 1 minute, then 2, 4, up to an
    /// hour. With no cloud connected yet (first run's Skip for now) there's nothing to try until one is.
    /// </summary>
    private async Task RetryAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        var waiting = engine.Config.HasNoCloud ? [] : engine.Games.Where(g => Waits(engine.State.GetState(g.Id).Status))
            .Select(g => g.Id).ToList();
        if (waiting.Count == 0)
        {
            (_retryWait, _retryAt) = (FirstRetry, nowUtc + FirstRetry);
            return;
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        await WorkAsync(async () =>
        {
            var (service, _) = await engine.OpenServiceAsync(Options(), ct);
            var results = await service.SyncAsync(waiting, ct);
            Report(output, results);
            Synced();
            var stillWaiting = results.Any(r => Waits(r.Status));
            _retryWait = stillWaiting ? TimeSpan.FromTicks(Math.Min(_retryWait.Ticks * 2, LongestRetry.Ticks)) : FirstRetry;
            _retryAt = nowUtc + _retryWait;
        });
    }

    /// <summary>BAK-06: a synced game whose build changed since it was last seen keeps its save before the new build runs.</summary>
    private async Task CheckBuildsAsync(CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        var changed = Builds.Changed(engine);
        if (changed.Count == 0)
        {
            return;
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        await WorkAsync(async () =>
        {
            var (service, _) = await engine.OpenServiceAsync(Options(), ct);
            await Builds.KeepAsync(engine, service, changed, output, ct);
            Synced();
        });
    }

    /// <summary>
    /// FIND-07: the folders the game syncs stayed quiet all session while a place named like the game changed, so its
    /// saves may have moved. The new place becomes a suggestion (<c>gamesync show</c>), and the person is told.
    /// </summary>
    private void CheckMovedSaves(Engine engine, GameId game, SessionInfo session, IReadOnlyList<GameResult> results)
    {
        if (results.Any(r => r.Game == game && r.Action is SyncAction.Upload or SyncAction.UploadHeld))
        {
            engine.State.SetSetting($"moved.{game}", "");
            return;
        }

        if (engine.Library.All().FirstOrDefault(e => e.Id == game) is not { Installed: true, Store: { } store, InstallDir: { } folder, Confirmed: not null } entry)
        {
            return;
        }

        var discoverer = new Discoverer(new SaveListStore(dataDir).Load(), engine.Here.Folders, Cli.SaveFolders(engine.State), engine.Here.Guard);
        var changed = discoverer.EveryLayer(new InstalledGame { Store = store, Title = entry.Title, InstallDir = folder, StoreId = entry.StoreId })
            .Where(p => p.NewestUtc >= session.StartUtc - DecisionEngine.SessionSlack)
            .ToList();
        var moved = (entry with { Proposals = changed }).Suggestions;
        if (moved.Count == 0)
        {
            return;
        }

        engine.Library.SaveAll([entry with { Proposals = entry.Proposals.Concat(moved).DistinctBy(p => (p.Root, p.Include)).ToList() }]);
        engine.State.SetSetting($"moved.{game}", string.Join("; ", moved.Select(p => p.Root)));
        output.NeedsYou(entry.DisplayTitle,
            $"Its saves may have moved: {moved[0].Root} changed while you played, and the folder GameSync syncs didn't. See: gamesync show {game}");
    }

    /// <summary>
    /// Says what each sync did, and tells the person what needs them (BG-05): a conflict, saves missing or a blocked
    /// file, per game; an expired sign-in once for all of them. A game that's fine again can tell them again later.
    /// </summary>
    /// <summary>A status in the app's words, as its badges say them: "synced", "backed up", "held for review".</summary>
    internal static string Said(GameStatus status) => status switch
    {
        GameStatus.BackupOnly => "backed up",
        GameStatus.Conflict => "conflict",
        GameStatus.HeldForReview => "held for review",
        GameStatus.FilesInUse => "files in use",
        GameStatus.NotAvailable => "not available",
        GameStatus.SavesMissing or GameStatus.NoSaves => "saves not found",
        GameStatus.Blocked or GameStatus.Error => "blocked",
        GameStatus.UploadPending => "upload pending",
        GameStatus.NewerInCloud => "newer in the cloud",
        GameStatus.Playing => "playing",
        _ => "synced",
    };

    internal static void Report(IAgentOutput output, IReadOnlyList<GameResult> results)
    {
        static bool SignedOut(GameResult result) =>
            result.CloudProblem == CloudErrorKind.SignInExpired || result.Message.Contains("sign in again", StringComparison.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (result.Action is not (SyncAction.None or null) || result.Status is not (GameStatus.Synced or GameStatus.BackupOnly))
            {
                output.Say($"{result.Title}: {Said(result.Status)}. {result.Message}");
            }

            if (result.Status is GameStatus.Conflict or GameStatus.SavesMissing or GameStatus.Blocked)
            {
                output.NeedsYou(result.Title, result.Notice ?? result.Message);
            }
            else if (!SignedOut(result))
            {
                output.Fine(result.Title);
            }
        }

        if (results.Any(SignedOut))
        {
            output.NeedsYou("GameSync", "Google Drive needs you to sign in again; until then saves are kept on this PC and wait to upload. Run: gamesync signin");
        }
        else if (results.Count > 0)
        {
            output.Fine("GameSync");
        }

        output.Synced(results);
    }

    /// <summary>A game playing, or a folder of the person's own still changing (LIB-13): nothing touches its files meanwhile.</summary>
    private bool IsRunningNow(GameId game) => _tracker.Playing.ContainsKey(game) || Live.Any(p => p.Game == game) || _quiet.Active.ContainsKey(game);

    /// <summary>The games' processes running now, less those of a session ended by hand (PLAY-06), which no longer count.</summary>
    private IEnumerable<GameProcess> Live => _running.Where(p => !_tracker.Ignores(p.ProcessId));
}

/// <summary>When a playing game's save folders last changed, from file system notifications; nothing is read (BG-08).</summary>
internal sealed class SaveActivity : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private long _lastTicks;

    public SaveActivity(IEnumerable<string> folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += Touch;
            watcher.Created += Touch;
            watcher.Deleted += Touch;
            watcher.Renamed += Touch;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public DateTime? LastWriteUtc => Interlocked.Read(ref _lastTicks) is var ticks and > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;

    /// <summary>
    /// Where a session nobody saw end most likely ended: the last save file written between its start and now, or its
    /// start when none was.
    /// </summary>
    public static DateTime LastWrite(IEnumerable<string> folders, DateTime startUtc, DateTime nowUtc)
    {
        var last = startUtc;
        var everything = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", everything))
            {
                if (file.LastWriteTimeUtc > last && file.LastWriteTimeUtc <= nowUtc)
                {
                    last = file.LastWriteTimeUtc;
                }
            }
        }

        return last;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    private void Touch(object? sender, FileSystemEventArgs e) => Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
}

/// <summary>The agent in a terminal: a line per thing it does, with the time.</summary>
internal sealed class ConsoleAgentOutput : IAgentOutput
{
    public void Say(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {line}");

    public void NeedsYou(string title, string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  ! {title} needs you: {message}");
}
