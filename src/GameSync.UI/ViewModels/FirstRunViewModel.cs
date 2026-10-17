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

    /// <summary>LIB-30: What's in a folder over first run, for the person to pick from; null when they closed it.</summary>
    public Func<string, Task<FolderPicks?>>? PickGames { get; init; }

    /// <summary>LIB-30: what the person picked from a folder, kept once the scan with it has finished.</summary>
    public Func<FolderPicks, CancellationToken, Task>? Pick { get; init; }
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
/// The cloud's two ways (design system → OnboardingScreen's Where saves are kept, ConnectCloudDialog): Sign in with
/// Google, or a folder (a NAS, a USB drive, another disk). What's chosen becomes games.json's cloud: "drive", the folder,
/// or none. First run (ONB-07, design system version 56) makes it a choice of three, Only on this PC, for now the third,
/// and goes on only once one is made (<see cref="Picked"/>, <see cref="Ready"/>).
/// </summary>
public sealed partial class CloudSetupViewModel : ObservableObject
{
    public const string Drive = "drive";

    public const string InFolder = "folder";

    public const string Local = "local";

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

    /// <summary>ONB-07: first run's choice, <c>drive</c>, <c>folder</c> or <c>local</c>; null until one is made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDrivePicked), nameof(IsFolderPicked), nameof(IsLocalPicked), nameof(Ready), nameof(Remote), nameof(NotReadyTip), nameof(IsPicked))]
    private string? _picked;

    public CloudSetupViewModel(CloudActions actions)
    {
        _actions = actions;
        if (actions.SignedIn)
        {
            _choice = Drive;
        }

        SignInCommand = new AsyncRelayCommand(SignInAsync, () => CanSignIn && !IsSigningIn);
        PickCommand = new RelayCommand<string>(choice => Picked = choice is Drive or InFolder or Local ? choice : null);
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

    public bool IsDrivePicked => Picked == Drive;

    public bool IsFolderPicked => Picked == InFolder;

    public bool IsLocalPicked => Picked == Local;

    public bool IsPicked => Picked is not null;

    /// <summary>ONB-07: first run goes on once the choice is whole: signed in, a folder chosen, or this PC only.</summary>
    public bool Ready => Picked switch
    {
        Drive => IsSignedIn,
        InFolder => HasFolder,
        Local => true,
        _ => false,
    };

    /// <summary>Why Continue waits, under the pointer.</summary>
    public string? NotReadyTip => Picked switch
    {
        null => "Choose where your saves are kept first",
        Drive when !IsSignedIn => "Sign in with Google first",
        InFolder when !HasFolder => "Choose the folder first",
        _ => null,
    };

    /// <summary>A choice in first run's Where saves are kept: <c>drive</c>, <c>folder</c> or <c>local</c>.</summary>
    public ICommand PickCommand { get; }

    /// <summary>Sign in with Google leads until a folder is chosen; then Continue does, and it steps down. It never leads when it can't work.</summary>
    public bool SignInIsMain => !HasFolder && CanSignIn;

    public bool HasError => Error is not null;

    public string SignedInTitle => Account is { Length: > 0 } account ? $"Signed in as {account}" : "Signed in";

    public string FolderTitle => $"Saves go to {Folder}";

    public string SignInLabel => IsSigningIn ? "Waiting for your browser…" : "Sign in with Google";

    /// <summary>games.json's cloud: "drive", the folder, or "none"; Only on this PC in first run is none, whatever else was done.</summary>
    public string Remote => Picked == Local ? AppConfig.NoCloud : Choice switch
    {
        Drive => Drive,
        "folder" when Folder is { } folder => folder,
        _ => AppConfig.NoCloud,
    };

    partial void OnChoiceChanged(string value)
    {
        OnPropertyChanged(nameof(Ready));
        OnPropertyChanged(nameof(NotReadyTip));
    }

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
            Picked = InFolder;
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
            Picked = Drive;
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

    /// <summary>The path in full, and for a live save beside copies kept by hand, what becomes of them (KAN-61).</summary>
    public string SavePathTip => Game.Kept is { } kept ? $"{SavePath}\n{kept}" : SavePath;

    public string FoundBy => Game.FoundBy ?? "";

    /// <summary>ONB-07: what its saves come to now ("4.2 MB"); nothing when there's none to back up.</summary>
    public string Size => Game.Bytes > 0 ? Cli.FormatSize(Game.Bytes) : "";

    /// <summary>It has a box: every group but No saves found yet, whose games are watched instead.</summary>
    public bool Tickable { get; }

    partial void OnIsCheckedChanged(bool value) => _changed();

    public override string ToString() => Tickable ? $"{Title}, {(IsChecked ? "backed up" : "not backed up")}{(Size.Length > 0 ? $", {Size} of saves" : "")}" : $"{Title}, watched the first time you play";
}

/// <summary>
/// A group of first run's Choose saves (ONB-02, ONB-07): a box for the whole group, its name, how many, their saves' size
/// and why, and its games, the first four at a time. Its heading opens and closes it (a friend: "There is no option to
/// collapse opened games"). No saves found yet has a clock instead of a box: its games are watched the first time
/// they're played.
/// </summary>
public sealed partial class SetupGroupViewModel : ObservableObject
{
    public const int Shown = 4;

    private readonly Action _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows), nameof(HasMore), nameof(ShowsOpen), nameof(Chevron), nameof(ToggleOpenName))]
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
        ToggleOpenCommand = new RelayCommand(() => IsOpen = !IsOpen, () => Rows.Count > 0 && Tickable);
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

    public string Summary => $"{(Rows.Count == 1 ? "1 game" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} games")}{(Bytes > 0 && Kind != SetupGroupKind.OnlineOnly ? $" · {Cli.FormatSize(Bytes)} of saves" : "")} · {Why}";

    /// <summary>What its games' saves come to.</summary>
    public long Bytes => Rows.Sum(r => r.Game.Bytes);

    /// <summary>The heading's chevron: down while open.</summary>
    public string Chevron => IsOpen ? "chevronDown" : "chevronRight";

    /// <summary>The heading, as a screen reader says it: "Close Back up and sync".</summary>
    public string ToggleOpenName => $"{(IsOpen ? "Close" : "Open")} {Title}";

    /// <summary>The heading: opens the group, or closes it.</summary>
    public ICommand ToggleOpenCommand { get; }

    /// <summary>The heading opens and closes it: a group with games to tick.</summary>
    public bool CanOpen => Rows.Count > 0 && Tickable;

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
        SetupGroupKind.Sync => ("Back up and sync", "GameSync syncs them between your PCs"),
        SetupGroupKind.StoreCloud => ("Synced by Steam or Epic already", "their store syncs them; GameSync keeps every version too"),
        SetupGroupKind.OnlineOnly => ("Probably online-only", $"only settings found: the game keeps your progress on its servers · {Names(games, 2)}"),
        SetupGroupKind.NotInstalled => ("Saves of games not installed", $"kept safe, ready if you install them again · {Names(games, 1)}"),
        _ => ("No saves found yet", $"{Names(games, 3)} · GameSync watches them the first time you play"),
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
/// First run (design system → OnboardingScreen, version 56; ONB-01 to ONB-07): five steps down the left, Welcome, Your
/// games, Choose saves, Where saves are kept, then Backups and startup, with no side rail until it's done. It says first
/// that GameSync keeps saves, not games; asks for game folders before anything else; and goes on from Where saves are
/// kept only once a choice is made (a friend clicked Next through every step, 8 Oct 2026). The scan starts when the page
/// opens and only reads; nothing is copied until Start using GameSync.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject, IPageSurface, IWholeWindowPage
{
    public static readonly IReadOnlyList<(string Id, string Label, string Icon)> StepList =
    [
        ("welcome", "Welcome", "logo"),
        ("folders", "Your games", "folder"),
        ("saves", "Choose saves", "saves"),
        ("cloud", "Where saves are kept", "cloud"),
        ("daily", "Backups and startup", "clock"),
    ];

    private readonly SetupActions _actions;
    private int _reached;
    private bool _started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsFolders), nameof(IsChoose), nameof(IsCloud), nameof(IsDaily))]
    private string _step = "welcome";

    [ObservableProperty]
    private IReadOnlyList<NavItem> _steps = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoose), nameof(ScanDone), nameof(SavesTitle), nameof(CanContinueSaves))]
    private bool _isScanning;

    [ObservableProperty]
    private double _scanValue;

    [ObservableProperty]
    private string _scanLabel = "Getting ready to scan";

    [ObservableProperty]
    private string _scanRight = "";

    /// <summary>KAN-80: the scan as a job: running (the stripe slides until there's a count), done, or failed.</summary>
    [ObservableProperty]
    private string _scanState = "running";

    [ObservableProperty]
    private string _scanTitle = "Scanning this PC";

    /// <summary>The bar: null while there's nothing to count yet.</summary>
    [ObservableProperty]
    private double? _scanBar;

    [ObservableProperty]
    private IReadOnlyList<StoreRow> _stores = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolders), nameof(NoFolders), nameof(SetupFoldersLine))]
    private IReadOnlyList<FolderItem> _folders = [];

    /// <summary>ONB-07: the title of Choose saves, "Saves found for 43 of 46 games", once the scan has finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesTitle))]
    private string _foundTitle = "";

    /// <summary>ONB-07: how many games are ticked and what their saves come to, above the groups.</summary>
    [ObservableProperty]
    private string _tickedText = "";

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

    /// <summary>ONB-07: Your setup, the games line ("31 games' saves").</summary>
    [ObservableProperty]
    private string _setupGamesTitle = "";

    [ObservableProperty]
    private string _setupGamesLine = "";

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
            if (e.PropertyName is nameof(CloudSetupViewModel.IsConnected) or nameof(CloudSetupViewModel.Picked) or nameof(CloudSetupViewModel.Ready))
            {
                OnPropertyChanged(nameof(CloudNextLabel));
                OnPropertyChanged(nameof(CloudNextIsMain));
                OnPropertyChanged(nameof(SetupKeptLine));
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
        GoToCommand = new RelayCommand<string>(step =>
        {
            if (step is not null && Index(step) <= _reached)
            {
                Step = step;
            }
        });
        CloseGroupsCommand = new RelayCommand(() =>
        {
            foreach (var group in Groups)
            {
                group.IsOpen = false;
            }
        });
        BuildSteps();
    }

    /// <summary>Your setup's Change: back to that step.</summary>
    public ICommand GoToCommand { get; }

    /// <summary>Choose saves' Close every group.</summary>
    public ICommand CloseGroupsCommand { get; }

    public bool IsWelcome => Step == "welcome";

    public bool IsFolders => Step == "folders";

    public bool HasFolders => Folders.Count > 0;

    /// <summary>ONB-07: no game folder yet: Your games says what that costs, and going on without one is its own button.</summary>
    public bool NoFolders => Folders.Count == 0;

    /// <summary>Choose saves' title: what was found, or that it's still looking.</summary>
    public string SavesTitle => IsScanning || FoundTitle.Length == 0 ? "Looking for saves…" : FoundTitle;

    /// <summary>Choose saves goes on once every game has been looked at.</summary>
    public bool CanContinueSaves => !IsScanning && ScanError is null;

    /// <summary>Your setup, the game folders line.</summary>
    public string SetupFoldersLine => Folders.Count == 0
        ? "None: only games from Steam, Epic and the EA app are found."
        : string.Join(", ", Folders.Select(f => f.Path));

    /// <summary>Your setup, where saves are kept.</summary>
    public string SetupKeptLine => Cloud.Picked switch
    {
        CloudSetupViewModel.Local => "On this PC only: not synced to your other PCs",
        CloudSetupViewModel.InFolder when Cloud.Folder is { } folder => $"On this PC and in {folder}",
        _ => "On this PC and in your Google Drive",
    };

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

    public bool IsChoose => Step == "saves";

    public bool IsCloud => Step == "cloud";

    public bool IsDaily => Step == "daily";

    public bool ScanDone => !IsScanning;

    /// <summary>Choose games waits for the scan.</summary>
    public bool CanChoose => !IsScanning && ScanError is null;

    public bool HasScanError => ScanError is not null;

    /// <summary>
    /// Why a game folder wasn't added (the owner's friend, 8 Oct 2026): said under the folders, without stopping Choose
    /// games, as the scan itself went fine.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderError))]
    private string? _folderError;

    public bool HasFolderError => FolderError is not null;

    /// <summary>Folders picked while the scan ran: each is added once it has finished, then the PC is scanned again with it.</summary>
    private readonly Queue<(string Folder, FolderPicks? Picks)> _waitingFolders = new();

    public bool HasAntiCheat => AntiCheatNote is not null;

    public bool NotEditingTime => !EditingTime;

    public bool HasTimeError => TimeError is not null;

    public bool HasFinishError => FinishError is not null;

    public string DailyTitle => DailyOn ? $"Back up every day at {DailyTime}" : "Back up every day";

    /// <summary>ONB-07: Continue, or Continue with this PC only; it waits until a choice is made.</summary>
    public string CloudNextLabel => Cloud.IsLocalPicked ? "Continue with this PC only" : "Continue";

    public bool CloudNextIsMain => true;

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

    /// <summary>
    /// Add a game folder: picked in Windows' folder picker, then What's in it (LIB-30) for the person to pick the games,
    /// then the PC is scanned again with it and what they left out stays out.
    /// </summary>
    public async void AddFolder(string folder)
    {
        FolderError = null;
        FolderPicks? picks = null;
        if (_actions.PickGames is { } pickGames)
        {
            try
            {
                picks = await pickGames(folder);
            }
            catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                FolderError = e.Message;
                return;
            }

            if (picks is null)
            {
                // Closed without adding.
                return;
            }
        }

        if (IsScanning)
        {
            // The owner's friend, 8 Oct 2026: a folder picked while the first scan ran was dropped without a word. It waits
            // for the scan, shown in the list meanwhile.
            _waitingFolders.Enqueue((folder, picks));
            Folders = [.. Folders, new FolderItem(folder, "added once this scan finishes")];
            return;
        }

        await AddPickedAsync(folder, picks);
    }

    private async Task AddPickedAsync(string folder, FolderPicks? picks)
    {
        FolderError = null;
        try
        {
            if (await _actions.AddGameFolder(folder, CancellationToken.None) is { } refused)
            {
                FolderError = refused;
                return;
            }
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FolderError = e.Message;
            return;
        }

        if (picks is not null)
        {
            _picks[picks.Folder] = picks;
        }

        await ScanAsync();
    }

    /// <summary>LIB-30: what was picked from each folder added here, kept after every scan and said beside the folder.</summary>
    private readonly Dictionary<string, FolderPicks> _picks = new(StringComparer.OrdinalIgnoreCase);

    public async Task ScanAsync()
    {
        IsScanning = true;
        ScanError = null;
        ScanValue = 2;
        ScanRight = "";
        (ScanState, ScanTitle, ScanBar) = ("running", "Scanning this PC", null);
        var progress = new Progress<ScanProgress>(p =>
        {
            if (!IsScanning)
            {
                // A report that arrives after the scan has finished.
                return;
            }

            ScanLabel = p.Total == 0 ? p.Stage : $"{p.Stage}: {p.Done.ToString(CultureInfo.InvariantCulture)} of {p.Total.ToString(CultureInfo.InvariantCulture)}";
            ScanValue = p.Total == 0 ? 2 : Math.Max(2, 100.0 * p.Done / p.Total);
            ScanBar = p.Total == 0 ? null : ScanValue;
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
            if (_actions.Pick is { } pick)
            {
                foreach (var picked in _picks.Values)
                {
                    await pick(picked, CancellationToken.None);
                }
            }

            Folders = scan.Folders.Select(f => new FolderItem(f.Path, f.Missing ? "not there right now" : FolderMeta(f))).ToList();
            var groups = await _actions.Groups(CancellationToken.None);
            Groups = groups.Select(g => new SetupGroupViewModel(g, Recount)).ToList();
            ScanLabel = $"Scan finished in {Seconds(scan.Took)}: {Count(scan.Games)}, saves found for {scan.WithSaves.ToString(CultureInfo.InvariantCulture)}";
            ScanValue = 100;
            ScanRight = "100%";
            (ScanState, ScanTitle, ScanBar) = ("done", "Scan finished", 100);
            FoundTitle = $"Saves found for {scan.WithSaves.ToString(CultureInfo.InvariantCulture)} of {Count(scan.Games)}";
            ChooseSubtitle = "These are save files, your progress in each game, usually a few megabytes. GameSync keeps a copy of the ticked ones when you finish. Your games aren't copied.";
            AntiCheatNote = AntiCheat(groups);
            Recount();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ScanError = $"The scan stopped before it finished: {e.Message}";
            ScanValue = 0;
            ScanRight = "";
            (ScanState, ScanTitle, ScanLabel) = ("failed", "The scan stopped", "Nothing was changed. Scan again tries once more.");
        }
        finally
        {
            IsScanning = false;
        }

        if (_waitingFolders.TryDequeue(out var waiting))
        {
            await AddPickedAsync(waiting.Folder, waiting.Picks);
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
        if ((Step == "saves" && !CanContinueSaves) || (Step == "cloud" && !Cloud.Ready))
        {
            // ONB-07: Choose saves waits for the scan, and Where saves are kept for a choice.
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
        var bytes = chosen.Sum(r => r.Game.Bytes);
        TickedText = $"{Count(chosen.Count)} ticked · {FirstRun.Size(bytes)} of saves";
        SetupGamesTitle = chosen.Count == 1 ? "1 game's saves" : $"{chosen.Count.ToString(CultureInfo.InvariantCulture)} games' saves";
        SetupGamesLine = chosen.Count == 0
            ? "None ticked: any game's page can back up its saves later."
            : $"Backed up when you start, {FirstRun.Size(bytes)}, then each time you stop playing.";
        var store = chosen.Count(r => Groups.Any(g => g.Kind == SetupGroupKind.StoreCloud && g.Rows.Contains(r)));
        var syncing = chosen.Count - store;
        var sync = $"{Count(syncing)} {(syncing == 1 ? "syncs" : "sync")}";
        var backup = $"{(store == 1 ? "1 is" : $"{store.ToString(CultureInfo.InvariantCulture)} are")} backed up";
        ReadyText = chosen.Count == 0
            ? "No games are ticked, so GameSync starts with nothing to sync. Any game's page can sync its saves later."
            : $"{(store == 0 ? sync : syncing == 0 ? $"{Count(store)} {(store == 1 ? "is" : "are")} backed up" : $"{sync} and {backup}")}. Your saves stay in their folders; GameSync keeps copies.";
        var size = FirstRun.Size(chosen.Sum(r => r.Game.Bytes));
        FirstBackupText = Cloud.IsConnected && !Cloud.IsLocalPicked
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

    /// <summary>A game folder's line: its games, and what the person left out there ("5 games · 4 left out").</summary>
    private string FolderMeta(GameFolderSummary folder) =>
        _picks.TryGetValue(folder.Path, out var picked) && picked.LeftOutNames.Count > 0
            ? $"{Count(folder.Games)} · {picked.LeftOutNames.Count.ToString(CultureInfo.InvariantCulture)} left out"
            : Count(folder.Games);

    private static List<StoreRow> Rows(IReadOnlyList<StoreSummary> stores) =>
        stores.Select(s => new StoreRow(s.Name, s.Where, s.Games == 0 ? "0" : Count(s.Games))).ToList();

    private static string Seconds(TimeSpan took) =>
        took.TotalSeconds < 1.5 ? "a second" : took.TotalMinutes < 1.5 ? $"{Math.Round(took.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds" : $"{Math.Round(took.TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes";
}
