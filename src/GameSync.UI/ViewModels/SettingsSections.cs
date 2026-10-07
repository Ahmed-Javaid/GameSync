using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>
/// Backup and sync (BG-01, SET-02, SET-03, SET-06): starting at sign-in, the daily backup with its last run and Run
/// now, what new games back up, and who wins for them when two PCs disagree. A change to the defaults offers to apply
/// it to the games already syncing, which otherwise keep their own.
/// </summary>
public sealed partial class BackupSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private readonly Action _reload;
    private bool _showing;
    private bool _filesTouched;
    private bool _conflictTouched;
    private int _filesDiffer;
    private int _conflictDiffer;
    private List<string> _skip = [];

    public BackupSettings(SettingsActions actions, Action reload)
    {
        _actions = actions;
        _reload = reload;
    }

    [ObservableProperty]
    private bool _startAtSignIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DailyTitle), nameof(DailyDescription))]
    private bool _dailyOn;

    /// <summary>The daily backup's time, as 20:00.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DailyTitle))]
    private string _dailyAt = "20:00";

    public string DailyTitle => DailyOn ? $"Back up every day at {DailyAt}" : "Back up every day";

    public string DailyDescription => DailyOn
        ? "Pick a time your PC is usually on. If it's off then, the backup runs about 10 minutes after the next sign-in."
        : "Off. Games still sync after you play, while GameSync runs.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotEditingTime))]
    private bool _editingTime;

    public bool NotEditingTime => !EditingTime;

    [ObservableProperty]
    private string _timeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimeError))]
    private string? _timeError;

    public bool HasTimeError => TimeError is not null;

    /// <summary>SET-03: "Today 20:00 · 12 games checked · 1 uploaded · running games are always skipped".</summary>
    [ObservableProperty]
    private string _lastRun = "";

    /// <summary>Run now was pressed and the daily backup hasn't finished yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunLabel), nameof(CanRun))]
    private bool _running;

    public string RunLabel => Running ? "Running…" : "Run now";

    public bool CanRun => !Running;

    // What each card's last change came to, said in that card, next to what was pressed.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartNote))]
    private Outcome? _startNote;

    public bool HasStartNote => StartNote is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDailyNote))]
    private Outcome? _dailyNote;

    public bool HasDailyNote => DailyNote is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilesNote))]
    private Outcome? _filesNote;

    public bool HasFilesNote => FilesNote is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflictNote))]
    private Outcome? _conflictNote;

    public bool HasConflictNote => ConflictNote is not null;

    // ---- What to back up (SET-06) ----

    public IReadOnlyList<SelectOption> SettingsFilesOptions { get; } =
        [new(GameDefaults.SyncBetween, "Sync between PCs"), new(GameDefaults.ThisPc, "Back up on this PC"), new(GameDefaults.Off, "Don't back up")];

    public IReadOnlyList<SelectOption> ScreenshotOptions { get; } = [new("on", "Back up on this PC"), new("off", "Don't back up")];

    [ObservableProperty]
    private string _settingsFiles = GameDefaults.ThisPc;

    [ObservableProperty]
    private string _screenshots = "off";

    [ObservableProperty]
    private bool _skipJunk = true;

    [ObservableProperty]
    private IReadOnlyList<FolderItem> _patterns = [];

    /// <summary>Add a pattern was pressed: its field shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotAddingPattern))]
    private bool _addingPattern;

    public bool NotAddingPattern => !AddingPattern;

    [ObservableProperty]
    private string _patternText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPatternError))]
    private string? _patternError;

    public bool HasPatternError => PatternError is not null;

    public bool ShowsFilesApply => _filesTouched && _filesDiffer > 0;

    public string FilesApplyNote => $"New games follow this now. {Games(_filesDiffer, "Your")} keep{(_filesDiffer == 1 ? "s its" : " their")} own until you say.";

    public string FilesApplyLabel => $"Apply to {Games(_filesDiffer, "your").ToLowerInvariant()}…";

    public string FilesApplyQuestion =>
        $"{Games(_filesDiffer, "Your")} take{(_filesDiffer == 1 ? "s" : "")} these for {(_filesDiffer == 1 ? "its" : "their")} files. Each one's next version carries it to your other PCs; nothing already backed up is removed.";

    // ---- When two PCs disagree ----

    public IReadOnlyList<NavItem> ConflictChoices { get; } = [new("newest", "Newest wins"), new("ask", "Always ask")];

    [ObservableProperty]
    private string _conflict = "newest";

    public bool ShowsConflictApply => _conflictTouched && _conflictDiffer > 0;

    public string ConflictApplyNote => $"New games follow this now. {Games(_conflictDiffer, "Your")} keep{(_conflictDiffer == 1 ? "s its" : " their")} own until you say.";

    public string ConflictApplyLabel => $"Apply to {Games(_conflictDiffer, "your").ToLowerInvariant()}…";

    public string ConflictApplyQuestion => Conflict == "ask"
        ? $"{Games(_conflictDiffer, "Your")} will ask you whenever two PCs disagree, instead of keeping the newest save."
        : $"{Games(_conflictDiffer, "Your")} will keep the newest save when two PCs disagree, with the other one pinned and a Swap button.";

    public void Show(SettingsView view)
    {
        _showing = true;
        StartAtSignIn = view.StartsAtSignIn;
        DailyOn = view.DailyAt is not null;
        if (view.DailyAt is { } at)
        {
            DailyAt = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        Running = view.DailyAsked;
        var now = DateTime.Now;
        LastRun = view.LastDaily is { } last
            ? $"{SettingsViewModel.When(last.AtUtc, now)} · {Count(last.Games, "game")} checked · {last.Uploads} uploaded{(last.NeedYou > 0 ? $" · {last.NeedYou} conflict{(last.NeedYou == 1 ? "" : "s")}" : "")} · running games are always skipped"
            : "None yet · running games are always skipped";
        var d = view.Defaults;
        SettingsFiles = d.SettingsFiles;
        Screenshots = d.Screenshots ? "on" : "off";
        SkipJunk = d.SkipJunk;
        if (!_skip.SequenceEqual(d.Skip))
        {
            _skip = d.Skip.ToList();
            Patterns = _skip.Select(Item).ToList();
        }

        Conflict = d.Conflict == ConflictPolicy.AlwaysAsk ? "ask" : "newest";
        (_filesDiffer, _conflictDiffer) = (view.FilesDiffer, view.ConflictDiffer);
        _showing = false;
        foreach (var name in new[] { nameof(ShowsFilesApply), nameof(FilesApplyNote), nameof(FilesApplyLabel), nameof(FilesApplyQuestion), nameof(ShowsConflictApply),
                     nameof(ConflictApplyNote), nameof(ConflictApplyLabel), nameof(ConflictApplyQuestion) })
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnStartAtSignInChanged(bool value)
    {
        if (!_showing)
        {
            _ = Change(() => _actions.SetStartAtSignIn(value), note => StartNote = note);
        }
    }

    partial void OnDailyOnChanged(bool value)
    {
        if (!_showing)
        {
            EditingTime = false;
            _ = Change(() => _actions.SetDaily(value ? Parse(DailyAt) ?? new TimeOnly(20, 0) : null), note => DailyNote = note);
        }
    }

    [RelayCommand]
    private void EditTime()
    {
        TimeText = DailyAt;
        TimeError = null;
        EditingTime = true;
    }

    [RelayCommand]
    private async Task SaveTime()
    {
        if (Parse(TimeText) is not { } at)
        {
            TimeError = "Write the time on the 24-hour clock, such as 20:00.";
            return;
        }

        TimeError = null;
        EditingTime = false;
        DailyAt = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        await Change(() => _actions.SetDaily(at), note => DailyNote = note);
    }

    [RelayCommand]
    private void CancelTime()
    {
        TimeError = null;
        EditingTime = false;
    }

    [RelayCommand]
    private async Task RunNow()
    {
        Running = true;
        await Change(_actions.RunDailyNow, note => DailyNote = note);
    }

    partial void OnSettingsFilesChanged(string value) => FilesChanged();

    partial void OnScreenshotsChanged(string value) => FilesChanged();

    partial void OnSkipJunkChanged(bool value) => FilesChanged();

    partial void OnConflictChanged(string value)
    {
        if (!_showing)
        {
            _conflictTouched = true;
            _ = SaveDefaults();
        }
    }

    [RelayCommand]
    private void StartPattern()
    {
        PatternText = "";
        PatternError = null;
        AddingPattern = true;
    }

    [RelayCommand]
    private void CancelPattern()
    {
        PatternError = null;
        AddingPattern = false;
    }

    /// <summary>Also skip: a pattern such as <c>*.bak</c> or <c>**/Mods/**</c>, checked as it's added.</summary>
    [RelayCommand]
    private void AddPattern()
    {
        var pattern = PatternText.Trim();
        if (GameDefaults.Refusal(pattern) is { } refused)
        {
            PatternError = refused;
            return;
        }

        if (_skip.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            PatternError = $"{pattern} is skipped already.";
            return;
        }

        if (_skip.Count >= GameDefaults.Patterns)
        {
            PatternError = $"GameSync takes up to {GameDefaults.Patterns} patterns.";
            return;
        }

        PatternError = null;
        PatternText = "";
        AddingPattern = false;
        _skip.Add(pattern);
        Patterns = _skip.Select(Item).ToList();
        FilesChanged();
    }

    [RelayCommand]
    private void RemovePattern(FolderItem item)
    {
        if (_skip.RemoveAll(p => p == item.Path) > 0)
        {
            Patterns = _skip.Select(Item).ToList();
            FilesChanged();
        }
    }

    [RelayCommand]
    private Task ApplyFiles() => Apply(files: true);

    [RelayCommand]
    private Task ApplyConflict() => Apply(files: false);

    private async Task Apply(bool files)
    {
        var outcome = await _actions.ApplyDefaults(files, !files);
        if (files)
        {
            _filesTouched = false;
            FilesNote = outcome;
        }
        else
        {
            _conflictTouched = false;
            ConflictNote = outcome;
        }

        _reload();
    }

    private void FilesChanged()
    {
        if (!_showing)
        {
            _filesTouched = true;
            _ = SaveDefaults();
        }
    }

    private async Task SaveDefaults()
    {
        await _actions.SetDefaults(new GameDefaults
        {
            SettingsFiles = SettingsFiles,
            Screenshots = Screenshots == "on",
            SkipJunk = SkipJunk,
            Skip = _skip.ToList(),
            Conflict = Conflict == "ask" ? ConflictPolicy.AlwaysAsk : ConflictPolicy.NewestWins,
        });
        _reload();
    }

    /// <summary>A change, with what it came to said in its own card.</summary>
    private async Task Change(Func<Task<Outcome>> change, Action<Outcome?> say)
    {
        var outcome = await change();
        say(outcome.Text.Length > 0 ? outcome : null);
        _reload();
    }

    /// <summary>20:00, 8:05 or 20.00, on the 24-hour clock.</summary>
    public static TimeOnly? Parse(string text) =>
        TimeOnly.TryParseExact(text.Trim().Replace('.', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    private static FolderItem Item(string pattern) => pattern.Replace('\\', '/').Trim('/').Contains('/')
        ? new FolderItem(pattern, "From the top of each save folder", Icon: pattern.EndsWith("/**", StringComparison.Ordinal) ? "folder" : "file")
        : new FolderItem(pattern, "In any folder", Icon: "file");

    private static string Games(int count, string your) => count == 1 ? $"{your} 1 game" : $"{your} {count} games";

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";
}

/// <summary>
/// Cloud (CLOUD-05, CLOUD-08, CLOUD-13, ONB-06): Google Drive signed in, with its folder and how full it is, and Sign
/// out; or the folder every PC points at; or no cloud yet, with Connect the cloud.
/// </summary>
public sealed partial class CloudSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private readonly Action _reload;
    private CloudDetails? _details;
    private bool _loading;

    public CloudSettings(SettingsActions actions, Action reload)
    {
        _actions = actions;
        _reload = reload;
    }

    /// <summary>"drive", "folder" or "none".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDrive), nameof(IsFolder), nameof(IsNone), nameof(Subtitle))]
    private string _kind = SettingsData.NoCloud;

    public bool IsDrive => Kind == SettingsData.Drive;

    public bool IsFolder => Kind == SettingsData.Folder;

    public bool IsNone => Kind == SettingsData.NoCloud;

    public string Subtitle => Kind switch
    {
        SettingsData.Drive => "Your saves live in your own Google Drive.",
        SettingsData.Folder => "Your saves live in a folder every PC points at.",
        _ => "No cloud is connected yet, so every version stays on this PC.",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedOut))]
    private bool _signedIn;

    public bool SignedOut => !SignedIn;

    [ObservableProperty]
    private bool _canSignIn;

    [ObservableProperty]
    private string _accountTitle = "Signed in";

    [ObservableProperty]
    private string? _folderPath;

    [ObservableProperty]
    private string _folderMeta = "Every PC that syncs points at this folder";

    [ObservableProperty]
    private bool _showsStorage;

    [ObservableProperty]
    private double _storagePercent;

    [ObservableProperty]
    private string? _storageRight;

    /// <summary>CLOUD-05: the Drive is over 80% full.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageWarning))]
    private string? _storageWarning;

    public bool HasStorageWarning => StorageWarning is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool _busy;

    public bool NotBusy => !Busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private Outcome? _note;

    public bool HasNote => Note is not null;

    public void Show(SettingsView view)
    {
        var changed = view.Cloud != Kind || view.SignedIn != SignedIn;
        Kind = view.Cloud;
        SignedIn = view.SignedIn;
        CanSignIn = view.CanSignIn;
        FolderPath = view.CloudFolder;
        if (changed)
        {
            _details = null;
            ShowDetails();
            LoadDetails();
        }
    }

    /// <summary>The account, how full the Drive is, and the folder's free space, asked of the cloud itself.</summary>
    public async void LoadDetails()
    {
        if (_loading || IsNone || (IsDrive && !SignedIn))
        {
            return;
        }

        _loading = true;
        try
        {
            _details = await _actions.Cloud(CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _details = null;
        }
        finally
        {
            _loading = false;
        }

        ShowDetails();
    }

    private void ShowDetails()
    {
        AccountTitle = _details?.Account is { } account ? $"Signed in as {account}" : "Signed in";
        if (IsDrive && _details is { UsedBytes: { } used, TotalBytes: { } total } && total > 0)
        {
            ShowsStorage = true;
            StoragePercent = Math.Clamp(used * 100.0 / total, 0, 100);
            StorageRight = $"{Gb(used)} of {Gb(total)} GB";
            StorageWarning = used * 100 / total >= 80
                ? $"Your Drive is {used * 100 / total}% full. GameSync keeps going until it's full; after that, new versions wait on this PC until there's room."
                : null;
        }
        else
        {
            ShowsStorage = false;
            StorageWarning = null;
        }

        FolderMeta = IsFolder && _details?.TotalBytes is { } free
            ? $"Every PC that syncs points at this folder · {Cli.FormatSize(free)} free"
            : "Every PC that syncs points at this folder";
    }

    /// <summary>CLOUD-08: GameSync's folder in Drive, in the browser.</summary>
    [RelayCommand]
    private void OpenDrive()
    {
        if (_details?.Where is { } where && where.StartsWith("https://drive.google.com/", StringComparison.Ordinal))
        {
            _actions.OpenLink(where);
        }
        else
        {
            _actions.OpenLink("https://drive.google.com/drive/my-drive");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (FolderPath is { } folder)
        {
            _actions.OpenFolder(folder);
        }
    }

    [RelayCommand]
    private void Connect() => _actions.ConnectCloud();

    [RelayCommand]
    private Task SignOut() => Run(_actions.SignOut);

    [RelayCommand]
    private Task SignIn() => Run(_actions.SignIn);

    private async Task Run(Func<Task<Outcome>> change)
    {
        Busy = true;
        try
        {
            var outcome = await change();
            Note = outcome.Text.Length > 0 ? outcome : null;
        }
        finally
        {
            Busy = false;
            _reload();
        }
    }

    private static string Gb(long bytes) => (bytes / 1024.0 / 1024 / 1024).ToString(bytes < 10L * 1024 * 1024 * 1024 ? "0.##" : "0.#", CultureInfo.InvariantCulture);
}

/// <summary>A PC in Devices: its name and when it was last seen, with its GameSync.</summary>
public sealed record PcRow(string Name, string Description)
{
    public override string ToString() => $"{Name}, {Description}";
}

/// <summary>Devices (PC-01, PC-02): every PC that syncs with this cloud; this one can be renamed, and the others see it after its next sync.</summary>
public sealed partial class DevicesSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private readonly Action _reload;

    public DevicesSettings(SettingsActions actions, Action reload)
    {
        _actions = actions;
        _reload = reload;
    }

    [ObservableProperty]
    private string _thisPc = "";

    [ObservableProperty]
    private string _thisPcDescription = "This PC";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOthers), nameof(NoOthers))]
    private IReadOnlyList<PcRow> _others = [];

    public bool HasOthers => Others.Count > 0;

    public bool NoOthers => Others.Count == 0;

    [ObservableProperty]
    private string _noOthersText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotRenaming))]
    private bool _renaming;

    public bool NotRenaming => !Renaming;

    [ObservableProperty]
    private string _nameText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private Outcome? _note;

    public bool HasNote => Note is not null;

    public void Show(SettingsView view)
    {
        var now = DateTime.Now;
        ThisPc = view.ThisPc;
        ThisPcDescription = $"This PC · GameSync {view.AppVersion}";
        var others = view.Pcs.Where(p => !p.ThisPc)
            .Select(p => new PcRow(p.Name, string.Join(" · ", new[]
            {
                p.LastSeenUtc is { } seen ? $"Last seen {Lower(SettingsViewModel.When(seen, now))}" : null,
                p.AppVersion is { Length: > 0 } version ? $"GameSync {version}" : null,
            }.OfType<string>())))
            .ToList();
        if (!others.SequenceEqual(Others))
        {
            Others = others;
        }

        NoOthersText = view.Cloud == SettingsData.NoCloud
            ? "No cloud is connected yet, so no other PC syncs with this one."
            : "No other PC syncs with this cloud yet. Install GameSync on the other PC and connect the same cloud; it shows here after its first sync.";
    }

    /// <summary>"today 19:31" and "yesterday 19:31" inside a sentence; a date stays as it is.</summary>
    private static string Lower(string when) => when.StartsWith("Today", StringComparison.Ordinal) || when.StartsWith("Yesterday", StringComparison.Ordinal)
        ? char.ToLowerInvariant(when[0]) + when[1..]
        : when;

    [RelayCommand]
    private void Rename()
    {
        NameText = ThisPc;
        Note = null;
        Renaming = true;
    }

    [RelayCommand]
    private async Task SaveName()
    {
        var outcome = await _actions.RenamePc(NameText);
        Note = outcome;
        if (!outcome.Failed)
        {
            Renaming = false;
            _reload();
        }
    }

    [RelayCommand]
    private void CancelRename()
    {
        Note = null;
        Renaming = false;
    }
}

/// <summary>Notifications (BG-05, BG-06): only for what needs the person, held while a fullscreen game runs, and the optional daily line.</summary>
public sealed partial class NotificationSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private bool _showing;

    public NotificationSettings(SettingsActions actions) => _actions = actions;

    [ObservableProperty]
    private bool _hold = true;

    [ObservableProperty]
    private bool _dailyNote;

    public void Show(SettingsView view)
    {
        _showing = true;
        Hold = view.HoldWhileFullscreen;
        DailyNote = view.DailyNote;
        _showing = false;
    }

    partial void OnHoldChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetNotifications(value, null);
        }
    }

    partial void OnDailyNoteChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetNotifications(null, value);
        }
    }
}

/// <summary>Safety: the games with an anti-cheat, which get no learn mode and no sharing, and launch only through their own launcher (ONB-05).</summary>
public sealed partial class SafetySettings : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<FolderItem> _antiCheat = [];

    public void Show(SettingsView view)
    {
        var games = view.AntiCheat.Select(g => new FolderItem(g.Title, null, g.AntiCheat, "shield")).ToList();
        if (!games.SequenceEqual(AntiCheat))
        {
            AntiCheat = games;
        }
    }
}

/// <summary>
/// Updates (PKG-03, R19; design system version 53 → SettingsScreen #updates): the version this PC runs; a newer one got
/// ready from GitHub, with what's new and Restart to update; or that there's none, or why it couldn't tell; and Check for
/// updates every day. Nothing installs that GameSync's own release signature doesn't check out for.
/// </summary>
public sealed partial class UpdateSettings : ObservableObject
{
    public const string Latest = "latest";
    public const string Checking = "checking";
    public const string Downloading = "downloading";
    public const string Ready = "ready";
    public const string Refused = "refused";
    public const string Problem = "problem";

    private readonly SettingsActions _actions;
    private bool _showing;

    public UpdateSettings(SettingsActions actions) => _actions = actions;

    /// <summary>"GameSync 1.0.0", the card's title.</summary>
    [ObservableProperty]
    private string _title = "GameSync";

    /// <summary>Where this copy is: installed for this person alone, or a build or the zip, which updates don't replace.</summary>
    [ObservableProperty]
    private string _where = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsDownloading), nameof(IsRefused), nameof(ShowsCheck), nameof(IsChecking))]
    private string _state = Latest;

    /// <summary>The row's title: "GameSync 1.0.1 is ready", "You have the latest version", …</summary>
    [ObservableProperty]
    private string _headline = "Updates";

    [ObservableProperty]
    private string _line = "";

    /// <summary>R19: why an update came down but wasn't kept, in warn.</summary>
    [ObservableProperty]
    private string _warning = "";

    /// <summary>What's new in the release ready, as its notes' bullets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private IReadOnlyList<string> _notes = [];

    [ObservableProperty]
    private string _notesTitle = "";

    /// <summary>Its page on GitHub, for Release notes on GitHub.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    private string? _page;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressLabel = "";

    [ObservableProperty]
    private string _progressRight = "";

    [ObservableProperty]
    private bool _daily = true;

    /// <summary>Restart to update waits while a game is played; the tooltip says why.</summary>
    [ObservableProperty]
    private bool _canRestart = true;

    [ObservableProperty]
    private string _restartTip = "GameSync closes, installs it and opens again in a few seconds.";

    /// <summary>What Check now or Restart to update came to, when it went wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private Outcome? _note;

    public bool IsReady => State == Ready;

    public bool IsDownloading => State == Downloading;

    public bool IsRefused => State == Refused;

    public bool IsChecking => State == Checking;

    /// <summary>Check now shows unless one is ready or coming down.</summary>
    public bool ShowsCheck => State is Latest or Checking or Problem;

    public bool HasNotes => Notes.Count > 0;

    public bool HasPage => !string.IsNullOrEmpty(Page);

    public bool HasNote => Note is { Text.Length: > 0 };

    public void Show(UpdatesView view, DateTime nowLocal)
    {
        _showing = true;
        Title = $"GameSync {view.Current.ToString(3)}";
        Where = view.Installed
            ? @"Installed for you alone, in %LOCALAPPDATA%\Programs\GameSync."
            : $"Running from {AppContext.BaseDirectory.TrimEnd('\\')}, not installed: a newer version installs in %LOCALAPPDATA%\\Programs\\GameSync, beside this one.";
        Daily = view.Daily;
        _showing = false;
        if (State is Checking or Downloading)
        {
            // The check under way says how it ends.
            return;
        }

        var checkedAt = view.CheckedUtc is { } at ? SettingsViewModel.When(at, nowLocal) : null;
        if (view.Ready is { } ready)
        {
            State = Ready;
            Headline = $"GameSync {ready.Version.ToString(3)} is ready";
            Line = "Restart to update: GameSync closes, installs it and opens again in a few seconds. Your games and saves aren't touched.";
            NotesTitle = $"What's new in {ready.Version.ToString(3)}".ToUpperInvariant();
            Notes = ready.Notes;
            Page = ready.Page;
        }
        else if (view.Refused is { } refused && refused.Version > view.Current)
        {
            State = Refused;
            Headline = $"GameSync {refused.Version.ToString(3)} wasn't installed";
            Line = "";
            Warning = $"It didn't pass GameSync's own check: {refused.Reason}. Nothing was installed; GameSync tries again tomorrow.";
            Notes = [];
            Page = null;
        }
        else if (view.Problem is { } problem && problem.AtUtc >= (view.CheckedUtc ?? DateTime.MinValue))
        {
            State = Problem;
            Headline = "Couldn't check for updates";
            Line = $"{SettingsViewModel.When(problem.AtUtc, nowLocal)}: {problem.Reason}. GameSync tries again {(problem.Offline ? "in a few hours" : "tomorrow")}.";
            Notes = [];
            Page = null;
        }
        else
        {
            State = Latest;
            Headline = "You have the latest version";
            Line = checkedAt is null ? "Not checked yet." : $"Checked {checkedAt.ToLowerInvariant()}.";
            Notes = [];
            Page = null;
        }
    }

    /// <summary>The app's check under way (daily or Check now): checking, then the download as it goes.</summary>
    public void ShowChecking()
    {
        State = Checking;
        Headline = "Checking for updates";
        Line = "Asking GitHub for GameSync's latest release.";
    }

    public void ShowDownloading(Version version, long done, long total, string right)
    {
        State = Downloading;
        Headline = $"Getting GameSync {version.ToString(3)}";
        Line = "In the background. It's checked against GameSync's own signature before it can install.";
        Progress = total > 0 ? 100.0 * done / total : 0;
        ProgressLabel = $"{done / 1048576.0:0} of {total / 1048576.0:0} MB";
        ProgressRight = right;
    }

    /// <summary>The check is over: what it came to shows from <paramref name="view"/>.</summary>
    public void Finished(UpdatesView view, DateTime nowLocal)
    {
        State = Latest;
        Show(view, nowLocal);
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        Note = null;
        var outcome = await _actions.CheckUpdates();
        if (outcome.Failed)
        {
            Note = outcome;
        }
    }

    [RelayCommand]
    private async Task Restart()
    {
        Note = null;
        var outcome = await _actions.RestartToUpdate();
        if (outcome.Failed)
        {
            Note = outcome;
        }
    }

    [RelayCommand]
    private void ReleaseNotes()
    {
        if (Page is { Length: > 0 } page)
        {
            _actions.OpenLink(page);
        }
    }

    partial void OnDailyChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetUpdatesDaily(value);
        }
    }
}

/// <summary>
/// A game in Achievements by game (KAN-110, KAN-131; design system version 41 → AchievementGamesDialog): its name, how far
/// it is, and two switches: Counts (on the Achievements page and Home) and GameSync's popup, off by default for a game a
/// launcher runs, as the launcher shows its own. A game left out has no popup either.
/// </summary>
public sealed partial class AchievementGameItem : ObservableObject
{
    private readonly Func<GameId, bool, Task> _setLeftOut;
    private readonly Func<GameId, bool, Task> _setPopup;
    private bool _showing;
    private string _countLine = "";

    public AchievementGameItem(AchievementGameRow row, Func<GameId, bool, Task> setLeftOut, Func<GameId, bool, Task>? setPopup = null)
    {
        Game = row.Game;
        Title = row.Title;
        Thumb = row.Cover is null ? null : ArtImages.LoadCover(row.Cover, 40);
        _setLeftOut = setLeftOut;
        _setPopup = setPopup ?? ((_, _) => Task.CompletedTask);
        Show(row);
    }

    public GameId Game { get; }

    public string Title { get; }

    /// <summary>Its cover, small, before its name (design system version 35).</summary>
    public Avalonia.Media.IImage? Thumb { get; }

    /// <summary>"123 of 171 · Steam shows its own popup", or what being left out means.</summary>
    [ObservableProperty]
    private string _line = "";

    /// <summary>Counted in the achievements: its place on the Achievements page and Home, and its popup.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PopupShown), nameof(CanPopup))]
    private bool _counted = true;

    /// <summary>R13 (design system version 51): it ships an anti-cheat, so GameSync draws nothing over it: no popup, ever.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PopupShown), nameof(CanPopup))]
    private bool _antiCheat;

    /// <summary>Whether its popup can be turned on: it counts, and it has no anti-cheat.</summary>
    public bool CanPopup => Counted && !AntiCheat;

    /// <summary>GameSync's popup for it, as chosen; it shows only while the game counts too.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PopupShown))]
    private bool _popup;

    /// <summary>Its Zenith, earned: the medal after its name.</summary>
    [ObservableProperty]
    private bool _zenith;

    /// <summary>The launcher that runs it and shows its own popup ("Steam"), or null.</summary>
    public string? Launcher { get; private set; }

    /// <summary>The popup's switch: on only while it counts and its popup is on; it can't be turned on while it's left out, or ever over an anti-cheat.</summary>
    public bool PopupShown
    {
        get => CanPopup && Popup;
        set
        {
            if (CanPopup)
            {
                Popup = value;
            }
        }
    }

    public string SwitchName => $"Count {Title} in your achievements";

    public string PopupSwitchName => $"GameSync's popup for {Title}";

    public void Show(AchievementGameRow row)
    {
        _showing = true;
        _countLine = row.Line;
        Launcher = row.Launcher;
        Zenith = row.Zenith;
        AntiCheat = row.AntiCheat;
        Counted = !row.LeftOut;
        Popup = row.Popup;
        Line = LineOf();
        _showing = false;
    }

    private string LineOf() => !Counted ? "Left out: not on the Achievements page or Home, no popup"
        : _countLine + (Zenith ? " · Zenith" : "") + (AntiCheat ? " · Has an anti-cheat: GameSync draws nothing over it"
            : Launcher is { } launcher ? $" · {launcher} shows its own popup" : "");

    partial void OnCountedChanged(bool value)
    {
        Line = LineOf();
        if (!_showing)
        {
            _ = _setLeftOut(Game, !value);
        }
    }

    partial void OnPopupChanged(bool value)
    {
        if (!_showing)
        {
            _ = _setPopup(Game, value);
        }
    }

    public override string ToString() => $"{Title}, {Line}";
}

/// <summary>
/// Achievements by game (KAN-131; design system version 41 → AchievementGamesDialog; the owner, 4 Oct 2026: "it needs to
/// be either a popup or a new window that shows all the games in the achievement system"): every game GameSync reads
/// achievements for, found by name, each with Counts and GameSync's popup; Done or Esc closes it. Changes apply at once.
/// </summary>
public sealed partial class AchievementGamesViewModel : ObservableObject
{
    private readonly AchievementSettings _settings;
    private readonly Action _close;

    public AchievementGamesViewModel(AchievementSettings settings, Action close)
    {
        _settings = settings;
        _close = close;
        // A switch flipped changes the summary only, so the list and its focus stay; the games read again change the list.
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AchievementSettings.Games))
            {
                Filter();
            }
            else if (e.PropertyName == nameof(AchievementSettings.GamesLine))
            {
                OnPropertyChanged(nameof(Summary));
            }
        };
        Filter();
    }

    /// <summary>Find a game: the games whose name holds what's typed.</summary>
    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private IReadOnlyList<AchievementGameItem> _shown = [];

    /// <summary>"12 games · 11 counted · 1 popup".</summary>
    public string Summary
    {
        get
        {
            var games = _settings.Games;
            var counted = games.Count(g => g.Counted);
            var popups = games.Count(g => g.PopupShown);
            return $"{Plural(games.Count, "game")} · {counted.ToString(CultureInfo.InvariantCulture)} counted · {Plural(popups, "popup")}";
        }
    }

    public bool NoMatch => Shown.Count == 0 && Query.Trim().Length > 0;

    public bool NoGames => _settings.Games.Count == 0;

    public string NoMatchLine => $"No game matches \u201c{Query.Trim()}\u201d.";

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        var words = Query.Trim();
        Shown = _settings.Games.Where(g => words.Length == 0 || g.Title.Contains(words, StringComparison.CurrentCultureIgnoreCase)).ToList();
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(NoMatchLine));
        OnPropertyChanged(nameof(NoGames));
        OnPropertyChanged(nameof(Summary));
    }

    private static string Plural(int n, string word) => $"{n.ToString(CultureInfo.InvariantCulture)} {word}{(n == 1 ? "" : "s")}";

    /// <summary>Done, or the close button.</summary>
    [RelayCommand]
    private void Close() => _close();

    /// <summary>Esc: clears the search first, then closes.</summary>
    public void Escape()
    {
        if (Query.Length > 0)
        {
            Query = "";
        }
        else
        {
            _close();
        }
    }
}

/// <summary>
/// Achievements (ACH-09, ACH-10; design system version 35 → SettingsScreen #achievements; the owner, 3 Oct 2026: "Add a
/// setting in settings to include/exclude a game from the achievement system"): the popup over a game when one unlocks,
/// its sound and its corner; Try the popup, now or in 10 seconds so there's time to switch to a game; and, since version 41
/// (KAN-131), one row for every game, Achievements by game, whose Choose games… opens <see cref="AchievementGamesViewModel"/>.
/// </summary>
public sealed partial class AchievementSettings : ObservableObject
{
    private readonly SettingsActions _actions;
    private bool _showing;
    private CancellationTokenSource? _later;

    public AchievementSettings(SettingsActions actions) => _actions = actions;

    public IReadOnlyList<SelectOption> CornerOptions { get; } =
        [new("top-right", "Top right"), new("top-left", "Top left"), new("bottom-right", "Bottom right"), new("bottom-left", "Bottom left")];

    /// <summary>KAN-120: the sounds to choose from.</summary>
    public IReadOnlyList<SelectOption> ChimeOptions { get; } = GameSync.Windows.Chime.Sounds.Select(s => new SelectOption(s.Id, s.Name)).ToList();

    /// <summary>KAN-120: the volumes to choose from, Soft unless changed.</summary>
    public IReadOnlyList<SelectOption> VolumeOptions { get; } = GameSync.Windows.Chime.Volumes.Select(v => new SelectOption(v.Id, v.Name)).ToList();

    [ObservableProperty]
    private bool _popups = true;

    [ObservableProperty]
    private bool _sound = true;

    [ObservableProperty]
    private string _corner = "top-right";

    [ObservableProperty]
    private string _chime = GameSync.Windows.Chime.DefaultSound;

    [ObservableProperty]
    private string _volume = GameSync.Windows.Chime.DefaultVolume;

    /// <summary>What Try the popup says: what it does, or that it's coming in 10 seconds.</summary>
    [ObservableProperty]
    private string _tryLine = TryIdle;

    private const string TryIdle = "One of your own unlocked achievements, as it shows over a game: now, or in 10 seconds so you can switch to a game first.";

    [ObservableProperty]
    private IReadOnlyList<AchievementGameItem> _games = [];

    [ObservableProperty]
    private bool _gamesRead;

    /// <summary>
    /// KAN-123 (design system version 48; the owner: "As long as my offline games also have achievements"): the folders
    /// where copies Steam doesn't run keep their own records of what's unlocked, each saying how many games have one there.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<FolderItem> _recordFolders = [];

    /// <summary>What adding or taking off a folder came to.</summary>
    [ObservableProperty]
    private Outcome? _recordNote;

    /// <summary>Add a folder: the folder picked on the page.</summary>
    public async Task AddRecordFolder(string folder) => RecordNote = await _actions.AddAchievementFolder(folder);

    [RelayCommand]
    private async Task RemoveRecordFolder(FolderItem item) => RecordNote = await _actions.RemoveAchievementFolder(item.Path);

    public bool HasGames => Games.Count > 0;

    public bool NoGames => GamesRead && Games.Count == 0;

    partial void OnGamesChanged(IReadOnlyList<AchievementGameItem> value)
    {
        foreach (var game in value)
        {
            game.PropertyChanged -= GameChanged;
            game.PropertyChanged += GameChanged;
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(NoGames));
        OnPropertyChanged(nameof(GamesLine));
    }

    private void GameChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(GamesLine));

    /// <summary>
    /// Achievements by game's row (version 41): "12 of 13 games count on the Achievements page and Home. GameSync's popup is
    /// on for 1: Steam shows its own for the rest."
    /// </summary>
    public string GamesLine
    {
        get
        {
            if (Games.Count == 0)
            {
                return GamesRead ? "Steam games show here once Steam has run them on this PC, and copies with their own record once their folder is added below." : "Reading your games\u2026";
            }

            var counted = Games.Count(g => g.Counted);
            var popups = Games.Count(g => g.PopupShown);
            var rest = Games.Where(g => g.Counted && !g.PopupShown && g.Launcher is not null).Select(g => g.Launcher!).Distinct().ToList();
            var own = rest.Count == 0 ? "" : $": {(rest.Count == 1 ? rest[0] : "their launchers")} show{(rest.Count == 1 ? "s" : "")} {(rest.Count == 1 ? "its" : "their")} own for the rest";
            return $"{counted.ToString(CultureInfo.InvariantCulture)} of {Games.Count.ToString(CultureInfo.InvariantCulture)} games count on the Achievements page and Home. " +
                $"GameSync's popup is on for {popups.ToString(CultureInfo.InvariantCulture)}{(popups == counted ? "" : own)}.";
        }
    }

    /// <summary>Choose games…: Achievements by game over the page.</summary>
    [RelayCommand]
    private void ChooseGames() => _actions.OpenDialog(new AchievementGamesViewModel(this, _actions.CloseDialog));

    partial void OnGamesReadChanged(bool value)
    {
        OnPropertyChanged(nameof(NoGames));
        OnPropertyChanged(nameof(GamesLine));
    }

    public void Show(SettingsView view)
    {
        _showing = true;
        Popups = view.Achievements.Popups;
        Sound = view.Achievements.Sound;
        Corner = view.Achievements.Corner;
        Chime = view.Achievements.Chime;
        Volume = view.Achievements.Volume;
        var folders = view.AchievementFolders.Select(f => new FolderItem(f.Path, f.Missing ? "Not there right now" : f.Games switch
        {
            0 => "No records here yet",
            1 => "Records for 1 game",
            _ => $"Records for {f.Games.ToString(CultureInfo.InvariantCulture)} games",
        }, null, "trophy")).ToList();
        if (!folders.SequenceEqual(RecordFolders))
        {
            RecordFolders = folders;
        }

        _showing = false;
    }

    /// <summary>Reads the games again: as the section opens, and after the app refreshes.</summary>
    public async void LoadGames()
    {
        try
        {
            var rows = await _actions.AchievementGames(CancellationToken.None);
            var known = Games.ToDictionary(g => g.Game);
            var next = rows.Select(r =>
            {
                if (known.TryGetValue(r.Game, out var item))
                {
                    item.Show(r);
                    return item;
                }

                return new AchievementGameItem(r, _actions.SetAchievementsLeftOut, _actions.SetAchievementPopup);
            }).ToList();
            if (!next.SequenceEqual(Games))
            {
                Games = next;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Read again next time the section opens.
        }

        GamesRead = true;
    }

    partial void OnPopupsChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetAchievementPopups(new AchievementPopupChange(Popups: value));
        }
    }

    partial void OnSoundChanged(bool value)
    {
        if (!_showing)
        {
            _ = _actions.SetAchievementPopups(new AchievementPopupChange(Sound: value));
        }
    }

    partial void OnCornerChanged(string value)
    {
        if (!_showing)
        {
            _ = _actions.SetAchievementPopups(new AchievementPopupChange(Corner: value));
        }
    }

    /// <summary>A sound picked plays at once, as it will over a game (KAN-120).</summary>
    partial void OnChimeChanged(string value)
    {
        if (!_showing)
        {
            _ = _actions.SetAchievementPopups(new AchievementPopupChange(Chime: value));
            _actions.HearChime(value, Volume);
        }
    }

    /// <summary>A volume picked plays the sound at it at once.</summary>
    partial void OnVolumeChanged(string value)
    {
        if (!_showing)
        {
            _ = _actions.SetAchievementPopups(new AchievementPopupChange(Volume: value));
            _actions.HearChime(Chime, value);
        }
    }

    /// <summary>Hear it: the sound picked, at the volume picked.</summary>
    [RelayCommand]
    private void Hear() => _actions.HearChime(Chime, Volume);

    /// <summary>The popup now, in the corner picked, with its chime when the sound is on.</summary>
    [RelayCommand]
    private void TryNow()
    {
        _later?.Cancel();
        TryLine = TryIdle;
        _actions.TryPopup(TimeSpan.Zero);
    }

    /// <summary>The popup in 10 seconds: time to switch to a game and see it over it.</summary>
    [RelayCommand]
    private async Task TryLater()
    {
        _later?.Cancel();
        var later = _later = new CancellationTokenSource();
        _actions.TryPopup(TimeSpan.FromSeconds(10));
        TryLine = "Showing in 10 seconds: switch to your game now.";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), later.Token);
            TryLine = TryIdle;
        }
        catch (TaskCanceledException)
        {
        }
    }
}
