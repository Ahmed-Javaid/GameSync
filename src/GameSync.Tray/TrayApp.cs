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
using SkiaSharp;

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
    private LibraryWatch? _libraryWatch;
    private AchievementWatch? _achievementWatch;

    /// <summary>
    /// KAN-123: the copy Steam doesn't run being played now, when there are records of copies' achievements to read: its
    /// record is watched for the popup, the window open or not.
    /// </summary>
    private volatile CopyPlaying? _copyPlaying;

    private sealed record CopyPlaying(long AppId, GameId Game, string Title);
    private AchievementPopups? _popups;
    private readonly HashSet<long> _achievementsAsked = [];
    private readonly TrayIcon _tray;
    private readonly ConsoleViewModel _console = new();
    private readonly DispatcherTimer _refresh;
    private readonly Dictionary<GameId, string> _playing = [];
    private readonly Dictionary<(int, TrayBadge, bool), byte[]> _icons = [];

    // KAN-80: every game's upload or download as it goes, for its saves and the tray's tooltip; and the games whose Play
    // was pressed that aren't running yet, whose Play says Starting… meanwhile.
    private readonly TransferBoard _transfers = new();
    private readonly DispatcherTimer _transferTick;
    private readonly Dictionary<GameId, DateTime> _starting = [];
    private Task _agentRun = Task.CompletedTask;
    private MainWindow? _window;
    private ShellViewModel? _shell;
    private Pages? _pages;
    private LibraryViewModel? _library;
    private SaveManagerViewModel? _saves;
    private SettingsViewModel? _settings;
    private TrophyRoomViewModel? _trophies;
    private FirstRunViewModel? _firstRun;
    private string _page = "home";
    private readonly Backdrops _backdrops;
    private Look _look = new();
    private ThemeChoice _choice = new();

    /// <summary>Counts changes of look, so one that waited for its backdrop gives way to a later one (KAN-124).</summary>
    private int _lookChanges;
    private IReadOnlyDictionary<string, string> _tokens = new Dictionary<string, string>();
    private bool _dark = true;
    private bool _transparency = true;
    private string? _backdropArt;
    private bool _setUp;
    private SyncCounts _counts = SyncCounts.None;
    private bool _working;
    private CloudErrorKind? _cloud;
    private TrayStatus _status = new(TrayMood.Synced, "GameSync");
    private bool _drawn;
    private DateTime _letGoneAt = DateTime.MinValue;
    private bool _letGoWaiting;
    private bool _stopped;

    // PKG-03: a newer GameSync downloaded and checked, waiting for Restart to update; one check at a time.
    private ReadyUpdate? _ready;
    private readonly SemaphoreSlim _checking = new(1, 1);

    private TrayApp(AppStart start, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _dataDir = start.DataDir;
        _lifetime = lifetime;
        _setUp = File.Exists(AppConfig.PathIn(_dataDir));
        _output = new AppOutput(_dataDir, Toasts.Show);
        _agent = new AppAgent(_dataDir, _output);
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Refresh());
        _transferTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TickTransfers());
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
        _tray.Menu = () => _ready is { } ready
            ?
            [
                new TrayMenuItem("Open GameSync", "open", IsDefault: true),
                new TrayMenuItem("Sync now", "sync", Enabled: _agent.IsRunning),
                new TrayMenuItem($"Restart to update to {ready.Version.ToString(3)}", "update", Enabled: _playing.Count == 0),
                TrayMenuItem.Separator,
                new TrayMenuItem("Quit GameSync", "quit"),
            ]
            :
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
            if (_pages?.Home is { } home)
            {
                home.IsSyncing = busy;
            }

            if (!busy)
            {
                LetGoSoon();
            }
        });
        _output.TransferChanged += update => Dispatcher.UIThread.Post(() =>
        {
            // The page follows every report; the tray's tooltip once a second at most, from the tick.
            _transfers.Apply(update, DateTime.UtcNow);
            if (!_transferTick.IsEnabled)
            {
                _transferTick.Start();
                ShowStatus();
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
                _starting.Remove(game);
                WatchCopy(game);
            }
            else
            {
                _playing.Remove(game);
                if (_copyPlaying?.Game == game)
                {
                    _copyPlaying = null;
                }
            }

            // Restart to update waits while a game is played.
            ShowUpdate(_ready);
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
        _ = Task.Run(UpdatesLoopAsync);

        // LIB-12: games installed or copied in while GameSync runs join the library, and are watched, without a restart.
        _libraryWatch = new LibraryWatch(_dataDir, _output, () => _agent.HoldsDiskWork, added =>
        {
            _agent.WatchNow();
            Dispatcher.UIThread.Post(RefreshSoon);
            if (added.Count > 0)
            {
                _ = Task.Run(() => RefreshArtAsync(now: true));
            }
        });
        _libraryWatch.Start();

        // ACH-09: an achievement unlocked while a Steam game runs pops up over it, with its chime.
        _popups = new AchievementPopups(_dataDir, () => _pages?.Games ?? []);
        _achievementWatch = new AchievementWatch(_dataDir, GameOfApp, runningCopy: RunningCopy, recordOf: app => Host.Achievements.RecordOf(_dataDir, app));
        _achievementWatch.Unlocked += unlock => Dispatcher.UIThread.Post(() => _popups?.Unlocked(unlock));
        _achievementWatch.Ended += app => Dispatcher.UIThread.Post(() => _popups?.Ended(app));
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
            var window = new MainWindow { DataContext = shell, DropZip = OpenImportSaves };
            window.Opened += (_, _) => window.PaintFrame(_tokens, _dark);

            // Back from a game: its page reads the live save again, so a named save played on from isn't In place any more.
            window.Activated += (_, _) =>
            {
                FollowTextSize();
                RefreshSoon();
            };
            window.Closed += (_, _) =>
            {
                _page = _shell?.Current ?? _page;
                _window = null;
                _shell = null;
                _pages = null;
                _library = null;
                _saves = null;
                _settings = null;
                _trophies = null;
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
        "settings" => Settings,
        "achievements" => Trophies,
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
            case "update":
                if (RestartToUpdate() is { Failed: true } refused)
                {
                    Toasts.Show("GameSync", refused.Text);
                }

                break;
        }
    }

    /// <summary>
    /// What the pages ask of the app: Play, Sync now, another page, hiding a game, favourites, the order, a game's page,
    /// its saves in the save manager, its Properties, and the save jobs.
    /// </summary>
    private LauncherActions Actions => new(Play, SyncNow, Show, SetHidden)
    {
        Start = StartAsync,
        IsStarting = _starting.ContainsKey,
        IsWorking = () => _working,
        TransferOf = _transfers.For,
        HasCloud = () => _cloud != CloudErrorKind.NotConnected && _pages?.Games.Any(g => g.Cloud is not null) != false,
        SetFavourite = SetFavourite,
        SetSort = sort => Task.Run(() => LauncherData.SetSort(_dataDir, sort)),
        SetInstalledOnly = installedOnly => Task.Run(() => LauncherData.SetInstalledOnly(_dataDir, installedOnly)),
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
        OpenShare = start =>
        {
            var share = new ShareViewModel(ShareActions, start);
            ShowDialog(share);
            share.Load();
        },
        OpenImport = OpenImportSaves,
        CheckPlan = ct => Task.Run(() => SyncPlans.CheckAsync(_dataDir, _output, ct), ct),
        RunPlan = async (chosen, ct) =>
        {
            try
            {
                var run = await Task.Run(() => SyncPlans.RunAsync(_dataDir, chosen, _output, ct), ct);
                _output.Say($"Plan: {run.Sentence}");
                return run;
            }
            finally
            {
                Dispatcher.UIThread.Post(RefreshSoon);
            }
        },
        LoadSpace = ct => Task.Run(() => SaveOverview.Space(_dataDir), ct),
        ConnectCloud = ConnectCloud,
        OpenGame = OpenGame,
        OpenAchievements = OpenAchievements,
        SetAchievementsLeftOut = SetAchievementsLeftOut,
        SeeZenith = game => _ = Task.Run(() => Host.Achievements.SeeZenith(_dataDir, game)),
        RestartToUpdate = () =>
        {
            if (RestartToUpdate() is { Failed: true } refused)
            {
                Toasts.Show("GameSync", refused.Text);
            }
        },
        LoadAchievements = (game, fetch, ct) =>
        {
            var shown = _pages?.Games.FirstOrDefault(g => g.Id == game);
            return Task.Run(async () =>
            {
                if (shown is null || Host.Achievements.For(_dataDir, shown) is not { } view)
                {
                    return null;
                }

                return fetch && await Host.Achievements.FetchAsync(_dataDir, view, ct, every: true) ? Host.Achievements.For(_dataDir, shown) : view;
            }, ct);
        },
        LoadAllAchievements = (how, ct) =>
        {
            var games = _pages?.Games ?? [];
            return Task.Run(async () =>
            {
                var views = Host.Achievements.ForAll(_dataDir, games);
                if (how == AchievementsLoad.Read || !await Host.Achievements.FetchAllAsync(_dataDir, views, ct, again: how == AchievementsLoad.Refresh))
                {
                    return views;
                }

                // Something new was kept: Home's card and the game pages show it too.
                Dispatcher.UIThread.Post(RefreshSoon);
                return Host.Achievements.ForAll(_dataDir, games);
            }, ct);
        },
        LoadGame = async (game, ct) =>
        {
            var detail = await Task.Run(() => GameDetails.ReadAsync(_dataDir, game, ct, SteamIdOf(game)), ct);
            Dispatcher.UIThread.Post(() => FetchAchievements(game, detail?.Achievements));
            return detail;
        },
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
        LoadVersions = ct => Task.Run(() => SaveHistory.ReadVersionsAsync(_dataDir, ct), ct),
        LoadLog = ct => Task.Run(() => SaveHistory.ReadLog(_dataDir), ct),
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
        OpenAddPlace = game => ShowDialog(new AddPlaceViewModel(game, TitleOf(game), Actions, starts: _pages?.Games.FirstOrDefault(g => g.Id == game)?.Syncs == false)),
        LookPlace = (game, path, ct) => Task.Run(() => SavePlaces.Look(_dataDir, game, path), ct),
        AddPlace = (game, path, category) => Job(async ct =>
        {
            try
            {
                var starts = _pages?.Games.FirstOrDefault(g => g.Id == game)?.Syncs == false;
                _output.Say(await GameSettings.ApplyAsync(_dataDir, game, new GamePropertiesChange { AddPlace = path, AddPlaceCategory = category },
                    () => _output.Say("Waiting for the sync in the background to finish first."), ct));

                // KAN-23: a game that starts syncing with the place is watched and synced at once, as Sync these saves does.
                if (starts)
                {
                    _agent.WatchNow();
                    await AppActions.RetryAsync(_dataDir, game, _output, ct);
                }
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _output.NeedsYou("GameSync", e.Message);
            }
        }),
        OpenLearnFinds = game => _ = OpenLearnFinds(game),
        SetLearn = async (game, on) =>
        {
            await Task.Run(() => LearnMode.Set(_dataDir, game, on));
            RefreshSoon();
        },
        AddLearned = async (game, paths) =>
        {
            try
            {
                // FIND-04: the places picked are added as Add a place adds them, and the game, syncing now, syncs at once.
                _output.Say(await Task.Run(() => LearnMode.AddAsync(_dataDir, game, paths, () => _output.Say("Waiting for the sync in the background to finish first."), _stop.Token)));
                _agent.WatchNow();
                _ = Job(ct => AppActions.RetryAsync(_dataDir, game, _output, ct));
                RefreshSoon();
                return null;
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return e.Message;
            }
        },
        Keep = async game =>
        {
            var kept = await Task.Run(() => AppActions.KeepAsync(_dataDir, game, _output, _stop.Token));
            if (kept)
            {
                _agent.WatchNow();
                RefreshSoon();
            }

            return kept;
        },
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
                _agent.UploadNow();
                Dispatcher.UIThread.Post(RefreshSoon);
            }

            return report;
        },
        // KAN-80: the page's button says Syncing… until the game's saves are kept here; the upload follows beside it.
        SyncGame = game => Job(async ct =>
        {
            if (await AppActions.SyncGameAsync(_dataDir, game, _output, ct))
            {
                _agent.WatchNow();
                await AppActions.RetryAsync(_dataDir, game, _output, ct);
            }
        }),
        BackUpNow = game => Job(ct => AppActions.BackUpNowAsync(_dataDir, game, _output, ct)),
        OpenNamedSave = start => ShowDialog(new NamedSaveViewModel(start, Actions)),
        OpenKeptCopies = start =>
        {
            var kept = new KeptCopiesViewModel(start, Actions);
            ShowDialog(kept);
            kept.Load();
        },
        LookKept = (game, progress, ct) => Task.Run(() => KeptSaves.LookAsync(_dataDir, game, ct, progress), ct),
        ApplyKept = async (game, bring, backupOnly, progress, ct) =>
        {
            try
            {
                var done = await Task.Run(() => KeptSaves.ApplyAsync(_dataDir, game, bring, backupOnly, _output, ct, progress), ct);
                _agent.WatchNow();

                // KAN-61: the live save's first version now: backed up here, or synced as the agent syncs a game.
                if (backupOnly)
                {
                    await Task.Run(() => AppActions.BackUpNowAsync(_dataDir, game, _output, ct), ct);
                }
                else
                {
                    _agent.SyncNow();
                }

                return done;
            }
            finally
            {
                _agent.UploadNow();
                Dispatcher.UIThread.Post(RefreshSoon);
            }
        },
        KeepNamed = (game, name) => JobAsync(ct => AppActions.SaveAsAsync(_dataDir, game, name, _output, ct)),
        ExportFolder = async (game, version, name, parent) =>
        {
            try
            {
                return await Task.Run(() => AppActions.ExportFolderAsync(_dataDir, game, version, name, parent, _output, _stop.Token));
            }
            finally
            {
                Dispatcher.UIThread.Post(RefreshSoon);
            }
        },
        SetNamedSort = sort => Task.Run(() => LauncherData.SetNamedSort(_dataDir, sort)),
        Restore = (game, version, name) => JobAsync(ct => AppActions.RestoreAsync(_dataDir, game, version, name, _output, ct)),
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

    /// <summary>Settings, for as long as the window is open: the section open and anything half typed stay when the app refreshes (SET-01).</summary>
    private SettingsViewModel Settings => _settings ??= new SettingsViewModel(SettingsActions, _look);

    /// <summary>What Settings asks of the app (SET-01): each change runs off the UI thread, and what went wrong comes back in its outcome.</summary>
    private SettingsActions SettingsActions => new()
    {
        Read = ct => Task.Run(() => (SettingsView?)SettingsData.Read(_dataDir), ct),
        Figures = ct => Task.Run(() => SettingsData.FiguresAsync(_dataDir, ct), ct),
        Cloud = ct => Task.Run(() => SettingsData.CloudAsync(_dataDir, ct), ct),
        SetLook = SetLook,
        Windows = () =>
        {
            var colors = Application.Current?.PlatformSettings?.GetColorValues();
            return (colors?.ThemeVariant == PlatformThemeVariant.Light, colors?.AccentColor1 is { } a ? $"#{a.R:x2}{a.G:x2}{a.B:x2}" : null);
        },
        MoveBackupFolder = (target, progress) => Change(async ct =>
        {
            var said = await SettingsData.MoveBackupFolderAsync(_dataDir, target, progress, () => _output.Say("Waiting for the sync in the background to finish first."), ct);
            _output.Say(said);
            return said;
        }),
        SetKeepEverything = everything => Change(_ => Task.FromResult(SettingsData.SetKeepEverything(_dataDir, everything))),
        AddGameFolder = folder => Change(async ct =>
        {
            var scan = await LocalGames.ScanFolderAsync(_dataDir, folder, _output, ct);
            _output.Say(scan.Sentence);
            _agent.WatchNow();
            _ = Task.Run(() => RefreshArtAsync(now: true));
            return scan.Sentence;
        }),
        RemoveGameFolder = folder => Change(async ct =>
        {
            var said = await LocalGames.RemoveFolderAsync(_dataDir, folder, _output, ct);
            _output.Say(said);
            _agent.WatchNow();
            return said;
        }),
        AddSaveFolder = folder => Change(async ct =>
        {
            var said = await SettingsData.AddSaveFolderAsync(_dataDir, folder, _output, ct);
            _output.Say(said);
            return said;
        }),
        RemoveSaveFolder = path => Change(async ct =>
        {
            var said = await SettingsData.RemoveSaveFolderAsync(_dataDir, path, _output, ct);
            _output.Say(said);
            return said;
        }),
        AddAchievementFolder = folder => Change(_ => Task.FromResult(Host.Achievements.AddRecordFolder(_dataDir, folder))),
        RemoveAchievementFolder = folder => Change(_ => Task.FromResult(Host.Achievements.RemoveRecordFolder(_dataDir, folder))),
        SetShareFolder = folder => Change(_ => Task.FromResult(Sharing.SetFolder(_dataDir, folder))),
        SetShareAsk = ask => Task.Run(() => Sharing.SetAsk(_dataDir, ask)),
        SetStartAtSignIn = on => Change(_ => Task.FromResult(SettingsData.SetStartAtSignIn(_dataDir, on))),
        SetDaily = at => Change(_ => Task.FromResult(SettingsData.SetDaily(_dataDir, at))),
        RunDailyNow = () => Change(_ => Task.FromResult(SettingsData.RunDailyNow(_dataDir))),
        SetDefaults = defaults => Task.Run(() => SettingsData.SetDefaults(_dataDir, defaults)),
        ApplyDefaults = (files, conflict) => Change(async ct =>
        {
            var said = await SettingsData.ApplyDefaultsAsync(_dataDir, files, conflict, () => _output.Say("Waiting for the sync in the background to finish first."), ct);
            _output.Say(said);
            return said;
        }),
        ConnectCloud = ConnectCloud,
        SignIn = () => Change(async ct =>
        {
            var account = await FirstRun.SignInAsync(_dataDir, OpenSignIn, ct);
            _agent.SyncNow();
            return account is null ? "Signed in to Google Drive; syncing carries on." : $"Signed in to Google Drive as {account}; syncing carries on.";
        }),
        SignOut = () => Change(async ct =>
        {
            var said = await SettingsData.SignOutAsync(_dataDir, ct);
            _output.Say(said);
            return said;
        }),
        OpenLink = OpenLink,
        OpenFolder = OpenFolder,
        RenamePc = name => Change(_ => Task.FromResult(SettingsData.RenamePc(_dataDir, name))),
        SetNotifications = (hold, dailyNote) => Task.Run(() =>
        {
            SettingsData.SetNotifications(_dataDir, hold, dailyNote);
            if (hold is { } h)
            {
                _output.HoldWhileFullscreen = h;
            }
        }),
        Diagnostics = ct => Task.Run(() => SettingsData.DiagnosticsAsync(_dataDir, ct), ct),
        SetAchievementPopups = async change =>
        {
            await Task.Run(() => AchievementPopupSettings.Set(_dataDir, change));
            _popups?.Reload();
        },
        HearChime = (sound, volume) => Chime.Play("gold", sound, volume),
        ReadUpdates = ct => Task.Run(() =>
        {
            using var updates = new Updates(_dataDir);
            return (UpdatesView?)updates.Read();
        }, ct),
        CheckUpdates = CheckUpdatesAsync,
        RestartToUpdate = () => Task.FromResult(RestartToUpdate()),
        SetUpdatesDaily = on => Task.Run(() => Updates.SetDaily(_dataDir, on)),
        TryPopup = delay => _popups?.Test(null, delay),
        AchievementGames = ct =>
        {
            var games = _pages?.Games ?? [];
            return Task.Run(() => Host.Achievements.SettingsRows(_dataDir, games), ct);
        },
        SetAchievementsLeftOut = (game, leftOut) =>
        {
            SetAchievementsLeftOut(game, leftOut);
            return Task.CompletedTask;
        },
        SetAchievementPopup = async (game, on) =>
        {
            await Task.Run(() => Host.Achievements.SetPopup(_dataDir, game, on));
            _popups?.Reload();
        },
        OpenDialog = ShowDialog,
        CloseDialog = () =>
        {
            if (_shell is not null)
            {
                _shell.Dialog = null;
            }
        },
    };

    /// <summary>A change Settings asked for, off the UI thread: what it came to, or why it couldn't be; the pages refresh after.</summary>
    private async Task<Outcome> Change(Func<CancellationToken, Task<string>> change)
    {
        try
        {
            return new Outcome(await Task.Run(() => change(_stop.Token)));
        }
        catch (OperationCanceledException)
        {
            return new Outcome("GameSync is closing, so nothing was changed.", Failed: true);
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException or CloudException
                                      or System.Net.Http.HttpRequestException or FormatException)
        {
            return new Outcome(e.Message, Failed: true);
        }
        finally
        {
            Dispatcher.UIThread.Post(RefreshSoon);
        }
    }

    /// <summary>What the share and import windows ask of the app (SHARE-01 to SHARE-13): each runs off the UI thread.</summary>
    private ShareActions ShareActions => new()
    {
        List = ct => Task.Run(() => Sharing.ListAsync(_dataDir, ct), ct),
        Pack = (picks, path, progress, ct) => Task.Run(() => Sharing.PackAsync(_dataDir, picks, path, progress, _output, ct), ct),
        Where = () => (Sharing.Folder(_dataDir), Sharing.AsksEachTime(_dataDir)),
        Preview = (zip, ct) => Task.Run(() => Sharing.Preview(_dataDir, zip), ct),
        Import = (zip, games) => Change(async ct =>
        {
            var said = await Sharing.ImportAsync(_dataDir, zip, games, _output, ct);
            _agent.WatchNow();
            return said;
        }),
        CoverOf = game => _pages?.Games.FirstOrDefault(g => g.Id == game)?.CoverPath,
        ShowInFolder = path =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                _output.Say($"! Explorer didn't open {path}: {e.Message}");
            }
        },
        Close = () =>
        {
            if (_shell is not null)
            {
                _shell.Dialog = null;
            }
        },
    };

    /// <summary>LOOK-07: the look picked in Settings, applied now and kept on this PC.</summary>
    private void SetLook(Look look)
    {
        var glossy = _look.ShowsGlossy(_choice, _transparency);
        _look = look;
        ApplyTheme(glossy);
        _ = Task.Run(() =>
        {
            try
            {
                using var state = new StateStore(_dataDir);
                look.Save(state);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _output.Say($"! Your appearance choice couldn't be kept for next time: {e.Message}");
            }
        });
    }

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

        // KAN-61: the copies kept by hand beside a live save come in as named saves, in the background, taking turns with
        // the first sync; the folders aren't changed.
        foreach (var kept in result.Kept)
        {
            Job(async ct =>
            {
                try
                {
                    await AppActions.KeptSavesAsync(_dataDir, kept.Game, kept.Folder, kept.Root, apply: true, _output, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _output.NeedsYou(kept.Title, $"The copies kept beside its live save weren't brought in: {e.Message} Import kept saves… on its saves brings them in.");
                }
            });
        }

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

    /// <summary>
    /// A job a page waits for, off the UI thread: null once it's done, or why it couldn't be (KAN-51); the pages show what
    /// it changed once it's done.
    /// </summary>
    private async Task<string?> JobAsync(Func<CancellationToken, Task<string?>> job)
    {
        try
        {
            return await Task.Run(() => job(_stop.Token));
        }
        catch (OperationCanceledException)
        {
            return "GameSync is closing, so nothing was changed.";
        }
        finally
        {
            // KAN-88: what the job made on this PC uploads now, beside whatever the person does next.
            _agent.UploadNow();
            Dispatcher.UIThread.Post(RefreshSoon);
        }
    }

    /// <summary>
    /// A job a page asked for, off the UI thread; the pages show what it changed once it's done. A page that waits for it
    /// shows its button busy meanwhile (KAN-80); the rest let it run.
    /// </summary>
    private Task Job(Func<CancellationToken, Task> job) => Task.Run(async () =>
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
            _agent.UploadNow();
            Dispatcher.UIThread.Post(RefreshSoon);
        }
    });

    /// <summary>
    /// KAN-80: Play, waited for: the check before playing and the start. The game's Play says Starting… from the press
    /// until the agent sees it running, or for half a minute at most when its store takes its time; at once again when it
    /// couldn't start, which the log and a notification say.
    /// </summary>
    private async Task StartAsync(GameId game)
    {
        _starting[game] = DateTime.UtcNow;
        var started = await Task.Run(() => AppActions.PlayAsync(_dataDir, game, _output));
        if (!started)
        {
            _starting.Remove(game);
        }
        else
        {
            var since = _starting.GetValueOrDefault(game);
            DispatcherTimer.RunOnce(() =>
            {
                if (_starting.TryGetValue(game, out var at) && at == since)
                {
                    _starting.Remove(game);
                    RefreshSoon();
                }
            }, TimeSpan.FromSeconds(30));
        }

        RefreshSoon();
    }

    /// <summary>
    /// KAN-80, once a second while a transfer runs or one has just ended: the tray's tooltip follows it, and one done a few
    /// seconds ago leaves its game's saves. Paused and failed ones stay until the next try, with nothing to tick.
    /// </summary>
    private void TickTransfers()
    {
        _transfers.Tick(DateTime.UtcNow);
        ShowStatus();
        if (_transfers.Running is null && !_transfers.Expiring)
        {
            _transferTick.Stop();
        }
    }

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
        var library = new LibraryViewModel(Actions, ReadSort(), installedOnly: ReadInstalledOnly());
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

    /// <summary>The library's Installed only, kept on this PC (KAN-47).</summary>
    private bool ReadInstalledOnly()
    {
        try
        {
            return LauncherData.ReadInstalledOnly(_dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A game's page in the library, from a cover on Home or a Needs you row (LIB-18); Back returns to that page.</summary>
    private void OpenGame(GameId game)
    {
        Library.Open(game, _shell?.Current is { } from && from != "library" ? from : null);
        _shell?.Open("library");
    }

    /// <summary>
    /// A game's achievements, every one, beside the library's list: from its page's View all (Back returns to its page),
    /// or from Home's card (Back returns there).
    /// </summary>
    /// <summary>
    /// KAN-110: leaves a game out of the achievements on this PC, or counts it again: its popups, its place on the
    /// Achievements page and Home; the pages read again.
    /// </summary>
    private void SetAchievementsLeftOut(GameId game, bool leftOut)
    {
        Host.Achievements.SetLeftOut(_dataDir, game, leftOut);
        _popups?.Reload();
        _achievementWatch?.Reload();
        if (_trophies is { } trophies)
        {
            trophies.Load();
        }

        RefreshSoon();
    }

    private void OpenAchievements(GameId game, string? returnTo)
    {
        Library.ShowAchievements(game, returnTo);
        _shell?.Open("library");
    }

    /// <summary>
    /// The Achievements page, for as long as the window is open: a game's achievements opened on it stay when the games
    /// refresh. Each visit reads every game's again, and asks Steam for what isn't kept yet.
    /// </summary>
    private TrophyRoomViewModel Trophies
    {
        get
        {
            if (_trophies is null)
            {
                _trophies = new TrophyRoomViewModel(Actions);
                _trophies.Update(_pages?.Games ?? []);
            }

            _trophies.Load();
            return _trophies;
        }
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
    /// <summary>Import saves (SHARE-10) for a shared zip: picked in the save manager, or dropped on the window.</summary>
    private void OpenImportSaves(string zip)
    {
        var import = new ImportSavesViewModel(ShareActions, zip);
        ShowDialog(import);
        import.Load();
    }

    /// <summary>
    /// FIND-04: what learn mode found for a game, over its saves; each place looked at first as Add a place would, so one it
    /// would refuse now (another game's folder, a Program Files folder) is shown locked with why.
    /// </summary>
    private async Task OpenLearnFinds(GameId game)
    {
        var (finds, refusals) = await Task.Run(() =>
        {
            var found = LearnMode.ReadFinds(_dataDir, game);
            var why = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var place in found?.Places ?? [])
            {
                try
                {
                    why[place.Path] = SavePlaces.Look(_dataDir, game, place.Path).Refused;
                }
                catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException)
                {
                    why[place.Path] = e.Message;
                }
            }

            return (found, why);
        });
        if (finds is null)
        {
            return;
        }

        ShowDialog(new LearnFindsViewModel(game, TitleOf(game), finds, refusals, Actions, () =>
        {
            if (_shell is not null)
            {
                _shell.Dialog = null;
            }
        }));
    }

    private void ShowDialog(object dialog)
    {
        if (_shell is not null)
        {
            _shell.Dialog = dialog;
        }
    }

    private string TitleOf(GameId game) => _pages?.Games.FirstOrDefault(g => g.Id == game)?.Title ?? game.Value;

    /// <summary>
    /// A link GameSync made (a game's Steam store page, Steam's own install and library links, GameSync's folder in
    /// Google Drive), in the app Windows keeps for it.
    /// </summary>
    private void OpenLink(string link)
    {
        if (!link.StartsWith("https://store.steampowered.com/app/", StringComparison.Ordinal) && !link.StartsWith("steam://", StringComparison.Ordinal) &&
            !link.StartsWith("https://drive.google.com/", StringComparison.Ordinal))
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

    /// <summary>ACH-09: the library's Steam game with this app ID, for the popup's words; read from the library when the window never opened.</summary>
    private (GameId Game, string Title)? GameOfApp(long app)
    {
        var games = _pages?.Games ?? LauncherData.Read(_dataDir, DateTime.Now).Games;
        return games.FirstOrDefault(g => g.SteamAppId == app && g.Store == GameSync.Core.Discovery.StoreKind.Steam) is { } game ? (game.Id, game.Title) : null;
    }

    /// <summary>KAN-123: the copy Steam doesn't run being played now, for the watcher to read its own record of achievements.</summary>
    private (long AppId, GameId Game, string Title)? RunningCopy() => _copyPlaying is { } copy ? (copy.AppId, copy.Game, copy.Title) : null;

    /// <summary>
    /// KAN-123: a game starts being played: when it's a copy Steam doesn't run, with a Steam app ID, and folders of copies'
    /// records are added in Settings, its record is watched while it plays (one it writes at its first unlock too). Looked up
    /// in the background, from the pages or, with the window closed, the library.
    /// </summary>
    private void WatchCopy(GameId game)
    {
        var games = _pages?.Games;
        _ = Task.Run(() =>
        {
            try
            {
                if (Host.Achievements.RecordFolders(_dataDir).Count == 0 ||
                    (games ?? LauncherData.Read(_dataDir, DateTime.Now).Games).FirstOrDefault(g => g.Id == game) is not { SteamAppId: { } app } shown ||
                    shown.Store == GameSync.Core.Discovery.StoreKind.Steam)
                {
                    return;
                }

                var copy = new CopyPlaying(app, game, shown.Title);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_playing.ContainsKey(game))
                    {
                        _copyPlaying = copy;
                    }
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Not watched this time: its unlocks still count once it closes.
            }
        });
    }

    /// <summary>
    /// KAN-123: the copies keeping their own record of achievements are given Steam's list (their names and icons) in the
    /// background, once a run each, so they count on the Achievements page and Home without their pages being opened first.
    /// </summary>
    private void FetchRecordLists(IReadOnlyList<LauncherGame> games)
    {
        var due = games.Where(g => g.Store != GameSync.Core.Discovery.StoreKind.Steam && g.SteamAppId is { } app && !_achievementsAsked.Contains(app)).ToList();
        if (due.Count == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var copies = Host.Achievements.WithRecords(_dataDir, due);
                if (copies.Count > 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var game in copies)
                        {
                            FetchAchievements(game.Id, null);
                        }
                    });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Looked at again with the next refresh.
            }
        });
    }

    private void Play(GameId game) => _ = StartAsync(game);

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

    /// <summary>
    /// Opens a page, on a view when asked: Home's My games opens the library on every game, and its Needs you the save
    /// manager's Needs you (KAN-46).
    /// </summary>
    private void Show(string page, string? tab)
    {
        if (page == "library" && tab is not null)
        {
            Library.ShowView(tab);
            Library.Selected = null;
        }
        else if (page == "saves" && tab is not null)
        {
            Saves.ShowTab(tab);
        }
        else if (page == "settings" && tab is not null)
        {
            Settings.Show(tab);
        }

        _shell?.Open(page);
    }

    /// <summary>
    /// What comes over the pipe: a second start (<c>show</c>), an installer closing GameSync for an update (<c>quit</c>),
    /// the tray icon's state and hover text (<c>status</c>), a changed look from <c>gamesync set surface</c> (<c>look</c>),
    /// or a command-line job (<c>run</c> and its arguments, BG-09), whose lines go back on <paramref name="lines"/>.
    /// </summary>
    private async Task<string> Answer(string message, TextWriter lines) => message switch
    {
        _ when message.StartsWith("run ", StringComparison.Ordinal) => await RunJobAsync(message[4..], lines),
        "show" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bring();
            return "ok";
        }),
        "look" => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LoadLook();
            ApplyTheme();
            _settings?.Appearance.Show(_look);
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
        _ when message.StartsWith("popup", StringComparison.Ordinal) => await Dispatcher.UIThread.InvokeAsync(() => TestPopup(message)),

        // R12: a gamesync://open/<game> link, whose game Links checked is this PC's: its page opens.
        _ when message.StartsWith("open ", StringComparison.Ordinal) && GameId.TryParse(message[5..], out var linked) => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bring();
            OpenGame(linked);
            return "ok";
        }),
        _ => "unknown",
    };

    /// <summary>
    /// ACH-09: <c>gamesync achievements popup [--tier gold|silver|bronze|hidden|zenith] [--in seconds]</c> sends
    /// <c>popup &lt;tier&gt; &lt;seconds&gt;</c>: a popup shown on purpose, after a moment to switch to a game.
    /// </summary>
    private string TestPopup(string message)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tier = parts.Length > 1 && parts[1] != "any" ? parts[1] : null;
        var seconds = parts.Length > 2 && int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, 120) : 0;
        if (_popups is null)
        {
            return "GameSync is still starting: try again in a moment.";
        }

        _popups.Test(tier, TimeSpan.FromSeconds(seconds));
        return "ok";
    }

    /// <summary>
    /// BG-09: <c>gamesync sync</c>, <c>plan</c>, <c>restore</c> or <c>launch</c>, done here while the app is open, under
    /// the engine lock like any job; what it prints goes back to the command line, and the window shows what changed.
    /// An app running as administrator, which it never should (R17), does none: the command line runs it with its own
    /// rights instead, so a job sent over the pipe never gets more than its sender has.
    /// </summary>
    private async Task<string> RunJobAsync(string json, TextWriter lines)
    {
        string[]? args;
        try
        {
            args = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            args = null;
        }

        if (args is null || !CliRelay.Takes(args) || Environment.IsPrivilegedProcess)
        {
            return "unknown";
        }

        try
        {
            var code = await Task.Run(() => CliRelay.RunAsync([.. args, "--data", _dataDir], lines));
            return $"exit {code}";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // What would have stopped the command line stops only the job: said there, the whole story in the app's log.
            _output.Say($"! gamesync {string.Join(' ', args)}: {e}");
            lines.WriteLine($"! {e.Message}");
            return "exit 1";
        }
        finally
        {
            _agent.WatchNow();
            _agent.UploadNow();
            Dispatcher.UIThread.Post(RefreshSoon);
        }
    }

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
                // KAN-125: the banner keeps showing the game it showed, and the page's colours keep following it.
                pages.Home.KeepShowing(_pages?.Home);
                pages.Home.HeroShown += () => HeroShown(pages);
                _pages = pages;
                ShowUpdate(_ready);
                _transfers.Cloud = pages.Games.Select(g => g.Cloud).FirstOrDefault(c => c is not null) ?? _transfers.Cloud;
                _backdropArt = pages.Home.HeroBackdropArt ?? pages.BackdropArt;
                PrepareHeroBackdrops(pages);
                _library?.Update(pages.Games, pages.Tiles);
                _trophies?.Update(pages.Games);
                FetchRecordLists(pages.Games);
                _saves?.Update(pages.Games);
                if (_shell.Current == "settings")
                {
                    _settings?.Reload();
                }

                _shell.Rail = pages.Rail;
                _shell.Reload();
                ShowSurface();
                if (pages.Achievements is { } homeCard)
                {
                    FetchAchievements(homeCard.Game, homeCard);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _console.Add(DateTime.Now, $"! GameSync couldn't read this PC's games: {e.Message}");
        }
    }

    private void ShowStatus()
    {
        // KAN-80: an upload under way says which game and how far, in place of "syncing".
        var moving = _transfers.Running is var (game, view) ? $"{view.Short} {view.GameTitle ?? TitleOf(game)}" : null;
        var status = TrayStatus.From(_setUp, _counts, _working, _cloud, _playing.Values.FirstOrDefault(), moving);
        if (_drawn && status == _status)
        {
            // Nothing new to say: the icon isn't made again (an upload asks every second).
            return;
        }

        _status = status;
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
            _drawn = true;
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
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() => ApplyTheme());
        }
    }

    /// <summary>
    /// KAN-125: Home's banner shows another game: Glossy's colours follow it (the light from the click, KAN-54), and its
    /// Achievements card's icons are fetched if they aren't kept.
    /// </summary>
    private void HeroShown(Pages pages)
    {
        if (!ReferenceEquals(pages, _pages))
        {
            return;
        }

        _backdropArt = pages.Home.HeroBackdropArt ?? pages.BackdropArt;
        ShowSurface();
        if (pages.Home.Achievements is { } card && Host.Achievements.For(_dataDir, pages.Games.First(g => g.Id == card.Game)) is { } view)
        {
            FetchAchievements(card.Game, view);
        }
    }

    /// <summary>
    /// KAN-125: the backdrops of the games Home's banner can show, made in the background, so moving between them changes the
    /// page's colours at once.
    /// </summary>
    private void PrepareHeroBackdrops(Pages pages)
    {
        if (!_look.ShowsGlossy(_choice, _transparency) || ThemeEngine.Glass(_choice, pages.Home.Strength) is not { } glass)
        {
            return;
        }

        foreach (var art in pages.Home.HeroSlides.Select(s => s.BackdropArt).OfType<string>().Distinct())
        {
            _ = _backdrops.GetAsync(art, pages.Home.Strength, _choice, glass, _tokens);
        }
    }

    /// <summary>
    /// ACH-02, ACH-03: the icons and rarity a game's achievements show that Steam's files on this PC don't hold, asked of
    /// Steam once a run per game, in the background; the pages read again once they're kept. A game Steam here holds no
    /// achievements for gets Steam's list first (KAN-111: the owner's Cuphead and Child of Light).
    /// </summary>
    private void FetchAchievements(GameId game, GameAchievementsView? view)
    {
        var shown = _pages?.Games.FirstOrDefault(g => g.Id == game);
        if ((view?.AppId ?? shown?.SteamAppId) is not { } appId || !_achievementsAsked.Add(appId))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var changed = view is { IsTracked: true }
                    ? await Host.Achievements.FetchAsync(_dataDir, view, _stop.Token)
                    : await Host.Achievements.FetchForAsync(_dataDir, game, shown?.Title ?? view?.Title ?? game.Value, shown?.Store, appId, _stop.Token);
                if (changed)
                {
                    Dispatcher.UIThread.Post(RefreshSoon);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Asked again at the next start.
            }
        });
    }

    /// <summary>
    /// A11Y-04: Windows' text size, read when the look is applied and whenever the window comes forward (as it does back
    /// from Windows' Settings), so a change shows at once.
    /// </summary>
    private static void FollowTextSize()
    {
        try
        {
            TextScale.Apply(Application.Current!.Resources, WindowsLook.TextScale());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unreadable: the sizes stay as they are.
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

    /// <param name="glossyBefore">Whether the window was Glossy before the look just picked in Settings; otherwise the look is the one shown.</param>
    private async void ApplyTheme(bool? glossyBefore = null)
    {
        FollowTextSize();
        var app = Application.Current!;
        var colors = app.PlatformSettings?.GetColorValues();
        var accent = colors?.AccentColor1 is { } a ? $"#{a.R:x2}{a.G:x2}{a.B:x2}" : null;
        var choice = _look.Resolve(colors?.ThemeVariant == PlatformThemeVariant.Light, accent);
        var transparency = WindowsLook.TransparencyOn();
        var change = ++_lookChanges;

        // KAN-124: in Glossy the new look's backdrop is made first, or found kept, so the window changes once and all of it.
        // Switching between Dark and Light used to show the new colours over the old glass while it was made, then change
        // again, with a second animation, when it came.
        if (_window is { IsVisible: true } && _shell is not null && (_shell.BackdropArt ?? _backdropArt) is { } art && _look.ShowsGlossy(choice, transparency)
            && ThemeEngine.Glass(choice, _shell.Strength) is { } next)
        {
            await Task.WhenAny(_backdrops.GetAsync(art, _shell.Strength, choice, next, ThemeEngine.Build(choice)), Task.Delay(TimeSpan.FromSeconds(1)));
            if (change != _lookChanges)
            {
                // A later change came while this one waited: that one shows.
                return;
            }
        }

        void Apply()
        {
            _choice = choice;
            _tokens = ThemeService.Apply(app, _choice);
            _dark = _choice.Mode == ThemeMode.Dark;
            _transparency = transparency;
            _window?.PaintFrame(_tokens, _dark);
            ShowSurface();
        }

        // KAN-76: a new look the open window shows comes through softly instead of jumping, from the click; Glossy and Solid
        // alone turn into each other evenly, all of the window at once (the owner, 1 Oct). Windows' accent changing while the
        // look doesn't use it changes nothing that shows.
        var colours = !ThemeEngine.Build(choice).OrderBy(t => t.Key).SequenceEqual(_tokens.OrderBy(t => t.Key));
        var surface = (glossyBefore ?? _look.ShowsGlossy(_choice, _transparency)) != _look.ShowsGlossy(choice, transparency);
        if (_window is { } window && (colours || surface))
        {
            window.ChangeLook(Apply, evenly: !colours);
        }
        else
        {
            Apply();
        }

        _settings?.Appearance.Refresh();
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

            // KAN-124: the other mode's too, in the background, so switching between Dark and Light needn't wait for it.
            var other = _choice with { Mode = _choice.Mode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light };
            if (backdrop is not null && _look.ShowsGlossy(other, _transparency) && ThemeEngine.Glass(other, strength) is { } otherGlass)
            {
                _ = _backdrops.GetAsync(art!, strength, other, otherGlass, ThemeEngine.Build(other));
            }
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
    /// closed (at most once a minute: work sooner than that is let go of once the minute is up), what that used is let
    /// go at once rather than whenever .NET gets round to it, which in an app this quiet can be hours.
    /// </summary>
    private void LetGoSoon(bool force = false)
    {
        if (!force && _window is not null)
        {
            return;
        }

        var since = DateTime.UtcNow - _letGoneAt;
        if (!force && since < TimeSpan.FromMinutes(1))
        {
            if (!_letGoWaiting)
            {
                _letGoWaiting = true;
                DispatcherTimer.RunOnce(() =>
                {
                    _letGoWaiting = false;
                    LetGoSoon();
                }, TimeSpan.FromMinutes(1) - since);
            }

            return;
        }

        _letGoneAt = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            Collect();

            // A closed window's pictures are let go by the renderer only after a moment: measured 30 Sep, the first
            // collection left about 300 MB that a later one brought down to about 130 (PERF-01). So once more, later.
            if (force)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                if (_window is null)
                {
                    Collect();
                }
            }
        });

        void Collect()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // KAN-62: with no window, Skia's caches of pictures and glyphs hold nothing anyone will see soon, and the
            // pages the window used needn't stay in memory: measured 1 Oct, a maximized window's library left 110 MB
            // after these collections.
            if (_window is null)
            {
                SKGraphics.PurgeAllCaches();
                ProcessMemory.Trim();
            }

            if (Environment.GetEnvironmentVariable("GAMESYNC_MEMORY_STATS") == "1")
            {
                var info = GC.GetGCMemoryInfo();
                _output.Say($"Memory: .NET heap {info.HeapSizeBytes / 1048576.0:0.0} MB, committed {info.TotalCommittedBytes / 1048576.0:0.0} MB.");
            }
        }
    }

    /// <summary>
    /// PKG-03 (design system version 53): what was downloaded before shows at once; then, two minutes after start and
    /// every hour after, the daily check when it's due (a day since the last, six hours when GitHub couldn't be reached),
    /// which gets a newer GameSync ready in the background.
    /// </summary>
    private async Task UpdatesLoopAsync()
    {
        try
        {
            using (var updates = new Updates(_dataDir))
            {
                updates.CleanUp();
                var ready = updates.FindReady();
                Dispatcher.UIThread.Post(() => ShowUpdate(ready));
            }

            await Task.Delay(TimeSpan.FromMinutes(2), _stop.Token);
            while (!_stop.IsCancellationRequested)
            {
                bool due;
                using (var updates = new Updates(_dataDir))
                {
                    due = updates.Due();
                }

                if (due)
                {
                    await CheckUpdatesAsync();
                }

                await Task.Delay(TimeSpan.FromHours(1), _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _output.Say($"! Checking for updates: {e.Message}");
        }
    }

    /// <summary>
    /// The daily check, or Check now: asks GitHub, gets a newer GameSync ready, and shows it in Settings as it goes. Once
    /// one is ready, a notification says so, once per version.
    /// </summary>
    private async Task<Outcome> CheckUpdatesAsync()
    {
        if (!await _checking.WaitAsync(0))
        {
            return new Outcome("");
        }

        try
        {
            Dispatcher.UIThread.Post(() => _settings?.Updates.ShowChecking());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var shown = TimeSpan.Zero;
            var progress = new Forward<UpdateProgress>(p =>
            {
                // A tenth of a second apart at most, on the UI thread.
                if (clock.Elapsed - shown < TimeSpan.FromMilliseconds(100) && p.Done < p.Total)
                {
                    return;
                }

                shown = clock.Elapsed;
                var speed = p.Done / Math.Max(0.5, clock.Elapsed.TotalSeconds);
                var right = $"{speed / 1048576.0:0.0} MB/s, about {Math.Max(1, (p.Total - p.Done) / Math.Max(1, speed)):0} s left";
                Dispatcher.UIThread.Post(() => _settings?.Updates.ShowDownloading(p.Version, p.Done, p.Total, right));
            });

            UpdatesView view;
            using (var updates = new Updates(_dataDir))
            {
                view = await updates.CheckAndGetAsync(progress, _stop.Token);
            }

            if (view.Ready is { } ready)
            {
                _output.Say($"GameSync {ready.Version.ToString(3)} is ready: Restart to update installs it.");
                TellOnce(ready);
            }
            else if (view.Refused is { } refused && refused.Version > view.Current && refused.AtUtc >= DateTime.UtcNow.AddMinutes(-5))
            {
                _output.Say($"! GameSync {refused.Version.ToString(3)} wasn't installed: {refused.Reason}.");
            }

            Dispatcher.UIThread.Post(() =>
            {
                _settings?.Updates.Finished(view, DateTime.Now);
                ShowUpdate(view.Ready);
            });
            return new Outcome("");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Dispatcher.UIThread.Post(() => _settings?.Reload());
            return new Outcome($"GameSync couldn't check for updates: {e.Message}", Failed: true);
        }
        catch (OperationCanceledException)
        {
            return new Outcome("");
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>A notification the first time a version is ready, never again for it.</summary>
    private void TellOnce(ReadyUpdate ready)
    {
        using var state = new StateStore(_dataDir);
        var version = ready.Version.ToString(3);
        if (state.GetSetting(Updates.ToldKey) == version)
        {
            return;
        }

        state.SetSetting(Updates.ToldKey, version);
        Toasts.Show($"GameSync {version} is ready", "Restart to update, in GameSync or its tray menu: it closes, installs it and opens again in a few seconds.");
    }

    /// <summary>What Home's top bar, Settings and the tray menu offer: Restart to update while one is ready, waiting while a game is played.</summary>
    private void ShowUpdate(ReadyUpdate? ready)
    {
        _ready = ready;
        var tip = ready is null ? null
            : _playing.Count > 0 ? $"GameSync {ready.Version.ToString(3)} is ready. It installs after you finish playing {_playing.Values.First()}."
            : $"GameSync {ready.Version.ToString(3)} is ready: GameSync closes, installs it and opens again in a few seconds.";
        if (_pages?.Home is { } home)
        {
            home.UpdateReady = tip;
            home.CanUpdate = _playing.Count == 0;
        }

        if (_settings is { } settings)
        {
            settings.Updates.CanRestart = _playing.Count == 0;
            settings.Updates.RestartTip = tip ?? settings.Updates.RestartTip;
        }
    }

    /// <summary>
    /// Restart to update (PKG-03, R19): the update is checked once more as it starts, then GameSync quits as its tray
    /// menu's Quit does, and the installer, which waits for it, replaces it and opens it again. Not while a game is
    /// played; refused, it says why and nothing is installed.
    /// </summary>
    private Outcome RestartToUpdate()
    {
        if (_ready is not { } ready)
        {
            return new Outcome("No update is ready yet: Check now looks for one.", Failed: true);
        }

        if (_playing.Count > 0)
        {
            return new Outcome($"GameSync {ready.Version.ToString(3)} installs after you finish playing {_playing.Values.First()}.", Failed: true);
        }

        try
        {
            using var updates = new Updates(_dataDir);
            using var installer = updates.StartInstall(ready, showWindow: _window is not null);
        }
        catch (ReleaseRefusedException e)
        {
            _output.NeedsYou("GameSync", $"GameSync {ready.Version.ToString(3)} wasn't installed: {e.Message}.");
            ShowUpdate(null);
            _settings?.Reload();
            return new Outcome($"GameSync {ready.Version.ToString(3)} wasn't installed: {e.Message}.", Failed: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new Outcome($"The update couldn't start: {e.Message}", Failed: true);
        }

        _output.Say($"Installing GameSync {ready.Version.ToString(3)}: GameSync closes, updates and opens again.");
        DispatcherTimer.RunOnce(Quit, TimeSpan.FromMilliseconds(250));
        return new Outcome($"Installing GameSync {ready.Version.ToString(3)}…");
    }

    /// <summary>Progress passed on as it's reported, on the thread reporting it.</summary>
    private sealed class Forward<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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
        _libraryWatch?.Dispose();
        _achievementWatch?.Dispose();
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
        IReadOnlyList<RailItem> Rail, string? BackdropArt, GameAchievementsView? Achievements)
    {
        public static Pages Read(string dataDir, LauncherActions actions, CloudErrorKind? cloud)
        {
            var now = DateTime.Now;
            var (games, home) = LauncherData.Read(dataDir, now);
            var achievements = Host.Achievements.ForHome(dataDir, home.Hero, games);

            // ACH-02: how far each game's achievements are, for the ring on its cover; only those counted (Steam's own, not left out).
            var progress = Host.Achievements.ForAll(dataDir, games).ToDictionary(v => v.Game, v => (v.Unlocked, v.Total));
            return new Pages(
                // KAN-125: each game the banner can show brings its own Achievements card.
                HomeViewModel.From(home, games, now, actions, Status(dataDir, now, cloud), achievements, game => Host.Achievements.ForHome(dataDir, game, games), progress,
                    Host.Achievements.ZenithsSeen(dataDir)),
                games,
                LibraryViewModel.Tiles(games, now, actions, progress),
                ShellViewModel.DefaultRail(games.Where(g => g.IsRunning && g.Shown).MaxBy(g => g.RunningSinceUtc ?? DateTime.MinValue)?.Title, games.Count(g => g.NeedsYou)),
                // Glossy's art: the hero game's, or when it has none (a game in its own folder with no art), the most
                // recently played game's that has some, so the pages don't go Solid (KAN-54).
                home.Hero?.HeroPath ?? home.Hero?.CoverPath ?? games
                    .Where(g => g.Shown && (g.HeroPath ?? g.CoverPath) is not null)
                    .OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
                    .Select(g => g.HeroPath ?? g.CoverPath)
                    .FirstOrDefault(),
                achievements.View);
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
