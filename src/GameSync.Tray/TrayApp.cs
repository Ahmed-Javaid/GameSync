using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.Branding;
using GameSync.UI.Controls;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;
using GameSync.UI.Views;
using GameSync.Windows;

namespace GameSync.Tray;

/// <summary>
/// GameSync's app once it runs: the tray icon (BG-07), the window while it's open, the agent inside (design.md →
/// Background work), the pipe a second start talks to (BG-01), and the theme, from the person's choice and Windows
/// (LOOK-01, LOOK-04, LOOK-07). All of it lives on the UI thread; the agent's news arrives on its own thread and is
/// passed over.
/// </summary>
internal sealed class TrayApp
{
    private static readonly TimeSpan ArtAfterStart = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopWithin = TimeSpan.FromSeconds(10);

    private readonly string _dataDir;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly CancellationTokenSource _stop = new();
    private readonly AppOutput _output;
    private readonly AppAgent _agent;
    private readonly TrayIcon _tray;
    private readonly ConsoleViewModel _console = new();
    private readonly DispatcherTimer _refresh;
    private readonly Dictionary<GameId, string> _playing = [];
    private readonly Dictionary<(int, TrayBadge, bool), byte[]> _icons = [];
    private Task _agentRun = Task.CompletedTask;
    private MainWindow? _window;
    private ShellViewModel? _shell;
    private Pages? _pages;
    private string _page = "home";
    private Look _look = new();
    private IReadOnlyDictionary<string, string> _tokens = new Dictionary<string, string>();
    private bool _dark = true;
    private bool _setUp;
    private SyncCounts _counts = SyncCounts.None;
    private bool _working;
    private CloudErrorKind? _cloud;
    private TrayStatus _status = new(TrayMood.Synced, "GameSync");
    private DateTime _letGoneAt = DateTime.MinValue;
    private bool _stopped;

    private TrayApp(AppStart start, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _dataDir = start.DataDir;
        _lifetime = lifetime;
        _setUp = File.Exists(AppConfig.PathIn(_dataDir));
        _output = new AppOutput(_dataDir, Toasts.Show);
        _agent = new AppAgent(_dataDir, _output);
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Refresh());

        _tray = new TrayIcon();
        _tray.Opened += Bring;
        _tray.Picked += Picked;
        _tray.LookChanged += DrawTray;
        _tray.Failed += e => _output.Say($"! The tray icon: {e.Message}");
        _tray.SessionEnding += () => Stop(TimeSpan.FromSeconds(4));
        _tray.Menu = () =>
        [
            new TrayMenuItem("Open GameSync", "open", IsDefault: true),
            new TrayMenuItem("Sync now", "sync", Enabled: _agent.IsRunning),
            TrayMenuItem.Separator,
            new TrayMenuItem("Quit GameSync", "quit"),
        ];

        _output.Line += line => Dispatcher.UIThread.Post(() => _console.Add(DateTime.Now, line));
        _output.WorkingChanged += busy => Dispatcher.UIThread.Post(() =>
        {
            _working = busy;
            ShowStatus();
            if (!busy)
            {
                LetGoSoon();
            }
        });
        _output.SyncFinished += results => Dispatcher.UIThread.Post(() =>
        {
            _cloud = TrayStatus.CloudTrouble(results);
            RefreshSoon();
        });
        _output.PlayChanged += (game, title, playing) => Dispatcher.UIThread.Post(() =>
        {
            if (playing)
            {
                _playing[game] = title;
            }
            else
            {
                _playing.Remove(game);
            }

            RefreshSoon();
        });

        _lifetime.Exit += (_, _) => Stop(TimeSpan.FromSeconds(4));
    }

    public static void Start(AppStart start, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var app = new TrayApp(start, lifetime);
        app.Begin(start.ShowWindow);
    }

    private void Begin(bool showWindow)
    {
        FollowTheme();
        ShowStatus();
        _agentRun = Task.Run(() => _agent.RunAsync(_stop.Token));
        _ = Task.Run(() => AppPipe.ServeAsync(_dataDir, Answer, e => _output.Say($"! A second start can't reach this app: {e.Message}"), _stop.Token));
        _ = Task.Run(ReadTodaysLogAsync);
        _ = Task.Run(RefreshArtAsync);
        RefreshSoon();
        if (showWindow)
        {
            Bring();
        }
    }

    /// <summary>Opens the window, or brings it forward.</summary>
    private void Bring()
    {
        if (_stopped)
        {
            return;
        }

        if (_window is null)
        {
            _shell = new ShellViewModel(MakePage, _page) { Rail = _pages?.Rail ?? ShellViewModel.DefaultRail(null, null) };
            var window = new MainWindow { DataContext = _shell };
            window.Opened += (_, _) => window.PaintFrame(_tokens, _dark);
            window.Closed += (_, _) =>
            {
                _page = _shell?.Current ?? _page;
                _window = null;
                _shell = null;
                _pages = null;
                LetGoSoon(force: true);
            };
            _window = window;
            RefreshSoon();
        }

        _window.Bring();
    }

    private object? MakePage(string id) => id switch
    {
        "home" when !_setUp => new PlaceholderViewModel("Welcome to GameSync", "logo",
            "GameSync isn't set up on this PC yet. First run, which does it in four steps, is on its way; until then, set it up from the command line with gamesync init."),
        "home" => _pages?.Home,
        "library" => _pages?.Library,
        "log" => _console,
        "saves" => new PlaceholderViewModel("Save manager", "saves",
            "Every game's saves in one table, with Plan, Versions and the full log, and sharing saves with friends. It's the next screen being built."),
        "settings" => new PlaceholderViewModel("Settings", "settings",
            "Appearance, folders, the daily backup, the cloud, your PCs and notifications. They come after the save manager; until then, the command line has them."),
        _ => null,
    };

    private void Picked(string id)
    {
        switch (id)
        {
            case "open":
                Bring();
                break;
            case "sync":
                SyncNow();
                break;
            case "quit":
                Quit();
                break;
        }
    }

    /// <summary>What the launcher's pages ask of the app: Play, Sync now, another page, hiding a game.</summary>
    private LauncherActions Actions => new(Play, SyncNow, Show, SetHidden);

    private void Play(GameId game) => _ = Task.Run(() => AppActions.PlayAsync(_dataDir, game, _output));

    private void SyncNow()
    {
        if (!_agent.IsRunning)
        {
            _output.Say("! Sync now waits: GameSync's agent isn't running in this app yet.");
            return;
        }

        _agent.SyncNow();
        _output.Say(_playing.Count > 0 ? "Sync now: every game syncs once you stop playing." : "Sync now: every game syncs in a moment.");
    }

    /// <summary>Opens a page, and the library on a view when asked: Home's My games and Needs you tabs are the library's views.</summary>
    private void Show(string page, string? tab)
    {
        if (page == "library" && tab is not null && _pages is not null)
        {
            _pages.Library.SelectedTab = tab;
        }

        _shell?.Open(page);
    }

    /// <summary>
    /// What comes over the pipe: a second start (<c>show</c>), an installer closing GameSync for an update (<c>quit</c>),
    /// or the tray icon's state and hover text (<c>status</c>).
    /// </summary>
    private async Task<string> Answer(string message) => message switch
    {
        "show" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bring();
            return "ok";
        }),
        "status" => await Dispatcher.UIThread.InvokeAsync(() =>
            $"{_status.Mood}{(_tray.IsShown ? "" : " (not in the notification area yet)")}: {_status.Tooltip.Replace('\n', ';')}"),
        "quit" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DispatcherTimer.RunOnce(Quit, TimeSpan.FromMilliseconds(250));
            return "ok";
        }),
        "ping" => "ok",
        _ => "unknown",
    };

    private void RefreshSoon()
    {
        _refresh.Stop();
        _refresh.Start();
    }

    /// <summary>Reads how the games are doing, off the UI thread, then updates the tray icon and, when the window is open, its rail and page.</summary>
    private async void Refresh()
    {
        _refresh.Stop();
        if (_stopped)
        {
            return;
        }

        try
        {
            _setUp = File.Exists(AppConfig.PathIn(_dataDir));
            var withPages = _window is not null && _setUp;
            var (actions, tab) = (Actions, _pages?.Library.SelectedTab ?? "all");
            var (counts, pages) = await Task.Run(() => (SyncCounts.Read(_dataDir), withPages ? Pages.Read(_dataDir, actions, tab) : null));
            _counts = counts;
            ShowStatus();
            if (_shell is not null && pages is not null)
            {
                _pages = pages;
                _shell.Rail = pages.Rail;
                _shell.Reload();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _console.Add(DateTime.Now, $"! GameSync couldn't read this PC's games: {e.Message}");
        }
    }

    private void ShowStatus()
    {
        _status = TrayStatus.From(_setUp, _counts, _working, _cloud, _playing.Values.FirstOrDefault());
        DrawTray();
    }

    private void DrawTray()
    {
        if (_stopped)
        {
            return;
        }

        var badge = _status.Mood switch
        {
            TrayMood.Working => TrayBadge.Working,
            TrayMood.NeedsYou => TrayBadge.NeedsYou,
            TrayMood.Offline => TrayBadge.Offline,
            TrayMood.Playing => TrayBadge.Playing,
            _ => TrayBadge.None,
        };
        var size = _tray.IconSize;
        var light = TrayIcon.TaskbarIsLight();
        if (!_icons.TryGetValue((size, badge, light), out var pixels))
        {
            pixels = MarkArt.Tray(size, badge, light);
            _icons[(size, badge, light)] = pixels;
        }

        try
        {
            _tray.Set(size, pixels, _status.Tooltip);
        }
        catch (InvalidOperationException e)
        {
            _output.Say($"! The tray icon: {e.Message}");
        }
    }

    private void SetHidden(GameId game, bool hidden)
    {
        LauncherData.SetHidden(_dataDir, game, hidden);
        Dispatcher.UIThread.Post(RefreshSoon);
    }

    /// <summary>The person's look on this PC, applied now and again whenever Windows' mode or accent changes (LOOK-01, LOOK-04).</summary>
    private void FollowTheme()
    {
        try
        {
            using var state = new StateStore(_dataDir);
            _look = Look.Load(state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _output.Say($"! Your appearance choices couldn't be read, so GameSync uses its default look: {e.Message}");
        }

        ApplyTheme();
        if (Application.Current?.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(ApplyTheme);
        }
    }

    private void ApplyTheme()
    {
        var app = Application.Current!;
        var colors = app.PlatformSettings?.GetColorValues();
        var accent = colors?.AccentColor1 is { } a ? $"#{a.R:x2}{a.G:x2}{a.B:x2}" : null;
        var choice = _look.Resolve(colors?.ThemeVariant == PlatformThemeVariant.Light, accent);
        _tokens = ThemeService.Apply(app, choice);
        _dark = choice.Mode == ThemeMode.Dark;
        _window?.PaintFrame(_tokens, _dark);
    }

    /// <summary>Today's log so far, so the Console page starts with what the agent already did.</summary>
    private async Task ReadTodaysLogAsync()
    {
        var today = DateTime.Now.Date;
        var file = Path.Combine(_dataDir, "logs", $"agent-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
        try
        {
            if (File.Exists(file))
            {
                var lines = (await File.ReadAllLinesAsync(file, _stop.Token)).TakeLast(ConsoleViewModel.Keep).ToList();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var live = _console.Lines.ToList();
                    _console.Lines.Clear();
                    _console.AddFile(lines, today);
                    foreach (var line in live)
                    {
                        _console.Lines.Add(line);
                    }
                });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    /// <summary>ART-07: a while after the start, art for new games, and art that's due a check, from Steam.</summary>
    private async Task RefreshArtAsync()
    {
        try
        {
            await Task.Delay(ArtAfterStart, _stop.Token);
            if (!File.Exists(AppConfig.PathIn(_dataDir)))
            {
                return;
            }

            var (_, refresh) = await LauncherData.FetchArtAsync(_dataDir, _stop.Token);
            if (refresh.Copied + refresh.Downloaded > 0)
            {
                _output.Say($"Art: {refresh.Copied + refresh.Downloaded} new pictures for the launcher.");
                Dispatcher.UIThread.Post(RefreshSoon);
            }

            Dispatcher.UIThread.Post(() => LetGoSoon());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _output.Say($"! Art from Steam didn't come this time: {e.Message}");
        }
    }

    /// <summary>
    /// PERF-01: GameSync waits in the tray small. When the window closes, and after work in the background while it's
    /// closed (at most once a minute), what that used is let go at once rather than whenever .NET gets round to it,
    /// which in an app this quiet can be hours.
    /// </summary>
    private void LetGoSoon(bool force = false)
    {
        if (!force && (_window is not null || DateTime.UtcNow - _letGoneAt < TimeSpan.FromMinutes(1)))
        {
            return;
        }

        _letGoneAt = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        });
    }

    /// <summary>Quit from the tray: the window closes, the agent records any open session and stops, and the tray icon goes.</summary>
    private void Quit()
    {
        _window?.Close();
        Stop(StopWithin);
        _lifetime.Shutdown();
    }

    private void Stop(TimeSpan within)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _stop.Cancel();
        try
        {
            _agentRun.Wait(within);
        }
        catch (AggregateException)
        {
            // The agent's own log has what went wrong.
        }

        _refresh.Stop();
        _tray.Dispose();
    }

    /// <summary>What the launcher's pages show, read off the UI thread: the games, the home screen, and their pictures at the size they're shown.</summary>
    private sealed record Pages(HomeViewModel Home, LibraryViewModel Library, IReadOnlyList<RailItem> Rail)
    {
        /// <param name="libraryTab">The library's view, kept when the pages are read again.</param>
        public static Pages Read(string dataDir, LauncherActions actions, string libraryTab)
        {
            var now = DateTime.Now;
            var (games, home) = LauncherData.Read(dataDir, now);
            return new Pages(
                HomeViewModel.From(home, games, now, actions),
                LibraryViewModel.From(games, now, libraryTab, actions),
                ShellViewModel.DefaultRail(games.FirstOrDefault(g => g.Status == GameStatus.Playing)?.Title, games.Count(g => g.NeedsYou)));
        }
    }
}
