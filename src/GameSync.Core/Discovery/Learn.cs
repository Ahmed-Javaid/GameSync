using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GameSync.Core.Games;
using GameSync.Core.Safety;

namespace GameSync.Core.Discovery;

/// <summary>
/// A place learn mode saw a game write to while it ran (FIND-04): a folder, or one file when it sat loose in a folder
/// GameSync never takes whole (the game's own folder, a whole Windows folder).
/// </summary>
/// <param name="Path">This PC's full path.</param>
/// <param name="Portable">As every PC reads it: <c>&lt;localLow&gt;/Landfall Games/ROUNDS</c>.</param>
/// <param name="Files">How many files were written there during the session.</param>
/// <param name="Examples">A few of their names, the save-like ones first.</param>
/// <param name="Tags">What makes it likely: <see cref="LearnPlaces.SaveLikeTag"/>, <see cref="LearnPlaces.OwnFolderTag"/>, <see cref="LearnPlaces.SteamCloudTag"/>.</param>
public sealed record LearnPlace(string Path, string Portable, bool IsFile, int Files, long Bytes, DateTime NewestUtc, IReadOnlyList<string> Examples,
    IReadOnlyList<string> Tags)
{
    public bool LooksLikeSaves => Tags.Contains(LearnPlaces.SaveLikeTag);
}

/// <summary>What one session learn mode watched came to: the places, likeliest first.</summary>
/// <param name="Overflowed">Windows reported changes faster than GameSync could take them in, so a place may be missing.</param>
public sealed record LearnFinds(DateTime StartUtc, DateTime EndUtc, IReadOnlyList<LearnPlace> Places, bool Overflowed = false);

/// <summary>A folder learn mode watches, as every PC reads it, and how deep under it one game's places start.</summary>
/// <param name="GroupDepth">How many folders under it name one game: 1 (<c>&lt;roaming&gt;/Game</c>), 2 for Steam's <c>userdata/&lt;account&gt;/&lt;app&gt;</c>.</param>
/// <param name="Tag">Said of every place in it (Steam's cloud folder, the game's own folder).</param>
public sealed record LearnRoot(string Path, string Portable, int GroupDepth = 1, string? Tag = null);

/// <summary>
/// Where learn mode watches and what it leaves out: the folders saves usually live in, Steam's <c>userdata</c>, the
/// person's extra save folders and the game's own folder; never GameSync's own folders (<see cref="LeftOut"/>) or a
/// folder the safety guard blocks (R5).
/// </summary>
/// <param name="Folders">This PC's placeholders, to make each place portable; <c>&lt;installDir&gt;</c> among them for the game's own folder.</param>
public sealed record LearnScope(IReadOnlyList<LearnRoot> Roots, IReadOnlyDictionary<string, string> Folders, IReadOnlyList<string> LeftOut,
    SensitivePathGuard? Guard = null);

/// <summary>
/// FIND-04's watcher: notes every file written under the folders it's given while a game runs, with when, and nothing
/// else. It needs no admin, never opens the game, and reads nothing but where files change (R13). Some writes are
/// dropped as they come (<paramref name="skip"/>), so a long session in a busy folder stays small.
/// </summary>
public sealed class LearnRecorder : IDisposable
{
    /// <summary>The most writes it keeps; past that it says it overflowed rather than growing.</summary>
    public const int MostKept = 100_000;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTime> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, bool>? _skip;
    private int _overflows;

    public LearnRecorder(IEnumerable<string> folders, Func<string, bool>? skip = null)
    {
        _skip = skip;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += (_, e) => Record(e.FullPath);
                watcher.Changed += (_, e) => Record(e.FullPath);
                watcher.Renamed += (_, e) => Record(e.FullPath);
                watcher.Error += (_, _) => Interlocked.Increment(ref _overflows);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A folder Windows won't watch is left out; the others still are.
            }
        }
    }

    /// <summary>How many folders are being watched.</summary>
    public int Watching => _watchers.Count;

    public bool Overflowed => Volatile.Read(ref _overflows) > 0;

    /// <summary>Every file written so far, with when it was last written.</summary>
    public IReadOnlyList<(string Path, DateTime WrittenUtc)> Written => _written.Select(w => (w.Key, w.Value)).ToList();

    private void Record(string path)
    {
        if (_skip?.Invoke(path) == true)
        {
            return;
        }

        if (_written.Count >= MostKept && !_written.ContainsKey(path))
        {
            Interlocked.Increment(ref _overflows);
            return;
        }

        _written[path] = DateTime.UtcNow;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}

/// <summary>
/// FIND-04: what a watched session wrote, as places a person can pick. Files written while the game ran (and in the few
/// seconds after, as games save on quitting) are grouped by the folder under a watched root that names one game, then
/// narrowed to the deepest folder they share, so Shadlix's <c>user/savedata/1/CUSA00207/SPRJ0005</c> is offered rather
/// than <c>user</c>. A file loose in a root is offered alone. Logs, caches, crash dumps, temporary files, program files
/// (R1), GameSync's own folders and anything the guard blocks (R5) are left out. The likeliest come first: names that
/// look like saves, then the most written.
/// </summary>
public static partial class LearnPlaces
{
    public const string SaveLikeTag = "Looks like saves";
    public const string OwnFolderTag = "In its own folder";
    public const string SteamCloudTag = "Steam's cloud folder";

    /// <summary>The most places offered.</summary>
    public const int MostOffered = 8;

    /// <summary>Writes this long after the session's end still count: games save as they quit.</summary>
    public static readonly TimeSpan AfterEnd = TimeSpan.FromSeconds(30);

    /// <summary>Files that change all the time and are never saves, by their names.</summary>
    [GeneratedRegex(@"(\.(log|dmp|mdmp|hdmp|tmp|temp|etl|pf|lock|lck|crdownload|part|partial|ldb|journal|wal|shm|pid|bak~)$|~$|^~\$|(^|[._-])log([._-]\d+)?\.txt$|^(output_log|player|player-prev|crash|debug)\.(txt|log)$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseFile();

    /// <summary>Steam's own folders under an account in <c>userdata</c>: its settings, screenshots and the client's own.</summary>
    [GeneratedRegex(@"^[^/]+/(config|7|760|241100)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SteamOwn();

    /// <summary>Names that look like saves.</summary>
    [GeneratedRegex(@"(sav(e|es|ed|egame|egames)?([._\-\d]|$)|slot|profile|progress|checkpoint|autosave|quicksave|world|character|playerdata|persistent|\.(sav|sl2|ess|save|sv|savegame|dat)$)", RegexOptions.IgnoreCase)]
    private static partial Regex SaveLike();

    /// <summary>
    /// Dropped as the watcher hears it, before it's even kept: a name that's never a save, or a path inside GameSync's
    /// own folders. The rest of the noise needs the root and waits for <see cref="From"/>.
    /// </summary>
    public static bool SkipAtOnce(string path, LearnScope scope) =>
        NoiseFile().IsMatch(Path.GetFileName(path)) || ProgramFileDetector.HasBlockedExtension(path) || scope.LeftOut.Any(f => Under(path, f));

    /// <summary>The places one session wrote to, likeliest first, at most <see cref="MostOffered"/>.</summary>
    public static LearnFinds From(IEnumerable<(string Path, DateTime WrittenUtc)> written, LearnScope scope, DateTime startUtc, DateTime endUtc,
        bool overflowed = false)
    {
        var roots = scope.Roots.OrderByDescending(r => r.Path.Length).ToList();
        var groups = new Dictionary<string, (LearnRoot Root, bool IsFile, List<FileInfo> Files)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, when) in written)
        {
            if (when < startUtc - TimeSpan.FromSeconds(2) || when > endUtc + AfterEnd || SkipAtOnce(path, scope))
            {
                continue;
            }

            var root = roots.FirstOrDefault(r => Under(path, r.Path));
            if (root is null)
            {
                continue;
            }

            var rest = Path.GetRelativePath(root.Path, path).Replace(Path.DirectorySeparatorChar, '/');
            if (Discoverer.NoisePath().IsMatch(rest) || (root.Tag == SteamCloudTag && SteamOwn().IsMatch(rest)))
            {
                continue;
            }

            FileInfo file;
            try
            {
                file = new FileInfo(path);
                if (!file.Exists || scope.Guard?.IsBlockedFile(file.FullName) == true || ProgramFileDetector.IsProgramFile(file.FullName))
                {
                    continue;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            // One game's folder under the root: one level (two under "My Games" or Steam's userdata); a file loose in
            // the root, or no deeper than that level, is offered alone.
            var segments = rest.Split('/');
            var depth = root.GroupDepth + (root.Portable == "<documents>" && segments[0].Equals("My Games", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            var isFile = segments.Length <= depth;
            var key = isFile ? file.FullName : Path.Combine([root.Path, .. segments[..depth]]);
            if (!groups.TryGetValue(key, out var group))
            {
                group = (root, isFile, []);
                groups[key] = group;
            }

            group.Files.Add(file);
        }

        var places = new List<LearnPlace>();
        foreach (var (key, (root, isFile, files)) in groups)
        {
            var place = isFile ? key : Shared(files.Select(f => f.DirectoryName!), key);
            if (!isFile && scope.Guard?.CheckRoot(place) is not null)
            {
                continue;
            }

            var tags = new List<string>();
            if (files.Any(f => SaveLike().IsMatch(f.Name)))
            {
                tags.Add(SaveLikeTag);
            }

            if (root.Tag is { } rootTag)
            {
                tags.Add(rootTag);
            }

            var examples = files.OrderByDescending(f => SaveLike().IsMatch(f.Name)).ThenByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            places.Add(new LearnPlace(place, RootResolver.ToPortable(place, scope.Folders), isFile, files.Count, files.Sum(f => f.Length),
                files.Max(f => f.LastWriteTimeUtc), examples, tags));
        }

        var ordered = places.OrderByDescending(p => p.LooksLikeSaves).ThenByDescending(p => p.Files).ThenByDescending(p => p.NewestUtc)
            .ThenBy(p => p.Portable, StringComparer.OrdinalIgnoreCase).Take(MostOffered).ToList();
        return new LearnFinds(startUtc, endUtc, ordered, overflowed);
    }

    /// <summary>
    /// Folds a later session's places into an earlier one's (Watch again): a place seen again takes its newer numbers, a
    /// new one joins, and the likeliest still come first.
    /// </summary>
    public static LearnFinds Merge(LearnFinds earlier, LearnFinds later)
    {
        var byPath = earlier.Places.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var place in later.Places)
        {
            byPath[place.Path] = place;
        }

        var ordered = byPath.Values.OrderByDescending(p => p.LooksLikeSaves).ThenByDescending(p => p.Files).ThenByDescending(p => p.NewestUtc)
            .ThenBy(p => p.Portable, StringComparer.OrdinalIgnoreCase).Take(MostOffered).ToList();
        return new LearnFinds(earlier.StartUtc, later.EndUtc, ordered, earlier.Overflowed || later.Overflowed);
    }

    /// <summary>The deepest folder every one of <paramref name="folders"/> is in, never above <paramref name="top"/>.</summary>
    private static string Shared(IEnumerable<string> folders, string top)
    {
        string? shared = null;
        foreach (var folder in folders)
        {
            shared = shared is null ? folder : Common(shared, folder);
        }

        return shared is not null && Under(shared, top) ? shared : top;
    }

    private static string Common(string a, string b)
    {
        var x = a.Split(Path.DirectorySeparatorChar);
        var y = b.Split(Path.DirectorySeparatorChar);
        var n = 0;
        while (n < x.Length && n < y.Length && string.Equals(x[n], y[n], StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }

        return string.Join(Path.DirectorySeparatorChar, x[..n]);
    }

    /// <summary>The path is the folder or inside it.</summary>
    internal static bool Under(string path, string folder)
    {
        var f = Path.TrimEndingDirectorySeparator(folder);
        return path.Equals(f, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
