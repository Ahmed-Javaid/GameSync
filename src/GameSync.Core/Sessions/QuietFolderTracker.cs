using GameSync.Core.Model;

namespace GameSync.Core.Sessions;

/// <summary>
/// LIB-13: when a folder of the person's own that has no program to watch, such as a game server's world, is in use. Its
/// session starts at the first change seen in it and ends once it has been quiet for 5 minutes, so the world syncs
/// between its autosaves and after the server stops, and its changes count as made in a session. Pure: each tick says
/// when each folder last changed.
/// </summary>
public sealed class QuietFolderTracker
{
    public static readonly TimeSpan QuietFor = TimeSpan.FromMinutes(5);

    private readonly Dictionary<GameId, Open> _open = [];

    /// <summary>When each folder's last session ended: a change seen by then already belongs to it.</summary>
    private readonly Dictionary<GameId, DateTime> _ended = [];

    /// <summary>The folders changing now, with when each one's session started.</summary>
    public IReadOnlyDictionary<GameId, DateTime> Active => _open.ToDictionary(o => o.Key, o => o.Value.StartUtc);

    /// <param name="lastWrites">When each folder last changed, as far as the caller has seen.</param>
    public IReadOnlyList<SessionEvent> Tick(DateTime nowUtc, IReadOnlyDictionary<GameId, DateTime> lastWrites)
    {
        var events = new List<SessionEvent>();
        foreach (var (game, written) in lastWrites)
        {
            if (_open.TryGetValue(game, out var open))
            {
                if (written > open.LastWriteUtc)
                {
                    _open[game] = open with { LastWriteUtc = written };
                }
            }
            else if (written > _ended.GetValueOrDefault(game) && nowUtc - written < QuietFor)
            {
                // A change since the folder's last session: a new one, from just before the change.
                var start = written - SessionTracker.StartSlack;
                _open[game] = new Open(start, written);
                events.Add(new SessionStarted(game, start));
            }
        }

        foreach (var (game, open) in _open.ToList())
        {
            if (nowUtc - open.LastWriteUtc >= QuietFor)
            {
                _open.Remove(game);
                _ended[game] = open.LastWriteUtc;
                events.Add(new SessionEnded(game, new SessionInfo(open.StartUtc, open.LastWriteUtc), ByHand: false));
            }
        }

        return events;
    }

    /// <summary>
    /// Stopping (sign-out, shutdown): each folder still changing has its session end at its last change, so what changed
    /// in it counts as made in a session when GameSync next syncs it.
    /// </summary>
    public IReadOnlyList<SessionEnded> EndAll()
    {
        var ended = _open.Select(o => new SessionEnded(o.Key, new SessionInfo(o.Value.StartUtc, o.Value.LastWriteUtc), ByHand: false)).ToList();
        foreach (var (game, open) in _open)
        {
            _ended[game] = open.LastWriteUtc;
        }

        _open.Clear();
        return ended;
    }

    private sealed record Open(DateTime StartUtc, DateTime LastWriteUtc);
}
