using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>What first run asks of the app: the scan, the games it found, a game folder, the cloud, and finishing.</summary>
public sealed record SetupActions(
    Func<IProgress<ScanProgress>, CancellationToken, Task<SetupScan>> Scan,
    Func<CancellationToken, Task<IReadOnlyList<SetupGroup>>> Groups,
    Func<string, CancellationToken, Task<string?>> AddGameFolder,
    CloudActions Cloud,
    Func<SetupChoice, CancellationToken, Task<SetupResult>> Finish)
{
    /// <summary>Something missing?'s Add a game or folder: its dialog over first run (LIB-13).</summary>
    public Action? AddGame { get; init; }
}

/// <summary>Connecting the cloud, in first run's step and in the Connect cloud dialog: Google Drive, or a folder.</summary>
/// <param name="CanSignIn">This copy of GameSync has a Google client, so Sign in with Google can work (CLOUD-12).</param>
/// <param name="SignIn">Opens the browser and waits for Google (CLOUD-02); the account's address, when Google gives it.</param>
/// <param name="Folder">The cloud in the folder picked (GameSync's own folder inside it); throws <see cref="UsageException"/> when it can't be.</param>
public sealed record CloudActions(bool CanSignIn, Func<CancellationToken, Task<string?>> SignIn, Func<string, string> Folder)
{
    /// <summary>This PC is signed in to Google Drive already.</summary>
    public bool SignedIn { get; init; }
}

/// <summary>
/// The cloud's two ways (design system → OnboardingScreen's Connect the cloud, ConnectCloudDialog): Sign in with Google,
/// or a folder (a NAS, a USB drive, another disk). What's chosen becomes games.json's cloud: "drive", the folder, or
/// none, for Skip for now, which keeps every version on this PC.
/// </summary>
public sealed partial class CloudSetupViewModel : ObservableObject
{
    public const string Drive = "drive";

    private readonly CloudActions _actions;

    /// <summary>What's chosen: <c>none</c>, <c>drive</c> or <c>folder</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(HasFolder), nameof(IsConnected), nameof(Remote), nameof(NoFolder), nameof(SignInIsMain))]
    private string _choice = "none";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedInTitle))]
    private string? _account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderTitle), nameof(Remote))]
    private string? _folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInLabel))]
    private bool _isSigningIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public CloudSetupViewModel(CloudActions actions)
    {
        _actions = actions;
        if (actions.SignedIn)
        {
            _choice = Drive;
        }

        SignInCommand = new AsyncRelayCommand(SignInAsync, () => CanSignIn && !IsSigningIn);
        ChangeFolderCommand = new RelayCommand(() =>
        {
            Folder = null;
            Choice = "none";
        });
    }

    public bool CanSignIn => _actions.CanSignIn;

    public bool CannotSignIn => !CanSignIn;

    public bool IsSignedIn => Choice == Drive;

    public bool HasFolder => Choice == "folder";

    public bool NoFolder => !HasFolder;

    public bool IsConnected => IsSignedIn || HasFolder;

    /// <summary>Sign in with Google leads until a folder is chosen; then Continue does, and it steps down. It never leads when it can't work.</summary>
    public bool SignInIsMain => !HasFolder && CanSignIn;

    public bool HasError => Error is not null;

    public string SignedInTitle => Account is { Length: > 0 } account ? $"Signed in as {account}" : "Signed in";

    public string FolderTitle => $"Saves go to {Folder}";

    public string SignInLabel => IsSigningIn ? "Waiting for your browser…" : "Sign in with Google";

    /// <summary>games.json's cloud: "drive", the folder, or "none".</summary>
    public string Remote => Choice switch
    {
        Drive => Drive,
        "folder" when Folder is { } folder => folder,
        _ => AppConfig.NoCloud,
    };

    public ICommand SignInCommand { get; }

    /// <summary>Change… on a chosen folder: back to choosing.</summary>
    public ICommand ChangeFolderCommand { get; }

    /// <summary>A folder picked in Windows' folder picker: checked, then the cloud is GameSync's folder inside it.</summary>
    public void ChooseFolder(string picked)
    {
        try
        {
            Folder = _actions.Folder(picked);
            Choice = "folder";
            Error = null;
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = e.Message;
        }
    }

    private async Task SignInAsync()
    {
        IsSigningIn = true;
        Error = null;
        try
        {
            Account = await _actions.SignIn(CancellationToken.None);
            Choice = Drive;
        }
        catch (OperationCanceledException)
        {
            Error = "Sign-in stopped before Google answered. Try again when you're ready.";
        }
        catch (TimeoutException)
        {
            Error = "Google didn't answer within 5 minutes, so GameSync stopped waiting. Try again.";
        }
        catch (Exception e)
        {
            Error = $"Signing in didn't finish: {e.Message}";
        }
        finally
        {
            IsSigningIn = false;
        }
    }
}

/// <summary>A store in Scan this PC: its name, where its games are, and how many ("0", faint, when none).</summary>
public sealed record StoreRow(string Name, string Where, string Count)
{
    public bool IsEmpty => Count == "0";

    public override string ToString() => $"{Name}: {(IsEmpty ? "no games" : Count)}, {Where}";
}

/// <summary>A game in first run's Choose games (ONB-03): its cover, its name, where its saves are and how they were found.</summary>
public sealed partial class SetupGameRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isChecked;

    public SetupGameRow(SetupGame game, bool isChecked, bool tickable, Action changed)
    {
        Game = game;
        _isChecked = isChecked;
        Tickable = tickable;
        _changed = changed;
        Cover = ArtImages.Load(game.CoverPath, 48);
    }

    public SetupGame Game { get; }

    public IImage? Cover { get; }

    public bool HasCover => Cover is not null;

    public bool NoCover => Cover is null;

    public string Initial => GsGameTile.InitialOf(Game.Title);

    public string Title => Game.Title;

    public string SavePath => Game.SavePath ?? "";

    public string FoundBy => Game.FoundBy ?? "";

    /// <summary>It has a box: every group but No saves found yet, whose games are watched instead.</summary>
    public bool Tickable { get; }

    partial void OnIsCheckedChanged(bool value) => _changed();

    public override string ToString() => Tickable ? $"{Title}, {(IsChecked ? "syncs" : "doesn't sync")}" : $"{Title}, watched the first time you play";
}

/// <summary>
/// A group of first run's Choose games (ONB-02): a box for the whole group, its name, how many and why, and its games,
/// the first four at a time. No saves found yet has a clock instead of a box: its games are watched the first time
/// they're played.
/// </summary>
public sealed partial class SetupGroupViewModel : ObservableObject
{
    public const int Shown = 4;

    private readonly Action _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows), nameof(HasMore), nameof(ShowsOpen))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows), nameof(HasMore))]
    private bool _showsAll;

    [ObservableProperty]
    private bool? _isChecked;

    public SetupGroupViewModel(SetupGroup group, Action changed)
    {
        Kind = group.Kind;
        _changed = changed;
        var ticked = Kind is SetupGroupKind.Sync or SetupGroupKind.StoreCloud or SetupGroupKind.NotInstalled;
        Rows = group.Games.Select(g => new SetupGameRow(g, ticked && Tickable, Tickable, RowChanged)).ToList();
        (Title, Why) = Describe(Kind, group.Games);
        _isOpen = Kind == SetupGroupKind.Sync;
        Recount();
        OpenCommand = new RelayCommand(() => IsOpen = true);
        ShowAllCommand = new RelayCommand(() => ShowsAll = true);
        ToggleCommand = new RelayCommand(() =>
        {
            var on = IsChecked != true;
            _quiet = true;
            foreach (var row in Rows)
            {
                row.IsChecked = on;
            }

            _quiet = false;
            RowChanged();
        });
    }

    private bool _quiet;

    public SetupGroupKind Kind { get; }

    public string Title { get; }

    public string Why { get; }

    public IReadOnlyList<SetupGameRow> Rows { get; }

    public bool Tickable => Kind != SetupGroupKind.NoSaves;

    public bool IsWatch => !Tickable;

    public string Summary => $"{(Rows.Count == 1 ? "1 game" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} games")} · {Why}";

    public IReadOnlyList<SetupGameRow> VisibleRows => !IsOpen ? [] : ShowsAll ? Rows : Rows.Take(Shown).ToList();

    public bool HasMore => IsOpen && !ShowsAll && Rows.Count > Shown;

    public string MoreLabel => $"Show {(Rows.Count - Shown).ToString(CultureInfo.InvariantCulture)} more";

    /// <summary>A closed group offers Show, which opens it.</summary>
    public bool ShowsOpen => !IsOpen;

    public ICommand OpenCommand { get; }

    public ICommand ShowAllCommand { get; }

    /// <summary>The group's box: ticks every game in it, or unticks them when all were ticked.</summary>
    public ICommand ToggleCommand { get; }

    /// <summary>The group's box, as a screen reader says it: "Untick every game in Sync".</summary>
    public string ToggleName => $"{(IsChecked == true ? "Untick" : "Tick")} every game in {Title}";

    private void RowChanged()
    {
        if (_quiet)
        {
            return;
        }

        Recount();
        _changed();
    }

    private void Recount()
    {
        var on = Rows.Count(r => r.IsChecked);
        IsChecked = !Tickable || Rows.Count == 0 ? false : on == Rows.Count ? true : on == 0 ? false : null;
        OnPropertyChanged(nameof(ToggleName));
    }

    /// <summary>A group's name, and why its games are in it, naming a few when it helps.</summary>
    private static (string Title, string Why) Describe(SetupGroupKind kind, IReadOnlyList<SetupGame> games) => kind switch
    {
        SetupGroupKind.Sync => ("Sync", "saves found, no store cloud"),
        SetupGroupKind.StoreCloud => ("Synced by their store", "Steam or Epic syncs these between PCs; GameSync keeps a backup of every version"),
        SetupGroupKind.OnlineOnly => ("Probably online-only", $"only settings files found · {Names(games, 2)}"),
        SetupGroupKind.NotInstalled => ("Saves found, game not installed", $"kept safe, offered back if you install them · {Names(games, 1)}"),
        _ => ("No saves found yet", $"{Names(games, 3)} · watched the first time you play"),
    };

    /// <summary>"Apex Legends, Rocket League and 10 more".</summary>
    public static string Names(IReadOnlyList<SetupGame> games, int named)
    {
        var names = games.Take(named).Select(g => g.Title).ToList();
        var more = games.Count - names.Count;
        return more <= 0
            ? names.Count > 1 ? $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}" : string.Join("", names)
            : $"{string.Join(", ", names)} and {more.ToString(CultureInfo.InvariantCulture)} more";
    }

    public override string ToString() => $"{Title}, {Summary}";
}

/// <summary>
/// First run (design system → OnboardingScreen; ONB-01 to ONB-05): four steps down the left, scan this PC, choose games,
/// connect the cloud, then backups and startup, with no side rail until it's done. The scan starts when the page opens
/// and only reads; nothing syncs until Start using GameSync.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject, IPageSurface, IWholeWindowPage
{
    public static readonly IReadOnlyList<(string Id, string Label, string Icon)> StepList =
    [
        ("scan", "Scan this PC", "search"),
        ("choose", "Choose games", "library"),
        ("cloud", "Connect the cloud", "cloud"),
        ("daily", "Backups and startup", "clock"),
    ];

    private readonly SetupActions _actions;
    private int _reached;
    private bool _started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScan), nameof(IsChoose), nameof(IsCloud), nameof(IsDaily))]
    private string _step = "scan";

    [ObservableProperty]
    private IReadOnlyList<NavItem> _steps = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoose), nameof(ScanDone))]
    private bool _isScanning;

    [ObservableProperty]
    private double _scanValue;

    [ObservableProperty]
    private string _scanLabel = "Getting ready to scan";

    [ObservableProperty]
    private string _scanRight = "";

    [ObservableProperty]
    private IReadOnlyList<StoreRow> _stores = [];

    [ObservableProperty]
    private IReadOnlyList<FolderItem> _folders = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScanError), nameof(CanChoose))]
    private string? _scanError;

    [ObservableProperty]
    private IReadOnlyList<SetupGroupViewModel> _groups = [];

    [ObservableProperty]
    private string _chooseSubtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAntiCheat))]
    private string? _antiCheatNote;

    [ObservableProperty]
    private bool _startAtSignIn = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DailyTitle))]
    private bool _dailyOn = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DailyTitle))]
    private string _dailyTime = "20:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotEditingTime))]
    private bool _editingTime;

    [ObservableProperty]
    private string _timeText = "20:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimeError))]
    private string? _timeError;

    [ObservableProperty]
    private string _readyText = "";

    [ObservableProperty]
    private string _firstBackupText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishLabel))]
    private bool _isFinishing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFinishError))]
    private string? _finishError;

    public FirstRunViewModel(SetupActions actions)
    {
        _actions = actions;
        Cloud = new CloudSetupViewModel(actions.Cloud);
        Cloud.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CloudSetupViewModel.IsConnected))
            {
                OnPropertyChanged(nameof(CloudNextLabel));
                OnPropertyChanged(nameof(CloudNextIsMain));
                Recount();
            }
        };
        NextCommand = new RelayCommand(Next);
        BackCommand = new RelayCommand(Back);
        ScanAgainCommand = new RelayCommand(() => _ = ScanAsync());
        EditTimeCommand = new RelayCommand(() =>
        {
            TimeText = DailyTime;
            TimeError = null;
            EditingTime = true;
        });
        SaveTimeCommand = new RelayCommand(SaveTime);
        FinishCommand = new AsyncRelayCommand(FinishAsync, () => !IsFinishing);
        AddGameCommand = new RelayCommand(() => _actions.AddGame?.Invoke());
        BuildSteps();
    }

    /// <summary>Something missing?: Add a game or folder, for one of the person's own (LIB-13).</summary>
    public ICommand AddGameCommand { get; }

    public bool CanAddGame => _actions.AddGame is not null;

    /// <summary>
    /// After Add a game or folder: the groups again, every tick as the person left it, and the group holding the game just
    /// added opened in full, so it shows, ticked.
    /// </summary>
    public async Task RegroupAsync(GameSync.Core.Model.GameId? added = null)
    {
        var ticked = Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Game.Id, r => r.IsChecked);
        var groups = await _actions.Groups(CancellationToken.None);
        Groups = groups.Select(g => new SetupGroupViewModel(g, Recount)).ToList();
        foreach (var row in Groups.SelectMany(g => g.Rows).Where(r => r.Tickable && ticked.ContainsKey(r.Game.Id)))
        {
            row.IsChecked = ticked[row.Game.Id];
        }

        if (added is { } id && Groups.FirstOrDefault(g => g.Rows.Any(r => r.Game.Id == id)) is { } group)
        {
            group.IsOpen = true;
            group.ShowsAll = true;
        }

        AntiCheatNote = AntiCheat(groups);
        Recount();
    }

    public CloudSetupViewModel Cloud { get; }

    /// <summary>Glossy's glow over the last-played game's art, like Settings (LOOK-17).</summary>
    public GlassStrength Strength => GlassStrength.Glow;

    public bool IsScan => Step == "scan";

    public bool IsChoose => Step == "choose";

    public bool IsCloud => Step == "cloud";

    public bool IsDaily => Step == "daily";

    public bool ScanDone => !IsScanning;

    /// <summary>Choose games waits for the scan.</summary>
    public bool CanChoose => !IsScanning && ScanError is null;

    public bool HasScanError => ScanError is not null;

    public bool HasAntiCheat => AntiCheatNote is not null;

    public bool NotEditingTime => !EditingTime;

    public bool HasTimeError => TimeError is not null;

    public bool HasFinishError => FinishError is not null;

    public string DailyTitle => DailyOn ? $"Back up every day at {DailyTime}" : "Back up every day";

    public string CloudNextLabel => Cloud.IsConnected ? "Continue" : "Skip for now";

    public bool CloudNextIsMain => Cloud.IsConnected;

    public string FinishLabel => IsFinishing ? "Setting up…" : "Start using GameSync";

    public ICommand NextCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ScanAgainCommand { get; }

    public ICommand EditTimeCommand { get; }

    public ICommand SaveTimeCommand { get; }

    public ICommand FinishCommand { get; }

    /// <summary>The games ticked, as Start using GameSync takes them.</summary>
    public IReadOnlyList<SetupGameRow> Chosen => Groups.SelectMany(g => g.Rows).Where(r => r.Tickable && r.IsChecked).ToList();

    /// <summary>Starts the scan once the page opens.</summary>
    public void Start()
    {
        if (!_started)
        {
            _started = true;
            _ = ScanAsync();
        }
    }

    /// <summary>Add a game folder: picked in Windows' folder picker, checked, then the PC is scanned again with it.</summary>
    public async void AddFolder(string folder)
    {
        if (IsScanning)
        {
            return;
        }

        try
        {
            if (await _actions.AddGameFolder(folder, CancellationToken.None) is { } refused)
            {
                ScanError = refused;
                return;
            }
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ScanError = e.Message;
            return;
        }

        await ScanAsync();
    }

    public async Task ScanAsync()
    {
        IsScanning = true;
        ScanError = null;
        ScanValue = 2;
        ScanRight = "";
        var progress = new Progress<ScanProgress>(p =>
        {
            if (!IsScanning)
            {
                // A report that arrives after the scan has finished.
                return;
            }

            ScanLabel = p.Total == 0 ? p.Stage : $"{p.Stage}: {p.Done.ToString(CultureInfo.InvariantCulture)} of {p.Total.ToString(CultureInfo.InvariantCulture)}";
            ScanValue = p.Total == 0 ? 2 : Math.Max(2, 100.0 * p.Done / p.Total);
            ScanRight = p.Total == 0 ? "" : $"{Math.Round(ScanValue).ToString(CultureInfo.InvariantCulture)}%";
            if (p.Folders is { } folders)
            {
                Folders = folders.Select(f => new FolderItem(f, "looking…")).ToList();
            }

            if (p.Stores is { } stores)
            {
                Stores = Rows(stores);
            }
        });
        try
        {
            var scan = await _actions.Scan(progress, CancellationToken.None);
            Stores = Rows(scan.Stores);
            Folders = scan.Folders.Select(f => new FolderItem(f.Path, f.Missing ? "not there right now" : Count(f.Games))).ToList();
            var groups = await _actions.Groups(CancellationToken.None);
            Groups = groups.Select(g => new SetupGroupViewModel(g, Recount)).ToList();
            ScanLabel = $"Scan finished in {Seconds(scan.Took)}: {Count(scan.Games)}, saves found for {scan.WithSaves.ToString(CultureInfo.InvariantCulture)}";
            ScanValue = 100;
            ScanRight = "100%";
            ChooseSubtitle = $"{Count(scan.Games)} found. Ticked games are backed up now and synced from then on; you can change this any time.";
            AntiCheatNote = AntiCheat(groups);
            Recount();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ScanError = $"The scan stopped before it finished: {e.Message}";
            ScanValue = 0;
            ScanRight = "";
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnStepChanged(string value)
    {
        var at = Index(value);
        if (at > _reached)
        {
            // Only steps already reached can be picked in the list; the rest wait for Continue.
            Dispatcher.UIThread.Post(() => Step = StepList[_reached].Id);
            return;
        }

        BuildSteps();
    }

    private void Next()
    {
        var at = Index(Step);
        if (at == 0 && !CanChoose)
        {
            return;
        }

        if (at < StepList.Count - 1)
        {
            _reached = Math.Max(_reached, at + 1);
            Step = StepList[at + 1].Id;
        }
    }

    private void Back()
    {
        var at = Index(Step);
        if (at > 0)
        {
            Step = StepList[at - 1].Id;
        }
    }

    private void SaveTime()
    {
        if (!FirstRun.TryParseTime(TimeText, out var at))
        {
            TimeError = "Type a time like 20:00, in 24-hour time.";
            return;
        }

        DailyTime = FirstRun.Time(at);
        TimeError = null;
        EditingTime = false;
    }

    private async Task FinishAsync()
    {
        if (EditingTime)
        {
            SaveTime();
            if (EditingTime)
            {
                return;
            }
        }

        IsFinishing = true;
        FinishError = null;
        try
        {
            FirstRun.TryParseTime(DailyTime, out var at);
            await _actions.Finish(new SetupChoice
            {
                Remote = Cloud.Remote,
                Games = Chosen.Select(r => r.Game.Id).ToList(),
                StartAtSignIn = StartAtSignIn,
                DailyAt = DailyOn ? at : null,
            }, CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            FinishError = $"GameSync couldn't finish setting up: {e.Message}";
        }
        finally
        {
            IsFinishing = false;
        }
    }

    /// <summary>The Ready card: how many games sync and are backed up, and the first backup's size.</summary>
    private void Recount()
    {
        var chosen = Chosen;
        var store = chosen.Count(r => Groups.Any(g => g.Kind == SetupGroupKind.StoreCloud && g.Rows.Contains(r)));
        var syncing = chosen.Count - store;
        var sync = $"{Count(syncing)} {(syncing == 1 ? "syncs" : "sync")}";
        var backup = $"{(store == 1 ? "1 is" : $"{store.ToString(CultureInfo.InvariantCulture)} are")} backed up";
        ReadyText = chosen.Count == 0
            ? "No games are ticked, so GameSync starts with nothing to sync. Any game's page can sync its saves later."
            : $"{(store == 0 ? sync : syncing == 0 ? $"{Count(store)} {(store == 1 ? "is" : "are")} backed up" : $"{sync} and {backup}")}. Your saves stay in their folders; GameSync keeps copies.";
        var size = FirstRun.Size(chosen.Sum(r => r.Game.Bytes));
        FirstBackupText = Cloud.IsConnected
            ? $"The first backup, about {size}, runs in the background once you start. You can play meanwhile."
            : $"The first backup, about {size}, stays on this PC until you connect a cloud. You can play meanwhile.";
        OnPropertyChanged(nameof(Chosen));
    }

    private void BuildSteps()
    {
        var at = Index(Step);
        Steps = StepList.Select((s, i) => new NavItem(s.Id, s.Label, i < at ? "check" : s.Icon)).ToList();
    }

    private static int Index(string step) => Math.Max(0, StepList.ToList().FindIndex(s => s.Id == step));

    /// <summary>ONB-05: the games found that ship an anti-cheat, by name.</summary>
    private static string? AntiCheat(IReadOnlyList<SetupGroup> groups)
    {
        var games = groups.Where(g => g.Kind != SetupGroupKind.NotInstalled).SelectMany(g => g.Games).Where(g => g.AntiCheat is not null).ToList();
        return games.Count == 0 ? null
            : $"{SetupGroupViewModel.Names(games, 4)} {(games.Count == 1 ? "has" : "have")} an anti-cheat: {(games.Count == 1 ? "it launches" : "they launch")} only through {(games.Count == 1 ? "its" : "their")} own launcher, and learn mode never runs on {(games.Count == 1 ? "it" : "them")}.";
    }

    private static string Count(int games) => games == 1 ? "1 game" : $"{games.ToString(CultureInfo.InvariantCulture)} games";

    private static List<StoreRow> Rows(IReadOnlyList<StoreSummary> stores) =>
        stores.Select(s => new StoreRow(s.Name, s.Where, s.Games == 0 ? "0" : Count(s.Games))).ToList();

    private static string Seconds(TimeSpan took) =>
        took.TotalSeconds < 1.5 ? "a second" : took.TotalMinutes < 1.5 ? $"{Math.Round(took.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds" : $"{Math.Round(took.TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes";
}
