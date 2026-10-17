using System.Collections.Concurrent;
using GameSync.Core.Discovery;

namespace GameSync.Host;

/// <summary>
/// Design system version 54 (the owner, 8 Oct 2026: "Tracking by itself", then "find them itself"): while a copy Steam
/// doesn't run plays with no record of its own known, the files written in the usual places and its own folder are
/// watched, with no admin, as learn mode watches for saves; the first record file (<c>.ini</c>, <c>.json</c>, <c>.cfg</c>,
/// <c>.bin</c>, <c>.dat</c>) that names the game's achievements (<see cref="CopyAchievements.Recognizes"/>) is taken as its
/// record, kept for the game, and the watch ends. A copy keeping such a record writes it at its first unlock, so that unlock
/// still pops up. Since ACH-18 the game may be known by more than one Steam app ID (GameSync's, and one the copy says
/// it is), each with its list; the record is the first that names one list's achievements, and is that ID's. Reading only:
/// nothing about the game is touched, and a game with an anti-cheat is never watched (R13, as learn mode).
/// </summary>
public sealed class RecordWatch : IDisposable
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".cfg", ".bin", ".dat" };

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, byte> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<(long AppId, IReadOnlyCollection<string> Achievements)> _lists;
    private readonly IReadOnlyList<string> _leftOut;
    private readonly Action<string, long> _found;
    private readonly Timer _timer;
    private int _done;
    private int _looking;

    /// <param name="lists">The Steam app IDs the game may be known by, each with its achievements by Steam's names: what tells its record from any other file.</param>
    /// <param name="roots">Where to watch: Windows' usual folders and the copy's own folder.</param>
    /// <param name="leftOut">GameSync's own folders, never taken for a record.</param>
    /// <param name="found">Told the record's path and the app ID it's of, once, off the UI thread.</param>
    /// <param name="every">How often the files written are looked at; a second in the app.</param>
    public RecordWatch(IReadOnlyList<(long AppId, IReadOnlyCollection<string> Achievements)> lists, IEnumerable<string> roots, IEnumerable<string> leftOut,
        Action<string, long> found, TimeSpan? every = null)
    {
        _lists = lists;
        _leftOut = leftOut.Where(f => !string.IsNullOrEmpty(f)).Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)) + Path.DirectorySeparatorChar).ToList();
        _found = found;
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => Heard(e.FullPath);
                watcher.Changed += (_, e) => Heard(e.FullPath);
                watcher.Renamed += (_, e) => Heard(e.FullPath);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A folder that can't be watched is passed over.
            }
        }

        var period = every ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Look(), null, period, period);
    }

    /// <summary>Whether it's watching any folder at all.</summary>
    public bool Watching => _watchers.Count > 0 && _done == 0;

    /// <summary>The files heard written since the last look, each looked at once it's been quiet a moment. Public for tests.</summary>
    public void Look()
    {
        if (_done != 0 || Interlocked.Exchange(ref _looking, 1) == 1)
        {
            return;
        }

        try
        {
            foreach (var path in _written.Keys.ToList())
            {
                _written.TryRemove(path, out _);
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var (app, achievements) in _lists)
                {
                    if (CopyAchievements.Recognizes(path, achievements) && Interlocked.Exchange(ref _done, 1) == 0)
                    {
                        Stop();
                        _found(path, app);
                        return;
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _looking, 0);
        }
    }

    /// <summary>A file written: only a record's kinds, never in GameSync's own folders, and a few hundred at most between looks.</summary>
    public void Heard(string path)
    {
        if (_done != 0 || _written.Count > 500 || !Kinds.Contains(Path.GetExtension(path)) ||
            _leftOut.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _written.TryAdd(path, 0);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _done, 1);
        _timer.Dispose();
        Stop();
    }

    private void Stop()
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
        }
    }
}
