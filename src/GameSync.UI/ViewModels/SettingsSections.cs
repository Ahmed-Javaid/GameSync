using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Games;
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
            ? $"{SettingsViewModel.When(last.AtUtc, now)} · {Count(last.Games, "game")} checked · {last.Uploads} uploaded{(last.NeedYou > 0 ? $" · {last.NeedYou} need you" : "")} · running games are always skipped"
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
