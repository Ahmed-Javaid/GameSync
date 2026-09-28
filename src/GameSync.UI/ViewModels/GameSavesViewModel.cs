using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>A place a game keeps its saves: the folder as every PC reads it, this PC's folder, and what's there.</summary>
public sealed record PlaceItem(string Portable, string? Folder, string? Tag, string Evidence, ICommand? OpenFolder)
{
    public string FolderText => Folder ?? "Not on this PC";

    /// <summary>
    /// This PC's folder, under the portable one, unless they're the same (a folder added by hand with no placeholder) or
    /// it's a registry key, which is the same on every PC.
    /// </summary>
    public bool ShowsFolder => Tag != "Registry" &&
        (Folder is null || !string.Equals(Portable.Replace('\\', '/').TrimEnd('/'), Folder.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    public bool HasTag => Tag is not null;

    public bool CanOpen => Folder is not null && OpenFolder is not null;

    public override string ToString() => string.Join(", ", new[] { Folder ?? Portable, Tag, Evidence }.OfType<string>());
}

/// <summary>A named save: its name, when and on which PC it was saved.</summary>
public sealed record NamedSaveItem(string Name, VersionId Version, string When, string Pc)
{
    public string Meta => $"{When} · {Pc}";

    public override string ToString() => $"{Name}, saved {When} on {Pc}";
}

/// <summary>A version in a game's history: when it was saved, the PC, what sets it apart, its size.</summary>
public sealed record VersionItem(VersionId Id, string Saved, string Pc, string? Note, bool IsCurrent, bool IsPinned, string Size, bool Uploaded)
{
    /// <summary>What sets a version apart, beside a pin when it's pinned; the current one shows Current instead.</summary>
    public bool ShowsNote => !IsCurrent && Note is not null;

    public bool ShowsPin => ShowsNote && IsPinned;

    public bool CanRestore => !IsCurrent;

    public override string ToString() => string.Join(", ", new[] { Saved, Pc, IsCurrent ? "current" : Note, Size, Uploaded ? null : "not uploaded yet" }.OfType<string>());
}

/// <summary>
/// One game's saves in the save manager (design system → GameSavesScreen): everything about a game's saves, which its
/// page only points to (MGR-07). Back and a breadcrumb; the game's status with its actions when it needs you; named
/// saves; where the saves are; every version from every PC with Restore; its log. Read from this PC after it opens.
/// Full glass over the game's own art (LOOK-17).
/// </summary>
public sealed partial class GameSavesViewModel : ObservableObject, IPageSurface
{
    private readonly LauncherActions? _actions;
    private LauncherGame _game;
    private VersionId? _current;
    private int _loads;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeld), nameof(IsConflict), nameof(NeedsYou), nameof(IsPlaying), nameof(BackUpTip))]
    private GameStatus? _status;

    [ObservableProperty]
    private string? _statusLabel;

    [ObservableProperty]
    private string? _sentence;

    /// <summary>The page's content has been read: until then only the header and status show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing))]
    private bool _hasDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing))]
    private bool _syncs;

    [ObservableProperty]
    private string? _foundBy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaces), nameof(NothingFound))]
    private IReadOnlyList<PlaceItem> _places = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private IReadOnlyList<PlaceItem> _suggestions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNamedSaves), nameof(NoNamedSaves))]
    private IReadOnlyList<NamedSaveItem> _namedSaves = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersions), nameof(NoVersions))]
    private IReadOnlyList<VersionItem> _versions = [];

    [ObservableProperty]
    private string _historySummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLog))]
    private IReadOnlyList<LogLine> _logLines = [];

    /// <summary>The name Save as… is typing.</summary>
    [ObservableProperty]
    private string _saveName = "";

    /// <param name="back">Back and the breadcrumb: every game's saves, or the page the person came from.</param>
    /// <param name="root">What the breadcrumb's first step says: "Save manager".</param>
    public GameSavesViewModel(LauncherGame game, LauncherActions? actions, ICommand back, string root = "Save manager")
    {
        _actions = actions;
        _game = game;
        BackCommand = back;
        Root = root;
        SyncGameCommand = new RelayCommand(() => _actions?.SyncGame?.Invoke(Id), () => _actions?.SyncGame is not null);
        BackUpNowCommand = new RelayCommand(() => _actions?.BackUpNow?.Invoke(Id), () => _actions?.BackUpNow is not null);
        SaveAsCommand = new RelayCommand(() =>
        {
            var name = SaveName.Trim();
            if (name.Length > 0)
            {
                _actions?.SaveAs?.Invoke(Id, name);
                SaveName = "";
            }
        }, () => _actions?.SaveAs is not null);
        RestoreCommand = new RelayCommand<object>(item =>
        {
            switch (item)
            {
                case NamedSaveItem named:
                    _actions?.Restore?.Invoke(Id, named.Version, named.Name);
                    break;
                case VersionItem version:
                    _actions?.Restore?.Invoke(Id, version.Id, null);
                    break;
            }
        }, _ => _actions?.Restore is not null);
        ApproveCommand = new RelayCommand(() => _actions?.Approve?.Invoke(Id), () => _actions?.Approve is not null);
        RestorePreviousCommand = new RelayCommand(() =>
        {
            if (_current is { } previous)
            {
                _actions?.Restore?.Invoke(Id, previous, null);
            }
        }, () => _actions?.Restore is not null);
        ChooseFilesCommand = new RelayCommand(() => _actions?.OpenProperties?.Invoke(Id, "saves"), () => _actions?.OpenProperties is not null);
        Update(game);
    }

    public GameId Id => _game.Id;

    /// <summary>What the breadcrumb's first step says.</summary>
    public string Root { get; }

    public GlassStrength Strength => GlassStrength.Glass;

    /// <summary>Glossy's backdrop: the game's own hero picture, or its cover when there's none.</summary>
    public string? BackdropArt => _game.HeroPath ?? _game.CoverPath;

    public ICommand BackCommand { get; }

    /// <summary>Sync these saves, for a game not syncing yet (FIND-06).</summary>
    public ICommand SyncGameCommand { get; }

    /// <summary>Back up now (BAK-16); the primary unless the game needs you, and not while it runs.</summary>
    public ICommand BackUpNowCommand { get; }

    /// <summary>Save as…: keeps this PC's save under the name typed (BAK-18).</summary>
    public ICommand SaveAsCommand { get; }

    /// <summary>Brings back a named save or a version; this PC's files are kept as a version first (BAK-08).</summary>
    public ICommand RestoreCommand { get; }

    /// <summary>A held save: keep the new one (BAK-12).</summary>
    public ICommand ApproveCommand { get; }

    /// <summary>A held save: bring back the one before the change, which is still the current version.</summary>
    public ICommand RestorePreviousCommand { get; }

    /// <summary>The game's Properties, on Saves: which files are backed up (FIND-12).</summary>
    public ICommand ChooseFilesCommand { get; }

    public bool IsHeld => Status == GameStatus.HeldForReview;

    public bool IsConflict => Status == GameStatus.Conflict;

    public bool NeedsYou => SyncCounts.NeedsYou(Status);

    public bool IsPlaying => Status == GameStatus.Playing;

    /// <summary>Why Back up now is off: a running game backs itself up once you quit.</summary>
    public string? BackUpTip => IsPlaying ? "Once you quit, it backs up by itself" : null;

    /// <summary>A game that syncs, once its page is read: named saves, where the saves are, and its history.</summary>
    public bool Syncing => HasDetail && Syncs;

    /// <summary>A game found but not syncing yet, once its page is read: the saves found, with Sync these saves.</summary>
    public bool NotSyncing => HasDetail && !Syncs;

    public bool HasPlaces => Places.Count > 0;

    public bool NothingFound => Places.Count == 0;

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasNamedSaves => NamedSaves.Count > 0;

    public bool NoNamedSaves => NamedSaves.Count == 0;

    public bool HasVersions => Versions.Count > 0;

    public bool NoVersions => Versions.Count == 0;

    public bool HasLog => LogLines.Count > 0;

    /// <summary>The history pane's command line: "history black-myth-wukong".</summary>
    public string HistoryCommand => $"history {Id}";

    /// <summary>The game as the library knows it now: its status may have changed since the page opened.</summary>
    public void Update(LauncherGame game)
    {
        _game = game;
        Title = game.Title;
        Status = game.Syncs ? game.Status ?? GameStatus.Synced : null;
        StatusLabel = game.Syncs ? HomeViewModel.StatusLabel(game) : null;
        Sentence = SentenceOf(game);
        OnPropertyChanged(nameof(BackdropArt));
    }

    /// <summary>What the page reads after it opens, and again whenever the games refresh.</summary>
    public async void Reload()
    {
        if (_actions?.LoadGame is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        try
        {
            var detail = await load(Id, CancellationToken.None);
            if (ticket == _loads && detail is not null)
            {
                Show(detail, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                FoundBy = $"GameSync couldn't read this game's saves on this PC: {e.Message}";
                HasDetail = true;
            }
        }
    }

    /// <summary>The page's content, from what this PC knows of the game.</summary>
    public void Show(GameDetail detail, DateTime nowLocal)
    {
        Syncs = detail.Syncs;
        FoundBy = detail.FoundBy;
        PlaceItem Place(GamePlace p) => new(p.Portable, p.Folder, p.Tag, p.Evidence,
            p.Folder is { } folder && _actions?.OpenFolder is { } open ? new RelayCommand(() => open(folder)) : null);
        Places = detail.Places.Select(Place).ToList();
        Suggestions = detail.Suggestions.Select(Place).ToList();
        NamedSaves = detail.NamedSaves.Select(n => new NamedSaveItem(n.Name, n.Version, When(n.SavedUtc, nowLocal), n.Pc.ToUpperInvariant())).ToList();
        Versions = detail.Versions.Select(v => new VersionItem(v.Id, When(v.SavedUtc, nowLocal), v.Pc.ToUpperInvariant(), v.Note, v.IsCurrent, v.IsPinned,
            Cli.FormatSize(v.Bytes), v.Uploaded)).ToList();
        _current = detail.Versions.FirstOrDefault(v => v.IsCurrent)?.Id;
        HistorySummary = detail.Versions.Count == 0 ? "no versions yet"
            : $"{(detail.Versions.Count == 1 ? "1 version" : $"{detail.Versions.Count.ToString(CultureInfo.InvariantCulture)} versions")} · {Cli.FormatSize(detail.HistoryBytes)}";
        LogLines = detail.Log.Select(l => new LogLine(When(l.AtUtc, nowLocal), l.Level switch
        {
            "error" => LogLevel.Error,
            "warn" => LogLevel.Warn,
            "ok" => LogLevel.Ok,
            _ => LogLevel.Info,
        }, l.Level, l.Message)).ToList();
        HasDetail = true;
    }

    /// <summary>"23 Sep 21:10", in a table's mono.</summary>
    public static string When(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime();
        return local.ToString(local.Year == nowLocal.Year ? "d MMM HH:mm" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>What happened to the game's save, in a sentence, and what the person can do about it.</summary>
    public static string SentenceOf(LauncherGame game) => game switch
    {
        { Syncs: false, Installed: false } => "Its saves are still on this PC. Sync them to keep them safe for when you play again.",
        { Syncs: false } => "GameSync found its saves on this PC. Nothing is backed up until you sync them.",
        { Status: GameStatus.HeldForReview } =>
            "Its save changed while the game wasn't running, so GameSync held it for you to look at. Keep the new save, or restore the previous one; either way the other stays in the history.",
        { Status: GameStatus.Conflict } => $"{game.StatusDetail ?? "It changed on two PCs."} To take the other PC's save instead, restore the version marked Kept from a conflict.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { Status: GameStatus.Playing } => "Running now. Its save syncs a few seconds after you quit.",
        { Status: GameStatus.BackupOnly } => $"{StoreNames.SyncingStore(game.Store)} syncs its saves between your PCs; GameSync keeps a backup of every version.",
        _ => "Its saves are backed up on this PC and in your Google Drive, and every version is kept.",
    };
}
