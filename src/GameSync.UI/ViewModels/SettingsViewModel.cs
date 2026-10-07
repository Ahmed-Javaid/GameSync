using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Games;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>What a setting's change came to, in a sentence for the page, and whether it failed.</summary>
public sealed record Outcome(string Text, bool Failed = false);

/// <summary>
/// What Settings asks of the app (SET-01): reading the settings, and each change. The app runs them off the UI thread
/// and says what went wrong in the <see cref="Outcome"/>; the defaults do nothing, for renders and tests.
/// </summary>
public sealed record SettingsActions
{
    public Func<CancellationToken, Task<SettingsView?>> Read { get; init; } = _ => Task.FromResult<SettingsView?>(null);

    public Func<CancellationToken, Task<BackupFigures?>> Figures { get; init; } = _ => Task.FromResult<BackupFigures?>(null);

    public Func<CancellationToken, Task<CloudDetails?>> Cloud { get; init; } = _ => Task.FromResult<CloudDetails?>(null);

    /// <summary>The look, saved on this PC and applied at once (LOOK-07).</summary>
    public Action<Look> SetLook { get; init; } = _ => { };

    /// <summary>Windows' app mode (light or not) and accent colour, for Match Windows and the Windows accent theme.</summary>
    public Func<(bool Light, string? Accent)> Windows { get; init; } = () => (false, null);

    public Func<string, IProgress<(int Done, int Total)>, Task<Outcome>> MoveBackupFolder { get; init; } = (_, _) => Task.FromResult(new Outcome(""));

    public Func<bool, Task<Outcome>> SetKeepEverything { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<string, Task<Outcome>> AddGameFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<string, Task<Outcome>> RemoveGameFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<string, Task<Outcome>> AddSaveFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<string, Task<Outcome>> RemoveSaveFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    /// <summary>KAN-123: a folder where copies Steam doesn't run keep their records of what's unlocked, added or taken off.</summary>
    public Func<string, Task<Outcome>> AddAchievementFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<string, Task<Outcome>> RemoveAchievementFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    /// <summary>FOLD-09: the folder shared zips go to, and asking each time instead.</summary>
    public Func<string, Task<Outcome>> SetShareFolder { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<bool, Task> SetShareAsk { get; init; } = _ => Task.CompletedTask;

    public Func<bool, Task<Outcome>> SetStartAtSignIn { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<TimeOnly?, Task<Outcome>> SetDaily { get; init; } = _ => Task.FromResult(new Outcome(""));

    public Func<Task<Outcome>> RunDailyNow { get; init; } = () => Task.FromResult(new Outcome(""));

    public Func<GameDefaults, Task> SetDefaults { get; init; } = _ => Task.CompletedTask;

    /// <summary>The defaults applied to the games already syncing: their files, who wins, or both (SET-06).</summary>
    public Func<bool, bool, Task<Outcome>> ApplyDefaults { get; init; } = (_, _) => Task.FromResult(new Outcome(""));

    public Action ConnectCloud { get; init; } = () => { };

    public Func<Task<Outcome>> SignIn { get; init; } = () => Task.FromResult(new Outcome(""));

    public Func<Task<Outcome>> SignOut { get; init; } = () => Task.FromResult(new Outcome(""));

    public Action<string> OpenLink { get; init; } = _ => { };

    public Action<string> OpenFolder { get; init; } = _ => { };

    public Func<string, Task<Outcome>> RenamePc { get; init; } = _ => Task.FromResult(new Outcome(""));

    /// <summary>BG-06 and BG-05: hold notifications while a fullscreen game runs; the daily backup's line. Null leaves one as it is.</summary>
    public Func<bool?, bool?, Task> SetNotifications { get; init; } = (_, _) => Task.CompletedTask;

    public Func<CancellationToken, Task<string>> Diagnostics { get; init; } = _ => Task.FromResult("");

    /// <summary>ACH-09: the popup, its sound and its corner; null leaves one as it is.</summary>
    public Func<AchievementPopupChange, Task> SetAchievementPopups { get; init; } = _ => Task.CompletedTask;

    /// <summary>KAN-120: plays the chime in a sound at a volume, as Hear it and picking one do.</summary>
    public Action<string, string> HearChime { get; init; } = (_, _) => { };

    /// <summary>Try the popup, after the delay given (KAN-110).</summary>
    public Action<TimeSpan> TryPopup { get; init; } = _ => { };

    /// <summary>PKG-03 (design system version 53): Settings → Updates, read from this PC alone.</summary>
    public Func<CancellationToken, Task<UpdatesView?>> ReadUpdates { get; init; } = _ => Task.FromResult<UpdatesView?>(null);

    /// <summary>Check now: asks GitHub, and gets a newer GameSync ready; the app shows the download as it goes.</summary>
    public Func<Task<Outcome>> CheckUpdates { get; init; } = () => Task.FromResult(new Outcome(""));

    /// <summary>Restart to update: GameSync closes, installs it and opens again.</summary>
    public Func<Task<Outcome>> RestartToUpdate { get; init; } = () => Task.FromResult(new Outcome(""));

    public Func<bool, Task> SetUpdatesDaily { get; init; } = _ => Task.CompletedTask;

    /// <summary>KAN-110: every game GameSync tracks achievements for, counted or left out.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<AchievementGameRow>>> AchievementGames { get; init; } = _ => Task.FromResult<IReadOnlyList<AchievementGameRow>>([]);

    /// <summary>KAN-110: leaves a game out of the achievements, or counts it again.</summary>
    public Func<GameSync.Core.Model.GameId, bool, Task> SetAchievementsLeftOut { get; init; } = (_, _) => Task.CompletedTask;

    /// <summary>KAN-131: turns GameSync's popup on or off for a game.</summary>
    public Func<GameSync.Core.Model.GameId, bool, Task> SetAchievementPopup { get; init; } = (_, _) => Task.CompletedTask;

    /// <summary>A dialog over the page (Achievements by game), and closing it.</summary>
    public Action<object> OpenDialog { get; init; } = _ => { };

    public Action CloseDialog { get; init; } = () => { };
}

/// <summary>
/// Settings (SET-01; design system → SettingsScreen): the sections down the left, one at a time on the right, and the
/// version with Copy diagnostics at the foot of the column. Everything is kept on this PC. The page lives as long as
/// the window, so the section open and anything half typed stay when the app refreshes.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IPageSurface
{
    public const string AppearanceId = "appearance";
    public const string StorageId = "storage";
    public const string BackupId = "backup";
    public const string CloudId = "cloud";
    public const string DevicesId = "devices";
    public const string NotificationsId = "notifications";
    public const string AchievementsId = "achievements";
    public const string SafetyId = "safety";
    public const string UpdatesId = "updates";

    private readonly SettingsActions _actions;
    private bool _reading;
    private bool _readAgain;

    public SettingsViewModel(SettingsActions actions, Look look, string? section = null)
    {
        _actions = actions;
        Appearance = new AppearanceSettings(actions, look);
        Storage = new StorageSettings(actions, Reload);
        Backup = new BackupSettings(actions, Reload);
        Cloud = new CloudSettings(actions, Reload);
        Devices = new DevicesSettings(actions, Reload);
        Notifications = new NotificationSettings(actions);
        Achievements = new AchievementSettings(actions);
        Safety = new SafetySettings();
        Updates = new UpdateSettings(actions);
        _section = section is not null && Nav(null).Any(s => s.Id == section) ? section : AppearanceId;
        _sections = Nav(null);
    }

    public GlassStrength Strength => GlassStrength.Glow;

    public AppearanceSettings Appearance { get; }

    public StorageSettings Storage { get; }

    public BackupSettings Backup { get; }

    public CloudSettings Cloud { get; }

    public DevicesSettings Devices { get; }

    public NotificationSettings Notifications { get; }

    public AchievementSettings Achievements { get; }

    public SafetySettings Safety { get; }

    public UpdateSettings Updates { get; }

    /// <summary>The sections, with a warn badge on one that needs the person: a backup drive not connected, Drive signed out.</summary>
    [ObservableProperty]
    private IReadOnlyList<NavItem> _sections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    private string _section;

    /// <summary>The section showing.</summary>
    public object Current => Section switch
    {
        StorageId => Storage,
        BackupId => Backup,
        CloudId => Cloud,
        DevicesId => Devices,
        NotificationsId => Notifications,
        AchievementsId => Achievements,
        SafetyId => Safety,
        UpdatesId => Updates,
        _ => Appearance,
    };

    /// <summary>"GameSync 1.0 · this PC is DESKTOP", at the foot of the column.</summary>
    [ObservableProperty]
    private string _footer = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyLabel), nameof(CopyIcon))]
    private bool _copied;

    public string CopyLabel => Copied ? "Copied" : "Copy diagnostics";

    public string CopyIcon => Copied ? "check" : "copy";

    /// <summary>The page puts the text on the clipboard.</summary>
    public event Action<string>? CopyText;

    /// <summary>Opens a section, from another page (Home's top bar opens Cloud or Devices).</summary>
    public void Show(string section)
    {
        if (Nav(null).Any(s => s.Id == section))
        {
            Section = section;
        }
    }

    partial void OnSectionChanged(string value)
    {
        if (value == CloudId)
        {
            Cloud.LoadDetails();
        }
        else if (value == AchievementsId)
        {
            Achievements.LoadGames();
        }
    }

    /// <summary>SET-05: the diagnostics go on the clipboard, and the button says Copied for a moment.</summary>
    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        string text;
        try
        {
            text = await _actions.Diagnostics(CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            text = $"GameSync couldn't gather its diagnostics: {e.Message}";
        }

        CopyText?.Invoke(text);
        Copied = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        Copied = false;
    }

    /// <summary>Reads every setting again: as the page opens, after a change, and when the app refreshes.</summary>
    public async void Reload()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;
        try
        {
            do
            {
                _readAgain = false;
                if (await _actions.Read(CancellationToken.None) is { } view)
                {
                    Apply(view);
                }

                if (await _actions.ReadUpdates(CancellationToken.None) is { } updates)
                {
                    Updates.Show(updates, DateTime.Now);
                }
            }
            while (_readAgain);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Storage.Note = new Outcome($"GameSync couldn't read its settings: {e.Message}", Failed: true);
        }
        finally
        {
            _reading = false;
        }
    }

    /// <summary>Everything the page shows: the settings, then what's slow to find out, the backup folder's figures and the cloud's.</summary>
    public void Open()
    {
        Reload();
        Storage.LoadFigures();
        if (Section == CloudId)
        {
            Cloud.LoadDetails();
        }
        else if (Section == AchievementsId)
        {
            Achievements.LoadGames();
        }
    }

    public void Apply(SettingsView view)
    {
        Storage.Show(view);
        Backup.Show(view);
        Cloud.Show(view);
        Devices.Show(view);
        Notifications.Show(view);
        Achievements.Show(view);
        Safety.Show(view);
        Footer = $"GameSync {view.AppVersion} · this PC is {view.ThisPc}";
        if (Nav(view) is var sections && !sections.SequenceEqual(Sections))
        {
            Sections = sections;
        }
    }

    private static IReadOnlyList<NavItem> Nav(SettingsView? view) =>
    [
        new(AppearanceId, "Appearance", "palette"),
        new(StorageId, "Storage and folders", "drive", view?.BackupMissing is not null ? "1" : null),
        new(BackupId, "Backup and sync", "sync"),
        new(CloudId, "Cloud", "cloud", view is { Cloud: SettingsData.Drive, SignedIn: false } ? "1" : null),
        new(DevicesId, "Devices", "monitor"),
        new(NotificationsId, "Notifications", "bell"),
        new(AchievementsId, "Achievements", "trophy"),
        new(SafetyId, "Safety", "shield"),
        new(UpdatesId, "Updates", "download"),
    ];

    /// <summary>"Today 20:00", "Yesterday 19:31", "23 Sep 20:00".</summary>
    public static string When(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime();
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return local.Date == nowLocal.Date ? $"Today {time}"
            : local.Date == nowLocal.Date.AddDays(-1) ? $"Yesterday {time}"
            : local.ToString(local.Year == nowLocal.Year ? "d MMM HH:mm" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Appearance (LOOK-01 to LOOK-07, LOOK-10, LOOK-17): the mode, the theme, pure black, the surface and the two colours.
/// Each change is saved on this PC and repaints the app at once.
/// </summary>
public sealed partial class AppearanceSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private bool _quiet;

    public AppearanceSettings(SettingsActions actions, Look look)
    {
        _actions = actions;
        Show(look);
    }

    public IReadOnlyList<NavItem> Modes { get; } = [new(Look.Dark, "Dark", "moon"), new(Look.Light, "Light", "sun"), new(Look.MatchWindows, "Match Windows", "monitor")];

    public IReadOnlyList<NavItem> Surfaces { get; } = [new(Look.Glossy, "Glossy"), new(Look.Solid, "Solid")];

    [ObservableProperty]
    private string _mode = Look.Dark;

    [ObservableProperty]
    private string _preset = "arcade";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryPick))]
    private string? _primary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondaryPick))]
    private string? _secondary;

    [ObservableProperty]
    private bool _pureBlack;

    [ObservableProperty]
    private string _surface = Look.Glossy;

    [ObservableProperty]
    private IReadOnlyList<ThemeCard> _themes = [];

    [ObservableProperty]
    private IReadOnlyList<SwatchCard> _primarySwatches = [];

    [ObservableProperty]
    private IReadOnlyList<SwatchCard> _secondarySwatches = [];

    /// <summary>The swatch the primary colour is: the person's own, or the theme's.</summary>
    public string? PrimaryPick
    {
        get => Primary ?? PresetOf(Preset).Primary;
        set
        {
            if (value is not null && value != PrimaryPick)
            {
                Primary = value == PresetOf(Preset).Primary ? null : value;
            }
        }
    }

    public string? SecondaryPick
    {
        get => Secondary ?? PresetOf(Preset).Secondary;
        set
        {
            if (value is not null && value != SecondaryPick)
            {
                Secondary = value == PresetOf(Preset).Secondary ? null : value;
            }
        }
    }

    /// <summary>Light, as Windows or the person chose it, which leaves pure black out.</summary>
    public bool IsLight => Resolved().Mode == ThemeMode.Light;

    public string ThemeNote => PresetOf(Preset) is { FollowsAccent: true } ? "Follows your Windows accent colour." : PresetOf(Preset).Note;

    /// <summary>The same in both modes since light mode has its own Glossy (design system version 35).</summary>
    public string SurfaceDescription =>
        "Glossy lets your game's art show through GameSync, frosted dark or light with the mode; Solid keeps every surface plain. Glossy turns off when Windows' transparency effects are off.";

    public string PureBlackDescription => IsLight ? "Dark mode only." : "For OLED screens: the page and side rail turn fully black, never glossy.";

    public bool CanPureBlack => !IsLight;

    /// <summary>What the person has now, as it's saved.</summary>
    public Look Look => new(Mode, PureBlack, Preset, Primary, Secondary, Surface);

    /// <summary>The look as it is on this PC, from the app or another change (<c>gamesync set surface</c>); nothing is saved.</summary>
    public void Show(Look look)
    {
        _quiet = true;
        Mode = look.Mode;
        Preset = look.Preset;
        Primary = look.Primary;
        Secondary = look.Secondary;
        PureBlack = look.PureBlack;
        Surface = look.Surface;
        _quiet = false;
        Rebuild();
    }

    /// <summary>Windows' mode or accent changed: the previews follow it.</summary>
    public void Refresh() => Rebuild();

    /// <summary>LOOK-10: Arcade, Dark, pure black off, and the theme's own colours; the surface stays.</summary>
    [RelayCommand]
    private void Reset()
    {
        _quiet = true;
        Mode = Look.Dark;
        Preset = "arcade";
        Primary = null;
        Secondary = null;
        PureBlack = false;
        _quiet = false;
        Changed();
    }

    partial void OnModeChanged(string value) => Changed();

    // Picking a preset resets any swatch the person chose, so the preset looks as designed (design system → ThemePicker).
    partial void OnPresetChanged(string value)
    {
        if (!_quiet)
        {
            _quiet = true;
            Primary = null;
            Secondary = null;
            _quiet = false;
        }

        Changed();
    }

    partial void OnPrimaryChanged(string? value) => Changed();

    partial void OnSecondaryChanged(string? value) => Changed();

    partial void OnPureBlackChanged(bool value) => Changed();

    partial void OnSurfaceChanged(string value) => Changed();

    private void Changed()
    {
        if (_quiet)
        {
            return;
        }

        Rebuild();
        _actions.SetLook(Look);
    }

    private void Rebuild()
    {
        var choice = Resolved();
        Themes = ThemeCard.For(choice.Mode, choice.Mode == ThemeMode.Dark && PureBlack, choice.Accent);
        PrimarySwatches = SwatchCard.For(choice, secondary: false);
        SecondarySwatches = SwatchCard.For(choice, secondary: true);
        OnPropertyChanged(nameof(PrimaryPick));
        OnPropertyChanged(nameof(SecondaryPick));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(ThemeNote));
        OnPropertyChanged(nameof(SurfaceDescription));
        OnPropertyChanged(nameof(PureBlackDescription));
        OnPropertyChanged(nameof(CanPureBlack));
    }

    private ThemeChoice Resolved()
    {
        var (light, accent) = _actions.Windows();
        return Look.Resolve(light, accent);
    }

    private static ThemePreset PresetOf(string id) => ThemeEngine.Presets.FirstOrDefault(p => p.Id == id) ?? ThemeEngine.Presets[0];
}

/// <summary>
/// Storage and folders (FOLD-02 to FOLD-08, FOLD-10): the backup folder with its move, how much history stays on this
/// PC, the folders GameSync looks in for games, and extra save folders.
/// </summary>
public sealed partial class StorageSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private readonly Action _reload;
    private IReadOnlyDictionary<FolderItem, string> _savePaths = new Dictionary<FolderItem, string>();
    private BackupFigures? _figures;
    private string? _missing;
    private bool _moving;

    public StorageSettings(SettingsActions actions, Action reload)
    {
        _actions = actions;
        _reload = reload;
    }

    public IReadOnlyList<NavItem> KeepChoices { get; } = [new("recent", "Recent"), new("all", "Everything")];

    [ObservableProperty]
    private string _backupFolder = "";

    [ObservableProperty]
    private string? _backupMeta;

    [ObservableProperty]
    private FolderState _backupState;

    [ObservableProperty]
    private string? _backupMessage;

    [ObservableProperty]
    private double _backupProgress;

    /// <summary>FOLD-06: "recent" or "all".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepDescription))]
    private string _keep = "recent";

    public string KeepDescription => Keep == "all"
        ? "A full copy of every version, as in the cloud."
        : "The last 10 versions of each game, up to 2 GB in total. The cloud keeps every version either way.";

    [ObservableProperty]
    private IReadOnlyList<FolderItem> _gameFolders = [];

    [ObservableProperty]
    private IReadOnlyList<FolderItem> _saveFolders = [];

    /// <summary>FOLD-09: where Share saves puts its zips.</summary>
    [ObservableProperty]
    private string _shareFolder = "";

    [ObservableProperty]
    private bool _shareAsk;

    /// <summary>A scan after a folder was added or removed is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotScanning))]
    private bool _scanning;

    public bool NotScanning => !Scanning;

    /// <summary>What the last change came to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private Outcome? _note;

    public bool HasNote => Note is not null;

    private bool _showing;

    public void Show(SettingsView view)
    {
        _missing = view.BackupMissing;
        if (!_moving)
        {
            BackupFolder = view.BackupFolder;
            ShowBackup();
        }

        _showing = true;
        Keep = view.KeepEverything ? "all" : "recent";
        ShareFolder = view.ShareFolder;
        ShareAsk = view.ShareAsks;
        _showing = false;
        var gameFolders = view.GameFolders.Select(f => new FolderItem(f.Path, f.Missing ? "Not there right now" : f.Games switch
        {
            0 => "No games found in it",
            1 => "1 game",
            _ => $"{f.Games} games",
        })).ToList();
        if (!gameFolders.SequenceEqual(GameFolders))
        {
            GameFolders = gameFolders;
        }

        var saves = view.SaveFolders.Select(f => (Item: new FolderItem(f.Full ?? f.Path,
            f.Missing ? "Not there right now" : f.ById ? "Subfolders are Steam app IDs" : "Watched for saves", f.ById ? "By game ID" : null), f.Path)).ToList();
        _savePaths = saves.GroupBy(s => s.Item).ToDictionary(g => g.Key, g => g.First().Path);
        if (!saves.Select(s => s.Item).SequenceEqual(SaveFolders))
        {
            SaveFolders = saves.Select(s => s.Item).ToList();
        }
    }

    /// <summary>The backup folder's size, versions and free space, which take a moment to count (FOLD-10).</summary>
    public async void LoadFigures()
    {
        try
        {
            _figures = await _actions.Figures(CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _figures = null;
        }

        if (!_moving)
        {
            ShowBackup();
        }
    }

    private void ShowBackup()
    {
        if (_missing is not null)
        {
            BackupState = FolderState.Missing;
            BackupMessage = _missing;
            BackupMeta = null;
            return;
        }

        if (BackupState == FolderState.Refused)
        {
            return;
        }

        BackupState = FolderState.Ok;
        BackupMessage = null;
        BackupMeta = _figures is { } f
            ? string.Join(" · ", new[]
            {
                Cli.FormatSize(f.Bytes),
                f.Versions == 1 ? "1 version" : $"{f.Versions.ToString("N0", CultureInfo.InvariantCulture)} versions",
                f.FreeBytes is { } free ? $"{Cli.FormatSize(free)} free on {f.Drive}" : null,
            }.OfType<string>())
            : null;
    }

    /// <summary>
    /// FOLD-02, FOLD-03: the folder picked in Change…; everything is copied there and checked before the old folder
    /// goes. A folder that isn't allowed says why, and the old one stays in use.
    /// </summary>
    public async Task MoveBackup(string picked)
    {
        if (_moving)
        {
            return;
        }

        _moving = true;
        BackupState = FolderState.Moving;
        BackupProgress = 0;
        BackupMessage = "Getting ready to move your backups…";
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (_moving)
            {
                BackupProgress = p.Total == 0 ? 100 : p.Done * 100.0 / p.Total;
                BackupMessage = $"Moving {p.Total.ToString("N0", CultureInfo.InvariantCulture)} files, checking each one before the old folder is removed";
            }
        });
        var outcome = await _actions.MoveBackupFolder(picked, progress);
        _moving = false;
        if (outcome.Failed)
        {
            BackupState = FolderState.Refused;
            BackupMessage = outcome.Text;
        }
        else
        {
            BackupState = FolderState.Ok;
            Note = new Outcome(outcome.Text);
        }

        _reload();
        LoadFigures();
    }

    [RelayCommand]
    private void OpenBackup() => _actions.OpenFolder(BackupFolder);

    [RelayCommand]
    private void OpenShareFolder() => _actions.OpenFolder(ShareFolder);

    /// <summary>FOLD-09: Change… on the shared zips' folder.</summary>
    public Task SetShareFolder(string folder) => Run(() => _actions.SetShareFolder(folder));

    partial void OnShareAskChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetShareAsk(value);
        }
    }

    partial void OnKeepChanged(string value)
    {
        if (!_showing)
        {
            _ = Run(() => _actions.SetKeepEverything(value == "all"));
        }
    }

    /// <summary>FOLD-07: Add folder in Game folders to scan; the PC is scanned with it.</summary>
    public Task AddGameFolder(string folder) => Run(() => _actions.AddGameFolder(folder), scan: true);

    [RelayCommand]
    private Task RemoveGameFolder(FolderItem item) => Run(() => _actions.RemoveGameFolder(item.Path), scan: true);

    /// <summary>FOLD-08: Add folder in Extra save folders; the PC is scanned with it.</summary>
    public Task AddSaveFolder(string folder) => Run(() => _actions.AddSaveFolder(folder), scan: true);

    [RelayCommand]
    private Task RemoveSaveFolder(FolderItem item) => Run(() => _actions.RemoveSaveFolder(_savePaths.GetValueOrDefault(item, item.Path)), scan: true);

    private async Task Run(Func<Task<Outcome>> change, bool scan = false)
    {
        if (scan)
        {
            if (Scanning)
            {
                return;
            }

            Scanning = true;
            Note = new Outcome("Scanning this PC for games and saves…");
        }

        try
        {
            var outcome = await change();
            Note = outcome.Text.Length > 0 ? outcome : null;
        }
        finally
        {
            Scanning = false;
            _reload();
        }
    }
}
