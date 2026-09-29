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
    private LibraryViewModel? _library;
    private SaveManagerViewModel? _saves;
    private FirstRunViewModel? _firstRun;
    private string _page = "home";
    private readonly Backdrops _backdrops;
    private Look _look = new();
    private ThemeChoice _choice = new();
    private IReadOnlyDictionary<string, string> _tokens = new Dictionary<string, string>();
    private bool _dark = true;
    private bool _transparency = true;
    private string? _backdropArt;
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
        _backdrops = new Backdrops(e => _output.Say($"! Glossy's backdrop couldn't be made, so the window stays Solid: {e.Message}"));
        _backdrops.Made += ShowSurface;

        _tray = new TrayIcon();
        _tray.Opened += Bring;
        _tray.Picked += Picked;
        _tray.LookChanged += () =>
        {
            DrawTray();
            FollowTransparency();
        };
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
        _ = Task.Run(() => RefreshArtAsync());
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
            var shell = new ShellViewModel(MakePage, _page) { Rail = _pages?.Rail ?? ShellViewModel.DefaultRail(null, null) };
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShellViewModel.Strength) or nameof(ShellViewModel.BackdropArt))
                {
                    ShowSurface();
                }
            };
            var window = new MainWindow { DataContext = shell };
            window.Opened += (_, _) => window.PaintFrame(_tokens, _dark);
            window.Closed += (_, _) =>
            {
                _page = _shell?.Current ?? _page;
                _window = null;
                _shell = null;
                _pages = null;
                _library = null;
                _saves = null;
                LetGoSoon(force: true);
            };
            (_window, _shell) = (window, shell);
            ShowSurface();
            RefreshSoon();
        }

        _window.Bring();
    }

    private object? MakePage(string id) => id switch
    {
        // Until GameSync is set up, the window is first run, whatever page it was left on (ONB-01).
        _ when !_setUp => _firstRun ??= new FirstRunViewModel(SetupActions),
        "home" => _pages?.Home,
        "library" => Library,
        "log" => _console,
        "saves" => Saves,
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

    /// <summary>
    /// What the pages ask of the app: Play, Sync now, another page, hiding a game, favourites, the order, a game's page,
    /// its saves in the save manager, its Properties, and the save jobs.
    /// </summary>
    private LauncherActions Actions => new(Play, SyncNow, Show, SetHidden)
    {
        SetFavourite = SetFavourite,
        SetSort = sort => Task.Run(() => LauncherData.SetSort(_dataDir, sort)),
        SetView = view => Task.Run(() => LauncherData.SetView(_dataDir, view)),
        ScanFolder = async (folder, ct) =>
        {
            try
            {
                var scan = await Task.Run(() => LocalGames.ScanFolderAsync(_dataDir, folder, _output, _stop.Token), ct);
                _output.Say(scan.Sentence);
                _agent.WatchNow();
                _ = Task.Run(() => RefreshArtAsync(now: true));
                return scan;
            }
            finally
            {
                Dispatcher.UIThread.Post(RefreshSoon);
            }
        },
        Locate = (game, program) => Job(async ct =>
        {
            try
            {
                _output.Say(await LocalGames.LocateAsync(_dataDir, game, program, _output, ct));
                _agent.WatchNow();
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _output.NeedsYou(TitleOf(game), e.Message);
            }
        }),
        OpenAddGame = () => ShowDialog(new AddOwnViewModel(OwnActionsFor(setup: false))),
        LoadSpace = ct => Task.Run(() => SaveOverview.Space(_dataDir), ct),
        ConnectCloud = ConnectCloud,
        OpenGame = OpenGame,
        LoadGame = (game, ct) => Task.Run(() => GameDetails.ReadAsync(_dataDir, game, ct, SteamIdOf(game)), ct),
        OpenFolder = OpenFolder,
        OpenSaves = OpenSaves,
        OpenProperties = OpenProperties,
        OpenLink = OpenLink,
        CloseDialog = () =>
        {
            if (_shell is not null)
            {
                _shell.Dialog = null;
            }
        },
        LoadSaves = ct => Task.Run(() => SaveOverview.ReadAsync(_dataDir, ct), ct),
        LoadProperties = (game, ct) => Task.Run(() => GameSettings.ReadAsync(_dataDir, game, SteamIdOf(game), ct), ct),
        SaveProperties = (game, change) => Job(async ct =>
        {
            try
            {
                _output.Say(await GameSettings.ApplyAsync(_dataDir, game, change, () => _output.Say("Waiting for the sync in the background to finish first."), ct));
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _output.NeedsYou("GameSync", e.Message);
            }
        }),
        Approve = game => Job(ct => AppActions.ApproveAsync(_dataDir, game, _output, ct)),
        OpenConflict = OpenConflict,
        LoadConflict = (game, ct) => Task.Run(() => ConflictDetails.ReadAsync(_dataDir, game, ct), ct),
        Resolve = (game, keepThisPc, cloudVersion) => Job(ct => AppActions.ResolveAsync(_dataDir, game, keepThisPc, cloudVersion, _output, ct)),
        Swap = game => Job(ct => AppActions.SwapAsync(_dataDir, game, _output, ct)),
        Retry = game => Job(ct => AppActions.RetryAsync(_dataDir, game, _output, ct)),
        RenameSave = (game, name, newName) => Job(ct => AppActions.RenameSaveAsync(_dataDir, game, name, newName, _output, ct)),
        ForgetSave = (game, name) => Job(ct => AppActions.ForgetSaveAsync(_dataDir, game, name, _output, ct)),
        OpenAddPlace = game => ShowDialog(new AddPlaceViewModel(game, TitleOf(game), Actions)),
        LookPlace = (game, path, ct) => Task.Run(() => SavePlaces.Look(_dataDir, game, path), ct),
        AddPlace = (game, path, category) => Job(async ct =>
        {
            try
            {
                _output.Say(await GameSettings.ApplyAsync(_dataDir, game, new GamePropertiesChange { AddPlace = path, AddPlaceCategory = category },
                    () => _output.Say("Waiting for the sync in the background to finish first."), ct));
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _output.NeedsYou("GameSync", e.Message);
            }
        }),
        OpenImportKept = game =>
        {
            var import = new ImportKeptViewModel(game, TitleOf(game), Actions);
            ShowDialog(import);
            import.Load();
        },
        SaveRoots = (game, ct) => Task.Run(() => SavePlaces.Roots(_dataDir, game), ct),
        KeptSaves = async (game, folder, root, apply, ct) =>
        {
            var report = await Task.Run(() => AppActions.KeptSavesAsync(_dataDir, game, folder, root, apply, _output, ct), ct);
            if (apply)
            {
                Dispatcher.UIThread.Post(RefreshSoon);
            }

            return report;
        },
        SyncGame = game => Job(async ct =>
        {
            if (await AppActions.SyncGameAsync(_dataDir, game, _output, ct))
            {
                _agent.WatchNow();
                _agent.SyncNow();
            }
        }),
        BackUpNow = game => Job(ct => AppActions.BackUpNowAsync(_dataDir, game, _output, ct)),
        SaveAs = (game, name) => Job(ct => AppActions.SaveAsAsync(_dataDir, game, name, _output, ct)),
        Restore = (game, version, name) => Job(ct => AppActions.RestoreAsync(_dataDir, game, version, name, _output, ct)),
    };

    /// <summary>What first run asks of the app (ONB-01): the scan, what it found, a game folder, the cloud, and finishing.</summary>
    private SetupActions SetupActions => new(
        Scan: (progress, ct) => Task.Run(async () =>
        {
            var scan = await FirstRun.ScanAsync(_dataDir, progress, ct);
            TryCopyArt();
            AskSteamMeanwhile();
            return scan;
        }, ct),
        Groups: ct => Task.Run(() => FirstRun.Groups(_dataDir), ct),
        AddGameFolder: (folder, ct) => Task.Run(() => FirstRun.AddGameFolder(_dataDir, folder), ct),
        Cloud: CloudActions,
        Finish: FinishSetupAsync)
    {
        AddGame = () => ShowDialog(new AddOwnViewModel(OwnActionsFor(setup: true), setup: true)),
    };

    /// <summary>
    /// Add a game or folder (LIB-13), the dialog's work. From the library the game syncs at once, the agent watches its
    /// folder or program, and its page opens; over first run it joins Choose games, ticked.
    /// </summary>
    private OwnActions OwnActionsFor(bool setup) => new(
        Look: (path, ct) => Task.Run(() => OwnGames.Look(_dataDir, path), ct),
        CheckProgram: (path, ct) => Task.Run(() => OwnGames.ProgramFolder(_dataDir, path), ct),
        Add: async (game, ct) =>
        {
            var added = await Task.Run(() => OwnGames.AddAsync(_dataDir, game, _output, ct), ct);
            _output.Say(added.Sentence);
            if (setup)
            {
                if (_firstRun is { } firstRun)
                {
                    await firstRun.RegroupAsync(added.Id);
                }
            }
            else
            {
                _agent.WatchNow();
                _agent.SyncNow();
                await RefreshAsync();
                OpenGame(added.Id);
            }

            return added;
        },
        Close: () =>
        {
            if (_shell is not null)
            {
                _shell.Dialog = null;
            }
        })
    {
        OpenFolder = OpenFolder,
    };

    /// <summary>The cloud's two ways, in first run and in Connect the cloud: Google Drive (with GameSync's client) or a folder.</summary>
    private CloudActions CloudActions => new(
        CanSignIn: FirstRun.GoogleClientFile(_dataDir) is not null,
        SignIn: ct => Task.Run(() => FirstRun.SignInAsync(_dataDir, OpenSignIn, ct), ct),
        Folder: picked => FirstRun.CloudFolder(_dataDir, picked))
    {
        SignedIn = SignedIn(),
    };

    private bool SignedIn()
    {
        try
        {
            return FirstRun.SignedIn(_dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>First run's covers, from what the Steam client already has here; no network, and no covers is no problem.</summary>
    private void TryCopyArt()
    {
        try
        {
            LauncherData.CopyArtFromSteam(_dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _output.Say($"! Covers from Steam didn't come this time: {e.Message}");
        }
    }

    /// <summary>
    /// While the person chooses games after first run's scan: Steam's store is asked about the games found, the same
    /// once-per-game question the art refresh asks after setup (ART-09), so Home has their art, and knows which are
    /// software (Wallpaper Engine) rather than games, as soon as setup is done.
    /// </summary>
    private void AskSteamMeanwhile()
    {
        // One at a time: a second scan (a game folder added) leaves it to the art refresh after setup.
        if (!_askingSteam.IsCompleted)
        {
            return;
        }

        _askingSteam = Task.Run(async () =>
        {
            try
            {
                await LauncherData.FetchArtAsync(_dataDir, _stop.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _output.Say($"! Art from Steam didn't come this time: {e.Message}");
            }
        });
    }

    private Task _askingSteam = Task.CompletedTask;

    /// <summary>
    /// Start using GameSync: the choices are saved (the cloud, the games, start at sign-in and the daily backup), then the
    /// window turns into the launcher, the agent starts, and the first backup and art run in the background.
    /// </summary>
    private async Task<SetupResult> FinishSetupAsync(SetupChoice choice, CancellationToken ct)
    {
        var result = await Task.Run(() => FirstRun.FinishAsync(_dataDir, choice, new FirstRun.WindowsSchedule(_dataDir), ct), ct);
        foreach (var note in result.Notes)
        {
            _output.Say(note);
        }

        _output.Say($"GameSync is set up: {result.Syncing} {(result.Syncing == 1 ? "game syncs" : "games sync")} and {result.BackedUp} {(result.BackedUp == 1 ? "is" : "are")} backed up.");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _setUp = true;
            _firstRun = null;
            _agent.Kick();
            _page = "home";
            _shell?.Open("home");
            Refresh();
        });
        // After what first run asked Steam meanwhile, which has usually finished by now.
        _ = Task.Run(async () =>
        {
            await _askingSteam;
            await RefreshArtAsync(now: true);
        });
        return result;
    }

    /// <summary>Connect the cloud, from Home after first run skipped it: the choice is connected at once, and the waiting versions go up.</summary>
    private void ConnectCloud() => ShowDialog(new ConnectCloudViewModel(CloudActions with { SignedIn = false }, async (remote, ct) =>
    {
        await Task.Run(() => FirstRun.ConnectAsync(_dataDir, remote, ct), ct);
        _output.Say(remote == CloudSetupViewModel.Drive ? "Google Drive is connected; what waited on this PC goes up now." : $"The cloud is {remote}; what waited on this PC goes up now.");
        _agent.SyncNow();
        Dispatcher.UIThread.Post(RefreshSoon);
    }, () =>
    {
        if (_shell is not null)
        {
            _shell.Dialog = null;
        }
    }));

    /// <summary>Google's sign-in page, in the person's browser (CLOUD-02).</summary>
    private void OpenSignIn(Uri url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            _output.Say($"! The browser didn't open for the sign-in: {e.Message}");
        }
    }

    /// <summary>A job a page asked for, off the UI thread; the pages show what it changed once it's done.</summary>
    private void Job(Func<CancellationToken, Task> job) => _ = Task.Run(async () =>
    {
        try
        {
            await job(_stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Dispatcher.UIThread.Post(RefreshSoon);
        }
    });

    /// <summary>A game's save folder in Explorer, from its page.</summary>
    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _output.NeedsYou("GameSync", $"{folder} isn't there on this PC right now.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            _output.Say($"! Explorer didn't open {folder}: {e.Message}");
        }
    }

    /// <summary>
    /// The library, for as long as the window is open: its search, order, view and open game stay when the games
    /// refresh (LIB-14 to LIB-18). Its order is the one kept on this PC.
    /// </summary>
    private LibraryViewModel Library => _library ??= NewLibrary();

    private LibraryViewModel NewLibrary()
    {
        var library = new LibraryViewModel(Actions, ReadSort(), view: ReadView());
        if (_pages is { } pages)
        {
            library.Update(pages.Games, pages.Tiles);
        }

        return library;
    }

    private LibrarySort ReadSort()
    {
        try
        {
            return LauncherData.ReadSort(_dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return LibrarySort.RecentlyPlayed;
        }
    }

    /// <summary>The library's view kept on this PC (LIB-22): All games until the person picks another.</summary>
    private string ReadView()
    {
        try
        {
            return LauncherData.ReadView(_dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return "all";
        }
    }

    /// <summary>A game's page in the library, from a cover on Home or a Needs you row (LIB-18); Back returns to that page.</summary>
    private void OpenGame(GameId game)
    {
        Library.Open(game, _shell?.Current is { } from && from != "library" ? from : null);
        _shell?.Open("library");
    }

    /// <summary>
    /// The save manager, for as long as the window is open: the game open in it stays when the games refresh (MGR-07).
    /// It shows the agent's log as it happens.
    /// </summary>
    private SaveManagerViewModel Saves => _saves ??= NewSaves();

    private SaveManagerViewModel NewSaves()
    {
        var saves = new SaveManagerViewModel(Actions, _console.Lines);
        saves.Update(_pages?.Games ?? []);
        return saves;
    }

    /// <summary>A game's saves in the save manager, from its page's Open in Saves or its status's action; Back returns to its page.</summary>
    private void OpenSaves(GameId game)
    {
        Saves.Open(game, _shell?.Current == "library" ? "library" : null);
        _shell?.Open("saves");
    }

    /// <summary>
    /// A game's conflict in the save manager, from Resolve anywhere (SYNC-10). Opened over the game's saves, Back returns
    /// to them; from Home or a game's page, to that page.
    /// </summary>
    private void OpenConflict(GameId game)
    {
        var from = _shell?.Current;
        Saves.OpenConflict(game, from is null or "saves" ? null : from);
        _shell?.Open("saves");
    }

    /// <summary>A game's Properties over the page (LIB-20, FIND-12), read after it opens.</summary>
    private void OpenProperties(GameId game, string? section)
    {
        var properties = new PropertiesViewModel(game, TitleOf(game), Actions, section);
        ShowDialog(properties);
        properties.Load();
    }

    /// <summary>A dialog over the page: Properties, Add a place, Import kept saves.</summary>
    private void ShowDialog(object dialog)
    {
        if (_shell is not null)
        {
            _shell.Dialog = dialog;
        }
    }

    private string TitleOf(GameId game) => _pages?.Games.FirstOrDefault(g => g.Id == game)?.Title ?? game.Value;

    /// <summary>A link GameSync made (a game's Steam store page, or Steam's own install and library links), in the app Windows keeps for it.</summary>
    private void OpenLink(string link)
    {
        if (!link.StartsWith("https://store.steampowered.com/app/", StringComparison.Ordinal) && !link.StartsWith("steam://", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            _output.Say($"! {link} didn't open: {e.Message}");
        }
    }

    /// <summary>The game's Steam app ID as the launcher knows it, for its page's About.</summary>
    private long? SteamIdOf(GameId game) => _pages?.Games.FirstOrDefault(g => g.Id == game)?.SteamAppId;

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
        if (page == "library" && tab is not null)
        {
            Library.ShowView(tab);
            Library.Selected = null;
        }

        _shell?.Open(page);
    }

    /// <summary>
    /// What comes over the pipe: a second start (<c>show</c>), an installer closing GameSync for an update (<c>quit</c>),
    /// the tray icon's state and hover text (<c>status</c>), or a changed look from <c>gamesync set surface</c> (<c>look</c>).
    /// </summary>
    private async Task<string> Answer(string message) => message switch
    {
        "show" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bring();
            return "ok";
        }),
        "look" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LoadLook();
            ApplyTheme();
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
    private async void Refresh() => await RefreshAsync();

    private async Task RefreshAsync()
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
            var actions = Actions;
            var cloud = _cloud;
            var (counts, pages) = await Task.Run(() => (SyncCounts.Read(_dataDir), withPages ? Pages.Read(_dataDir, actions, cloud) : null));
            _counts = counts;
            ShowStatus();
            if (_shell is not null && pages is not null)
            {
                _pages = pages;
                _backdropArt = pages.BackdropArt;
                _library?.Update(pages.Games, pages.Tiles);
                _saves?.Update(pages.Games);
                _shell.Rail = pages.Rail;
                _shell.Reload();
                ShowSurface();
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

    private void SetFavourite(GameId game, bool favourite)
    {
        LauncherData.SetFavourite(_dataDir, game, favourite);
        Dispatcher.UIThread.Post(RefreshSoon);
    }

    /// <summary>The person's look on this PC, applied now and again whenever Windows' mode or accent changes (LOOK-01, LOOK-04).</summary>
    private void FollowTheme()
    {
        LoadLook();
        ApplyTheme();
        if (Application.Current?.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(ApplyTheme);
        }
    }

    private void LoadLook()
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
    }

    private void ApplyTheme()
    {
        var app = Application.Current!;
        var colors = app.PlatformSettings?.GetColorValues();
        var accent = colors?.AccentColor1 is { } a ? $"#{a.R:x2}{a.G:x2}{a.B:x2}" : null;
        _choice = _look.Resolve(colors?.ThemeVariant == PlatformThemeVariant.Light, accent);
        _tokens = ThemeService.Apply(app, _choice);
        _dark = _choice.Mode == ThemeMode.Dark;
        _transparency = WindowsLook.TransparencyOn();
        _window?.PaintFrame(_tokens, _dark);
        ShowSurface();
    }

    /// <summary>LOOK-18: with Windows' transparency effects turned off, Glossy goes Solid, and back when they're on again.</summary>
    private void FollowTransparency()
    {
        if (WindowsLook.TransparencyOn() != _transparency)
        {
            _transparency = !_transparency;
            ShowSurface();
        }
    }

    /// <summary>
    /// Glossy or Solid for the open window (LOOK-17, LOOK-18): Glossy shows the last-played game's art behind the page, at
    /// the page's strength, when the person chose it, the theme is dark and not pure black, Windows' transparency effects
    /// are on, and there's art. While a backdrop is being made, the window keeps what it shows.
    /// </summary>
    private void ShowSurface()
    {
        if (_window is null || _shell is null)
        {
            return;
        }

        // A game's page shows its own game's art; every other page the last-played game's.
        var strength = _shell.Strength;
        var art = _shell.BackdropArt ?? _backdropArt;
        var glass = art is not null && _look.ShowsGlossy(_choice, _transparency) ? ThemeEngine.Glass(_choice, strength) : null;
        if (glass is null)
        {
            _window.ShowSurface(null, null);
        }
        else if (_backdrops.TryGet(art!, strength, _choice, glass, _tokens, out var backdrop))
        {
            _window.ShowSurface(backdrop is null ? null : glass, backdrop);
        }
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

    /// <summary>
    /// ART-07: a while after the start (or at once, after a scan found games), art for new games, and art that's due a
    /// check, from Steam. What Steam says a game is comes with it, so the agent then watches the games anew: software
    /// such as Wallpaper Engine isn't taken for a game being played (PLAY-12).
    /// </summary>
    private async Task RefreshArtAsync(bool now = false)
    {
        try
        {
            if (!now)
            {
                await Task.Delay(ArtAfterStart, _stop.Token);
            }

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

            if (refresh.Asked > 0)
            {
                _agent.WatchNow();
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

    /// <summary>
    /// What the launcher's pages show, read off the UI thread: the games, the home screen, and their pictures at the size
    /// they're shown (the library's tiles, handed to the library that stays); and Glossy's art, the last-played game's (LOOK-17).
    /// </summary>
    private sealed record Pages(HomeViewModel Home, IReadOnlyList<LauncherGame> Games, IReadOnlyDictionary<GameId, TileItem> Tiles,
        IReadOnlyList<RailItem> Rail, string? BackdropArt)
    {
        public static Pages Read(string dataDir, LauncherActions actions, CloudErrorKind? cloud)
        {
            var now = DateTime.Now;
            var (games, home) = LauncherData.Read(dataDir, now);
            return new Pages(
                HomeViewModel.From(home, games, now, actions, Status(dataDir, now, cloud)),
                games,
                LibraryViewModel.Tiles(games, now, actions),
                ShellViewModel.DefaultRail(games.Where(g => g.IsRunning && g.Shown).MaxBy(g => g.RunningSinceUtc ?? DateTime.MinValue)?.Title, games.Count(g => g.NeedsYou)),
                home.Hero?.HeroPath ?? home.Hero?.CoverPath);
        }

        /// <summary>Home's top bar: where the saves go and whether it's reachable, this PC, and the other PCs with when each was last seen.</summary>
        private static HomeStatus? Status(string dataDir, DateTime nowLocal, CloudErrorKind? cloud)
        {
            try
            {
                var (thisPc, others, where) = LauncherData.Devices(dataDir);
                if (where is null)
                {
                    // First run skipped the cloud: the top bar offers Connect the cloud.
                    return new HomeStatus("No cloud yet", "Every version is kept on this PC until you connect a cloud; then it goes up in the background.",
                        thisPc.ToUpperInvariant(), others.Select(o => $"{o.Name.ToUpperInvariant()} · last seen {Launcher.WhenText(o.LastSeenUtc, nowLocal)}").ToList())
                    {
                        NoCloud = true,
                    };
                }

                var line = cloud switch
                {
                    CloudErrorKind.Offline => "Offline: new saves wait on this PC and go up once you're back online.",
                    CloudErrorKind.SignInExpired => "Signed out: new saves wait on this PC until you sign in to Google Drive again (gamesync signin).",
                    CloudErrorKind.StorageFull => "Your Google Drive is full: new saves wait on this PC until there's room.",
                    null when where == "Google Drive" => "Connected. Every game's saves go to your own Google Drive, into a folder only GameSync can see.",
                    null => $"Every game's saves go to {where}.",
                    _ => "The last sync had trouble reaching it; new saves wait on this PC, and GameSync tries again.",
                };
                return new HomeStatus(where, line, thisPc.ToUpperInvariant(),
                    others.Select(o => $"{o.Name.ToUpperInvariant()} · last seen {Launcher.WhenText(o.LastSeenUtc, nowLocal)}").ToList());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
            {
                return null;
            }
        }
    }
}
