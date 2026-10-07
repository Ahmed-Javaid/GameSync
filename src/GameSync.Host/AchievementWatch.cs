using System.Globalization;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>An achievement unlocked while its game ran, as the popup shows it: the game, the achievement and how far the game is now.</summary>
/// <param name="BySteam">
/// Steam ran the game and keeps the unlock (KAN-122): its popup is off unless turned on for the game, as Steam shows its
/// own. An unlock from a copy's own record (KAN-123) isn't: no launcher shows a popup for it, so GameSync's is on.
/// </param>
public sealed record AchievementUnlock(GameId Game, string GameTitle, long AppId, AchievementShown Achievement, int Done, int Total, bool BySteam = true)
{
    /// <summary>This one finished the game: every one unlocked, its Zenith.</summary>
    public bool IsZenith => Total > 0 && Done >= Total;

    /// <summary>The chime and the popup's metal: <c>gold</c>, <c>silver</c>, <c>bronze</c>; null while its rarity isn't known.</summary>
    public string? Tier => Achievement.Tier switch
    {
        AchievementTier.Gold => "gold",
        AchievementTier.Silver => "silver",
        AchievementTier.Bronze => "bronze",
        _ => null,
    };
}

/// <summary>
/// ACH-09: achievements unlocked while a game runs, seen within a second (the owner, 3 Oct 2026: "as someone is playing a
/// game they need to be able to see a popup"). Steam says which game runs (<see cref="SteamRunning"/>) and rewrites that
/// game's progress file in its <c>appcache\stats</c> folder the moment the game stores an unlock; this reads that one file
/// once a second and tells what's new. A copy Steam doesn't run that keeps its own record (KAN-123) is watched the same
/// way: GameSync's own watcher says it runs, and its record is read once a second. Reading only: nothing about the game or
/// Steam is touched (the safety rules).
/// </summary>
public sealed class AchievementWatch : IDisposable
{
    private readonly string _dataDir;
    private readonly Func<long, (GameId Game, string Title)?> _gameOf;
    private readonly Func<string?> _steamRoot;
    private readonly Func<long?> _running;
    private readonly Func<(long AppId, GameId Game, string Title)?>? _runningCopy;
    private readonly Func<long, string?>? _recordOf;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private long? _app;
    private HashSet<string> _known = [];
    private string? _progressFile;
    private DateTime _progressChanged;
    private DateTime _lookedForFile;
    private bool _needsBaseline;
    private bool _ticking;
    private bool _disposed;

    /// <summary>The copy being watched, when the game running keeps its own record (KAN-123); null for Steam's.</summary>
    private (GameId Game, string Title)? _copy;

    /// <summary>When the game being watched started being watched.</summary>
    private DateTime _since;

    /// <param name="gameOf">The library's game for a Steam app ID, with its title; null for one GameSync doesn't know.</param>
    /// <param name="steamRoot">For tests: Steam's folder in place of this PC's.</param>
    /// <param name="running">For tests: the running app's ID in place of Steam's.</param>
    /// <param name="every">How often it looks; a second in the app.</param>
    /// <param name="runningCopy">
    /// KAN-123: the game running now when it's a copy Steam doesn't run, as GameSync's own watcher sees it (its Steam app ID,
    /// the game and its title); such a copy runs under another app's ID in Steam or none, so it comes first.
    /// </param>
    /// <param name="recordOf">KAN-123: the copy's own record for a Steam app ID, from the folders added in Settings.</param>
    public AchievementWatch(string dataDir, Func<long, (GameId Game, string Title)?> gameOf, Func<string?>? steamRoot = null, Func<long?>? running = null,
        TimeSpan? every = null, Func<(long AppId, GameId Game, string Title)?>? runningCopy = null, Func<long, string?>? recordOf = null)
    {
        _dataDir = dataDir;
        _gameOf = gameOf;
        _steamRoot = steamRoot ?? StoreLocations.SteamRoot;
        _running = running ?? SteamRunning.AppId;
        _runningCopy = runningCopy;
        _recordOf = recordOf;
        var period = every ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Tick(), null, period, period);
    }

    /// <summary>An achievement unlocked while its game runs; raised off the UI thread.</summary>
    public event Action<AchievementUnlock>? Unlocked;

    /// <summary>The game with this app ID stopped running (the unlocks held while it was in exclusive fullscreen go then).</summary>
    public event Action<long>? Ended;

    /// <summary>The game being watched now, if any.</summary>
    public long? Watching => _app;

    /// <summary>What it knows is read again at the next look (a game left out or counted again).</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _progressChanged = DateTime.MinValue;
        }
    }

    /// <summary>One look: which game runs, and whether its progress file changed. Public for tests.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_ticking || _disposed)
            {
                return;
            }

            _ticking = true;
        }

        try
        {
            var copy = _runningCopy?.Invoke();
            var running = copy?.AppId ?? _running();
            if (running != _app || copy?.Game != _copy?.Game)
            {
                var ended = _app;
                Start(running, copy is { } c ? (c.Game, c.Title) : null);
                if (ended is { } app)
                {
                    Ended?.Invoke(app);
                }

                return;
            }

            if (_app is { } watched)
            {
                if (_copy is not null)
                {
                    LookRecord(watched);
                }
                else if (_steamRoot() is { } root)
                {
                    Look(root, watched);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            // Steam writing the file: looked at again next second.
        }
        finally
        {
            lock (_gate)
            {
                _ticking = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }

    /// <summary>A game starts (or none runs): what it has unlocked already is the baseline, so only what's new pops up.</summary>
    private void Start(long? app, (GameId Game, string Title)? copy = null)
    {
        _app = app;
        _copy = copy;
        _known = [];
        _progressFile = null;
        _progressChanged = DateTime.MinValue;
        _lookedForFile = DateTime.MinValue;
        _needsBaseline = false;
        _since = DateTime.UtcNow;
        if (app is { } copied && copy is not null)
        {
            // KAN-123: a copy that keeps its own record; what it holds already is the baseline.
            _progressFile = _recordOf?.Invoke(copied);
            if (_progressFile is not null)
            {
                _progressChanged = File.GetLastWriteTimeUtc(_progressFile);
                _known = new HashSet<string>(CopyAchievements.Read(_progressFile)?.Keys ?? [], StringComparer.OrdinalIgnoreCase);
            }

            return;
        }

        if (app is not { } id || _steamRoot() is not { } root)
        {
            return;
        }

        _progressFile = ProgressFile(root, id);
        _progressChanged = _progressFile is null ? DateTime.MinValue : File.GetLastWriteTimeUtc(_progressFile);
        _needsBaseline = false;
        if (SteamAchievements.Read(root, id) is { } read)
        {
            _known = read.All.Where(a => a.Unlocked).Select(a => a.Id).ToHashSet();
        }
        else if (_progressFile is not null)
        {
            // Being written as the game starts: what it holds is read at the next look, as the baseline, never as new.
            _needsBaseline = true;
            _progressChanged = DateTime.MinValue;
        }
    }

    private void Look(string root, long app)
    {
        // A game played for the first time on this account has no progress file until its first unlock: looked for again
        // every few seconds.
        if (_progressFile is null || !File.Exists(_progressFile))
        {
            if (DateTime.UtcNow - _lookedForFile < TimeSpan.FromSeconds(4))
            {
                return;
            }

            _lookedForFile = DateTime.UtcNow;
            _progressFile = ProgressFile(root, app);
            if (_progressFile is null)
            {
                return;
            }
        }

        var changed = File.GetLastWriteTimeUtc(_progressFile);
        if (changed == _progressChanged)
        {
            return;
        }

        var read = SteamAchievements.Read(root, app);
        if (read is null)
        {
            // Being written: tried again next second.
            return;
        }

        _progressChanged = changed;
        var unlocked = read.All.Where(a => a.Unlocked).ToList();
        if (_needsBaseline)
        {
            _needsBaseline = false;
            _known = unlocked.Select(a => a.Id).ToHashSet();
            return;
        }

        // Only what was unlocked just now: Steam also rewrites the file when it brings a game's stats down from its
        // servers as the game starts, with what was unlocked long ago or on another PC, and those mustn't pop up.
        var now = DateTime.UtcNow;
        var fresh = unlocked.Where(a => !_known.Contains(a.Id) && a.UnlockedUtc is { } at && at > DateTime.UnixEpoch && (now - at).Duration() < Recent)
            .OrderBy(a => a.UnlockedUtc)
            .ToList();
        // Once seen unlocked, never new again while the game runs: a file read half-written, with fewer unlocked, can't
        // bring the same popup back.
        _known.UnionWith(unlocked.Select(a => a.Id));
        if (fresh.Count == 0 || _gameOf(app) is not { } game)
        {
            return;
        }

        using var cache = new AchievementCache(_dataDir);
        var rarity = cache.Rarity(app);
        foreach (var a in fresh)
        {
            var file = a.Icon ?? a.IconLocked;
            var shown = new AchievementShown(a.Id, a.Name, a.Description, a.Hidden, a.UnlockedUtc, cache.Icon(app, file), file,
                rarity.TryGetValue(a.Id, out var percent) ? percent : null);
            var done = unlocked.Count(u => u.UnlockedUtc <= a.UnlockedUtc);
            Unlocked?.Invoke(new AchievementUnlock(game.Game, game.Title, app, shown, done, read.All.Count));
        }
    }

    /// <summary>
    /// KAN-123: one look at a running copy's own record: what's newly unlocked in it pops up, named from Steam's list (kept
    /// for the game; until it is, nothing can be named, and those wait as seen). A record that keeps no times counts what's
    /// new as unlocked just now.
    /// </summary>
    private void LookRecord(long app)
    {
        // A copy that has unlocked nothing yet may have no record until its first: looked for again every few seconds.
        if (_progressFile is null || !File.Exists(_progressFile))
        {
            if (DateTime.UtcNow - _lookedForFile < TimeSpan.FromSeconds(4))
            {
                return;
            }

            _lookedForFile = DateTime.UtcNow;
            _progressFile = _recordOf?.Invoke(app);
            if (_progressFile is null)
            {
                return;
            }

            // Written since the game started: its first unlocks, which are news. One there before (its folder added in
            // Settings only now) holds what was unlocked already, the baseline.
            if (File.GetCreationTimeUtc(_progressFile) < _since - TimeSpan.FromSeconds(2))
            {
                _progressChanged = File.GetLastWriteTimeUtc(_progressFile);
                _known = new HashSet<string>(CopyAchievements.Read(_progressFile)?.Keys ?? [], StringComparer.OrdinalIgnoreCase);
                return;
            }
        }

        var changed = File.GetLastWriteTimeUtc(_progressFile);
        if (changed == _progressChanged || CopyAchievements.Read(_progressFile) is not { } record)
        {
            // Unchanged, or being written: looked at again next second.
            return;
        }

        _progressChanged = changed;
        var now = DateTime.UtcNow;
        var fresh = record.Where(r => !_known.Contains(r.Key) && (r.Value is not { } at || (now - at).Duration() < RecordRecent))
            .OrderBy(r => r.Value ?? now)
            .Select(r => r.Key)
            .ToList();
        _known.UnionWith(record.Keys);
        using var cache = new AchievementCache(_dataDir);
        if (fresh.Count == 0 || _copy is not { } game || cache.List(app) is not { } list)
        {
            return;
        }

        var rarity = cache.Rarity(app);
        var named = list.All.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var done = list.All.Count(a => record.ContainsKey(a.Id));
        foreach (var id in fresh)
        {
            if (!named.TryGetValue(id, out var a))
            {
                continue;
            }

            var file = a.Icon ?? a.IconLocked;
            var percent = rarity.TryGetValue(a.Id, out var known) ? known : list.Percents.TryGetValue(a.Id, out var listed) ? listed : (double?)null;
            var shown = new AchievementShown(a.Id, a.Name, a.Description, a.Hidden, record[id] ?? now, cache.Icon(app, file), file, percent);
            Unlocked?.Invoke(new AchievementUnlock(game.Game, game.Title, app, shown, done, list.All.Count, BySteam: false));
        }
    }

    /// <summary>How close to now an unlock's time must be to count as just unlocked.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(5);

    /// <summary>The same for a copy's own record, some of which keep the time only to the nearest thousand seconds.</summary>
    public static readonly TimeSpan RecordRecent = TimeSpan.FromMinutes(20);

    /// <summary>The progress file of the account that played the game last, as <see cref="SteamAchievements"/> reads it.</summary>
    private static string? ProgressFile(string root, long app)
    {
        var stats = Path.Combine(root, "appcache", "stats");
        return Directory.Exists(stats)
            ? new DirectoryInfo(stats).EnumerateFiles($"UserGameStats_*_{app.ToString(CultureInfo.InvariantCulture)}.bin").MaxBy(f => f.LastWriteTimeUtc)?.FullName
            : null;
    }
}
