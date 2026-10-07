using GameSync.Core.Model;

namespace GameSync.Core.Sessions;

/// <summary>A running process that belongs to a game, and when it started.</summary>
public sealed record GameProcess(GameId Game, int ProcessId, DateTime StartedUtc);

public abstract record SessionEvent(GameId Game);

/// <summary>A game started playing; <paramref name="StartUtc"/> is already its first process's start minus the slack.</summary>
public sealed record SessionStarted(GameId Game, DateTime StartUtc) : SessionEvent(Game);

/// <param name="ByHand">Ended with "I'm done playing" rather than by the game closing (PLAY-06).</param>
public sealed record SessionEnded(GameId Game, SessionInfo Session, bool ByHand) : SessionEvent(Game);

/// <summary>
/// PLAY-04: when a game's play session starts and ends. It starts at its first process's start time minus 2 seconds,
/// since games write saves the moment they start, and ends once none of its processes has run for 10 seconds and its
/// save folders have been quiet for 5, so a launcher handing over to the game stays one session and the last save lands
/// in it. Pure: each tick says which game processes run now and when each game's save files last changed.
/// </summary>
public sealed class SessionTracker
{
    public static readonly TimeSpan StartSlack = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ProcessGrace = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(5);

    private readonly Dictionary<GameId, Open> _open = [];

    /// <summary>Processes of sessions ended by hand, ignored until they exit (PLAY-06).</summary>
    private readonly HashSet<int> _dismissed = [];

    /// <summary>The games playing now, with when each session started.</summary>
    public IReadOnlyDictionary<GameId, DateTime> Playing => _open.ToDictionary(o => o.Key, o => o.Value.StartUtc);

    /// <param name="lastWrites">When each game's save files last changed, as far as the caller has seen.</param>
    public IReadOnlyList<SessionEvent> Tick(DateTime nowUtc, IReadOnlyCollection<GameProcess> running, IReadOnlyDictionary<GameId, DateTime>? lastWrites = null)
    {
        _dismissed.IntersectWith(running.Select(p => p.ProcessId));
        var live = running.Where(p => !_dismissed.Contains(p.ProcessId)).GroupBy(p => p.Game).ToDictionary(g => g.Key, g => g.ToList());
        var events = new List<SessionEvent>();
        foreach (var (game, processes) in live)
        {
            if (_open.TryGetValue(game, out var open))
            {
                _open[game] = open with { LastSeenUtc = nowUtc };
                continue;
            }

            var start = processes.Min(p => p.StartedUtc) - StartSlack;
            _open[game] = new Open(start, nowUtc, start);
            events.Add(new SessionStarted(game, start));
        }

        foreach (var (game, open) in _open.ToList())
        {
            if (live.ContainsKey(game))
            {
                continue;
            }

            var lastWrite = lastWrites is not null && lastWrites.TryGetValue(game, out var written) && written > open.LastWriteUtc ? written : open.LastWriteUtc;
            if (nowUtc - open.LastSeenUtc >= ProcessGrace && nowUtc - lastWrite >= QuietFor)
            {
                _open.Remove(game);
                events.Add(new SessionEnded(game, new SessionInfo(open.StartUtc, Later(open.LastSeenUtc, lastWrite)), ByHand: false));
            }
            else
            {
                _open[game] = open with { LastWriteUtc = lastWrite };
            }
        }

        return events;
    }

    /// <summary>
    /// PLAY-06, "I'm done playing": ends the game's session now. Whatever of it still runs, such as a launcher left
    /// open, is ignored until it exits, so it doesn't start a new session.
    /// </summary>
    public SessionEnded? EndByHand(GameId game, DateTime nowUtc, IReadOnlyCollection<GameProcess> running)
    {
        if (!_open.Remove(game, out var open))
        {
            return null;
        }

        _dismissed.UnionWith(running.Where(p => p.Game == game).Select(p => p.ProcessId));
        return new SessionEnded(game, new SessionInfo(open.StartUtc, Later(nowUtc, open.LastWriteUtc)), ByHand: true);
    }

    /// <summary>A process of a session ended by hand, still running (an emulator still closing): it no longer counts as playing.</summary>
    public bool Ignores(int processId) => _dismissed.Contains(processId);

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private sealed record Open(DateTime StartUtc, DateTime LastSeenUtc, DateTime LastWriteUtc);
}
