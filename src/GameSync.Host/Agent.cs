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
}

/// <summary>
/// The agent (design.md → Background work). It notices games being played, however they were started (PLAY-05), and
/// around each session marks the game as playing for the other PCs (PLAY-07), records the session, syncs the game once
/// it has closed, and checks its saves didn't move (FIND-07). Between sessions it retries what waits to upload (BG-04)
/// and keeps the save before a game update runs (BAK-06). While any game plays, it only watches (BG-08).
/// </summary>
internal sealed class Agent(string dataDir, IAgentOutput output) : IDisposable
{
    public static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GamesEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BuildsEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromHours(1);

    private readonly StateStore _state = new(dataDir);
    private readonly SessionTracker _tracker = new();
    private readonly ProcessWatcher _watcher = new();
    private readonly Dictionary<GameId, SaveActivity> _activity = [];
    private readonly Dictionary<GameId, SessionInfo> _ended = [];
    private IReadOnlyList<GamePrograms> _programs = [];
    private Dictionary<GameId, (string Title, IReadOnlyList<string> Folders)> _games = [];
    private IReadOnlyList<GameProcess> _running = [];
    private DateTime _gamesAt = DateTime.MinValue;
    private DateTime _buildsAt = DateTime.MinValue;
    private DateTime _syncAt = DateTime.MinValue;
    private DateTime _retryAt = DateTime.MinValue;
    private TimeSpan _retryWait = FirstRetry;
    private int _syncSoon;

    public const string DailyRequestKey = "daily.requested";

    public static string DoneKey(GameId game) => $"session.done.{game}";

    /// <summary>The games playing now, with when each session started.</summary>
    public IReadOnlyDictionary<GameId, DateTime> Playing => _tracker.Playing;

    /// <summary>Sync now, from the window or the tray: every game syncs at the next round with nothing playing (BG-08). Any thread.</summary>
    public void SyncSoon() => Interlocked.Exchange(ref _syncSoon, 1);

    public void Dispose()
    {
        foreach (var activity in _activity.Values)
        {
            activity.Dispose();
        }

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

        // Stopping (sign-out, shutdown): sessions still open are recorded up to now, and sync at the next start.
        foreach (var game in _tracker.Playing.Keys.ToList())
        {
            if (_tracker.EndByHand(game, DateTime.UtcNow, _running) is { } ended)
            {
                Record(ended);
            }
        }
    }

    /// <summary>One round: find the games' processes, move sessions on, and between sessions do what waits.</summary>
    public async Task TickAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc - _gamesAt >= GamesEvery)
        {
            RefreshGames(nowUtc);
        }

        _running = _watcher.Find(_programs);
        foreach (var game in _state.GetSettings("session.done.").Keys.Select(k => GameId.Parse(k["session.done.".Length..])).ToList())
        {
            _state.SetSetting(DoneKey(game), "");
            if (_tracker.EndByHand(game, nowUtc, _running) is { } ended)
            {
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

        // BG-08: no disk work while anything plays.
        if (_tracker.Playing.Count > 0)
        {
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
            await WorkAsync(() => Daily.RunAsync(dataDir, output, IsRunningNow, ct));
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
    }

    /// <summary>Work that reads saves or talks to the cloud, with the tray icon showing it (BG-07).</summary>
    private async Task WorkAsync(Func<Task> work)
    {
        output.Working(true);
        try
        {
            await work();
        }
        finally
        {
            output.Working(false);
        }
    }

    /// <summary>Which games sync, their programs and their save folders, read again now and then so new ones are watched.</summary>
    private void RefreshGames(DateTime nowUtc)
    {
        using var engine = Engine.Open(dataDir);
        _programs = engine.Programs();
        _games = engine.Games.ToDictionary(
            g => g.Id,
            g => (g.Title, (IReadOnlyList<string>)g.Roots.Values.Where(f => !RootResolver.IsUnresolved(f) && Directory.Exists(f)).ToList()));
        _gamesAt = nowUtc;
    }

    private string Title(GameId game) => _games.TryGetValue(game, out var known) ? known.Title : game.Value;

    /// <summary>
    /// A session the agent had open when it stopped without closing it (a crash, a power cut): it's recorded from its
    /// start to the last save written since, and synced; if the game still runs, watching picks it up again.
    /// </summary>
    private void CloseLeftoverSessions(DateTime nowUtc)
    {
        foreach (var (key, value) in _state.GetSettings("session.open."))
        {
            _state.SetSetting(key, "");
            if (GameId.TryParse(key["session.open.".Length..], out var game) &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
            {
                var session = new SessionInfo(start, SaveActivity.LastWrite(_games.TryGetValue(game, out var known) ? known.Folders : [], start, nowUtc));
                _state.AddSession(game, session);
                _ended[game] = session;
            }
        }
    }

    private async Task StartedAsync(SessionStarted started, CancellationToken ct)
    {
        var game = started.Game;
        _state.SetSetting(RunningGames.OpenSessionKey(game), started.StartUtc.ToString("O", CultureInfo.InvariantCulture));
        _state.SetStatus(game, GameStatus.Playing, $"Playing since {started.StartUtc.ToLocalTime():HH:mm}.");
        _activity[game] = new SaveActivity(_games.TryGetValue(game, out var known) ? known.Folders : []);
        output.Say($"{Title(game)}: playing since {started.StartUtc.ToLocalTime():HH:mm:ss}.");
        output.Played(game, Title(game), playing: true);
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
        _ended[ended.Game] = ended.Session;
        var minutes = Math.Max(1, (int)Math.Round((ended.Session.EndUtc - ended.Session.StartUtc).TotalMinutes));
        output.Say($"{Title(ended.Game)}: session over after {minutes} min{(ended.ByHand ? ", ended by hand" : "")}.");
        output.Played(ended.Game, Title(ended.Game), playing: false);
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

        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = IsRunningNow }, ct);
        var results = await service.SyncAsync(games, ct);
        await Markers.ClearAsync(engine, service, IsRunningNow, ct);
        Report(output, results);
        foreach (var game in games)
        {
            CheckMovedSaves(engine, game, sessions[game], results);
        }

        if (results.Any(r => r.Status is GameStatus.UploadPending or GameStatus.NewerInCloud))
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
            var (service, recovered) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = IsRunningNow }, ct);
            var results = recovered.Concat(await service.SyncAsync(null, ct)).ToList();
            await Markers.ClearAsync(engine, service, IsRunningNow, ct);
            Report(output, results);
            (_retryWait, _retryAt) = results.Any(r => r.Status is GameStatus.UploadPending or GameStatus.NewerInCloud)
                ? (FirstRetry, nowUtc + FirstRetry)
                : (FirstRetry, _syncAt);
        });
    }

    /// <summary>BG-04: games whose upload or download waits (offline, Drive full) try again after 1 minute, then 2, 4, up to an hour.</summary>
    private async Task RetryAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var engine = Engine.Open(dataDir);
        var waiting = engine.Games.Where(g => engine.State.GetState(g.Id).Status is GameStatus.UploadPending or GameStatus.NewerInCloud)
            .Select(g => g.Id).ToList();
        if (waiting.Count == 0)
        {
            (_retryWait, _retryAt) = (FirstRetry, nowUtc + FirstRetry);
            return;
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        await WorkAsync(async () =>
        {
            var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = IsRunningNow }, ct);
            var results = await service.SyncAsync(waiting, ct);
            Report(output, results);
            var stillWaiting = results.Any(r => r.Status is GameStatus.UploadPending or GameStatus.NewerInCloud);
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
            var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = IsRunningNow }, ct);
            await Builds.KeepAsync(engine, service, changed, output, ct);
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
    internal static void Report(IAgentOutput output, IReadOnlyList<GameResult> results)
    {
        static bool SignedOut(GameResult result) =>
            result.CloudProblem == CloudErrorKind.SignInExpired || result.Message.Contains("sign in again", StringComparison.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (result.Action is not (SyncAction.None or null) || result.Status is not (GameStatus.Synced or GameStatus.BackupOnly))
            {
                output.Say($"{result.Title}: {result.Status}. {result.Message}");
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

    private bool IsRunningNow(GameId game) => _tracker.Playing.ContainsKey(game) || _running.Any(p => p.Game == game);
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
