using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// LIB-12: the library rescans by itself, a minute after GameSync starts and half a minute after a store's install records
/// or the top of a game folder change (a game installed, copied in, moved or removed), so a game copied into <c>G:\</c> is
/// watched before it's played (the owner's Sons of the Forest, 3 Oct 2026, wasn't: nothing had scanned since 30 Sep).
/// Only folder names are watched, never a game's files; a rescan only reads, and it waits while a game that syncs is
/// played (BG-08).
/// </summary>
public sealed class LibraryWatch(string dataDir, IAgentOutput output, Func<bool> busy, Action<IReadOnlyList<string>> rescanned) : IDisposable
{
    public TimeSpan AfterStart { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Long enough for a copy or an install to settle into one rescan.</summary>
    public TimeSpan AfterChange { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WhileBusy { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The rescan itself: this PC's stores and game folders folded into the library (tests give their own).</summary>
    public Func<CancellationToken, Task<IReadOnlyList<LibraryEntry>>>? Rescan { get; init; }

    /// <summary>The folders watched: each store's install records and the game folders (tests give their own).</summary>
    public Func<IReadOnlyList<string>>? Folders { get; init; }

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;
    private int _running;
    private bool _stopped;

    public void Start()
    {
        lock (_gate)
        {
            Watch();
            _timer = new Timer(_ => _ = RunAsync(), null, AfterStart, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>A rescan soon: a folder or a store's records changed, or something asked.</summary>
    public void Soon(TimeSpan? after = null)
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _timer?.Change(after ?? AfterChange, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _timer?.Dispose();
            Unwatch();
        }
    }

    private async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            // A change while a rescan runs gets one of its own after it.
            Soon();
            return;
        }

        try
        {
            if (!File.Exists(AppConfig.PathIn(dataDir)))
            {
                // Not set up yet: first run scans for itself.
                return;
            }

            if (busy())
            {
                Soon(WhileBusy);
                return;
            }

            HashSet<GameId> before;
            using (var engine = Engine.Open(dataDir))
            {
                before = engine.Library.All().Where(e => e is { Installed: true, MergedInto: null }).Select(e => e.Id).ToHashSet();
            }

            var entries = await (Rescan ?? (ct => LocalGames.RescanAsync(dataDir, output, ct)))(CancellationToken.None);
            var added = entries.Where(e => e is { Installed: true, MergedInto: null, State: not LibraryState.Ignored } && !before.Contains(e.Id))
                .Select(e => e.DisplayTitle)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var title in added)
            {
                output.Say($"{title}: found installed on this PC; GameSync watches it now.");
            }

            rescanned(added);
            lock (_gate)
            {
                // The game folders and Steam's libraries may have changed with it.
                Unwatch();
                Watch();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            output.Say($"! GameSync couldn't look for new games just now: {e.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>Where installs show: each Steam library's records, Epic's and EA's, and the top of each game folder.</summary>
    private void Watch()
    {
        if (_stopped || !File.Exists(AppConfig.PathIn(dataDir)))
        {
            return;
        }

        IReadOnlyList<string> folders;
        try
        {
            if (Folders is { } given)
            {
                folders = given();
            }
            else
            {
                using var engine = Engine.Open(dataDir);
                var gameFolders = Cli.GameFoldersOf(engine.State);
                var sources = StoreLocations.ForThisPc(gameFolders);
                var steam = sources.SteamRoot is { } root ? SteamReader.Read(root) : null;
                folders = (steam?.Libraries ?? []).Select(l => Path.Combine(l, "steamapps"))
                    .Concat(sources.EpicManifests is { } epic ? [epic] : [])
                    .Concat(sources.EaFolders)
                    .Concat(gameFolders)
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return;
        }

        foreach (var folder in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                };
                watcher.Created += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += Changed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // A drive gone since: the next rescan looks again.
            }
        }
    }

    private void Unwatch()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void Changed(object? sender, FileSystemEventArgs e)
    {
        // Steam rewrites its records as it updates games; only an install record coming or going is an install.
        if (sender is FileSystemWatcher { Path: var folder } && folder.EndsWith("steamapps", StringComparison.OrdinalIgnoreCase) &&
            !e.Name!.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Soon();
    }
}
