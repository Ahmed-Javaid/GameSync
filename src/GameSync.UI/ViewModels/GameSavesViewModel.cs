using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;
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

/// <summary>A named save: its name, when and on which PC it was saved, and the new name typed to rename it.</summary>
public sealed partial class NamedSaveItem(string name, VersionId version, string when, string pc, bool inPlace = false) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Its files are the game's save now (KAN-51): it says In place instead of offering Restore.</summary>
    public bool IsInPlace { get; } = inPlace;

    public bool CanRestore => !IsInPlace;

    public VersionId Version { get; } = version;

    public string When { get; } = when;

    public string Pc { get; } = pc;

    /// <summary>Rename's box: it starts as the name.</summary>
    [ObservableProperty]
    private string _newName = name;

    public string Meta => $"{When} · {Pc}";

    public override string ToString() => $"{Name}, saved {When} on {Pc}{(IsInPlace ? ", in place now" : "")}";
}

/// <summary>A version in a game's history: when it was saved, the PC, what sets it apart, its size.</summary>
public sealed record VersionItem(VersionId Id, string Saved, string Pc, string? Note, bool IsCurrent, bool IsPinned, string Size, bool Uploaded)
{
    /// <summary>The version the Versions tab opened these saves on (MGR-08), marked where its Restore is.</summary>
    public bool IsPicked { get; init; }

    /// <summary>What sets a version apart, beside a pin when it's pinned; the current one shows Current beside it ("Restored from …", KAN-51).</summary>
    public bool ShowsNote => Note is not null;

    public bool ShowsPin => ShowsNote && IsPinned;

    public bool CanRestore => !IsCurrent;

    public override string ToString() => string.Join(", ", new[] { Saved, Pc, IsCurrent ? "current" : null, Note, Size, Uploaded ? null : "not uploaded yet" }.OfType<string>());
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
    [NotifyPropertyChangedFor(nameof(IsHeld), nameof(IsConflict), nameof(IsFilesInUse), nameof(IsSavesMissing), nameof(NeedsYou), nameof(IsPlaying), nameof(BackUpTip),
        nameof(ShowsSettled), nameof(IsKeptOnly))]
    private GameStatus? _status;

    /// <summary>The last conflict GameSync settled, while its winner is still the current save: which PC's was kept, and Swap (SYNC-04).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSettled), nameof(SwapLabel))]
    private ConflictDetail? _settled;

    [ObservableProperty]
    private string? _statusLabel;

    [ObservableProperty]
    private string? _sentence;

    /// <summary>The page's content has been read: until then only the header and status show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing))]
    private bool _hasDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing), nameof(IsKeptOnly))]
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
                    Restore(named.Version, named.Name, $"“{named.Name}”");
                    break;
                case VersionItem version:
                    Restore(version.Id, null, $"the save from {version.Saved}");
                    break;
            }
        }, _ => _actions?.Restore is not null);
        ApproveCommand = new RelayCommand(() => _actions?.Approve?.Invoke(Id), () => _actions?.Approve is not null);
        RestorePreviousCommand = new RelayCommand(() =>
        {
            if (_current is { } previous)
            {
                Restore(previous, null, "the save from before the change");
            }
        }, () => _actions?.Restore is not null);
        ChooseFilesCommand = new RelayCommand(() => _actions?.OpenProperties?.Invoke(Id, "saves"), () => _actions?.OpenProperties is not null);
        RenameSaveCommand = new RelayCommand<NamedSaveItem>(named =>
        {
            var name = named?.NewName.Trim() ?? "";
            if (named is not null && name.Length > 0 && name != named.Name)
            {
                _actions?.RenameSave?.Invoke(Id, named.Name, name);
            }
        }, _ => _actions?.RenameSave is not null);
        ForgetSaveCommand = new RelayCommand<NamedSaveItem>(named =>
        {
            if (named is not null)
            {
                _actions?.ForgetSave?.Invoke(Id, named.Name);
            }
        }, _ => _actions?.ForgetSave is not null);
        ResolveCommand = new RelayCommand(() => _actions?.OpenConflict?.Invoke(Id), () => _actions?.OpenConflict is not null);
        RetryCommand = new RelayCommand(() => _actions?.Retry?.Invoke(Id), () => _actions?.Retry is not null);
        SwapCommand = new RelayCommand(() => _actions?.Swap?.Invoke(Id), () => _actions?.Swap is not null);
        OpenAddPlaceCommand = new RelayCommand(() => _actions?.OpenAddPlace?.Invoke(Id), () => _actions?.OpenAddPlace is not null);
        OpenImportKeptCommand = new RelayCommand(() => _actions?.OpenImportKept?.Invoke(Id), () => _actions?.OpenImportKept is not null);
        KeepAndImportCommand = new AsyncRelayCommand(async () =>
        {
            // KAN-63: a game not syncing yet is kept first (backed up, not synced between PCs), so its places are known.
            if (_actions?.Keep is { } keep && await keep(Id))
            {
                _actions.OpenImportKept?.Invoke(Id);
            }
        }, () => _actions?.Keep is not null && _actions.OpenImportKept is not null);
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

    /// <summary>A named save's Rename, with the name typed in its box (BAK-18).</summary>
    public ICommand RenameSaveCommand { get; }

    /// <summary>A named save's Remove the name: the save stays in the history, unnamed (BAK-18).</summary>
    public ICommand ForgetSaveCommand { get; }

    /// <summary>A conflict: both saves side by side, to choose one (SYNC-10). A settled one's See both opens it too.</summary>
    public ICommand ResolveCommand { get; }

    /// <summary>Files in use: sync the game again, once the program holding them let go (SYNC-02).</summary>
    public ICommand RetryCommand { get; }

    /// <summary>A settled conflict: switch to the save that lost (SYNC-04). It restores, so the page asks first.</summary>
    public ICommand SwapCommand { get; }

    /// <summary>Add a place: a folder or file where it keeps saves GameSync didn't find (FOLD-01).</summary>
    public ICommand OpenAddPlaceCommand { get; }

    /// <summary>Import kept saves: copies of its saves kept by hand, as named saves (BAK-19).</summary>
    public ICommand OpenImportKeptCommand { get; }

    /// <summary>Import kept saves… for a game not syncing yet: GameSync keeps its saves first, then the dialog opens (KAN-63).</summary>
    public ICommand KeepAndImportCommand { get; }

    /// <summary>
    /// KAN-63: kept by the person's choice, backed up only, not synced between their PCs (a store's cloud game backs up
    /// only because its store syncs it): the status offers Sync between PCs.
    /// </summary>
    public bool IsKeptOnly => Syncs && Status == GameStatus.BackupOnly && !_game.StoreSyncs;

    /// <summary>What Save as…, Back up now and Import kept saves… say first for a game not syncing yet (KAN-63).</summary>
    public static string KeepNote =>
        "GameSync starts keeping its saves: every version backed up on this PC and in the cloud. They don't sync between your PCs until you choose Sync these saves.";

    /// <summary>
    /// The same, with what the last scan found, so a big folder (Bloodborne's, with its copies kept by hand) is no surprise:
    /// "GameSync starts keeping its saves, 1,051 files and 1022.2 MB as they are now: …".
    /// </summary>
    public static string KeepNoteFor(int files, long bytes) => files <= 0 ? KeepNote
        : $"GameSync starts keeping its saves, {(files == 1 ? "1 file" : $"{files.ToString("N0", CultureInfo.InvariantCulture)} files")} and {Cli.FormatSize(bytes).Replace(' ', ' ')} as they are now: every version backed up on this PC and in the cloud. They don't sync between your PCs until you choose Sync these saves.";

    /// <summary>What the buttons say first on this game, with its size (<see cref="KeepNoteFor"/>).</summary>
    [ObservableProperty]
    private string _keepLine = KeepNote;

    /// <summary>A conflict GameSync settled by itself or you did, and nothing else needs you: which save is current, with Swap.</summary>
    public bool ShowsSettled => Settled is not null && !NeedsYou && !IsPlaying;

    public string SettledText => Settled is not { } s ? ""
        : s.ByHand ? $"You kept {s.KeptPc}'s save; {s.PinnedPc}'s is pinned in history."
        : $"Newest kept: {s.KeptPc}'s save is current, {s.PinnedPc}'s is pinned in history.";

    public string SwapLabel => Settled is { PinnedPc: { } pc } ? $"Swap to {pc}'s" : "Swap";

    public bool IsHeld => Status == GameStatus.HeldForReview;

    public bool IsConflict => Status == GameStatus.Conflict;

    public bool IsFilesInUse => Status == GameStatus.FilesInUse;

    /// <summary>Its saves weren't found where its places say: Add a place sets one by hand.</summary>
    public bool IsSavesMissing => Status is GameStatus.SavesMissing or GameStatus.NoSaves;

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

    /// <summary>The version the Versions tab opened these saves on (MGR-08); null when they were opened another way.</summary>
    public VersionId? Picked { get; private set; }

    /// <summary>
    /// KAN-51: what a restore is doing, then that it's done ("“Before the lighthouse” is back in place…") or why it
    /// couldn't be; null before any. It stays while the page is open, so the person sees how it ended.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestoreNote), nameof(RestoreDone))]
    private string? _restoreNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreDone))]
    private bool _restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreDone))]
    private bool _restoreFailed;

    public bool HasRestoreNote => RestoreNote is not null;

    public bool RestoreDone => HasRestoreNote && !Restoring && !RestoreFailed;

    /// <summary>One question was asked already (the flyout); this brings the save back and says how it went.</summary>
    private async void Restore(VersionId version, string? name, string what)
    {
        if (_actions?.Restore is not { } restore || Restoring)
        {
            return;
        }

        (Restoring, RestoreFailed, RestoreNote) = (true, false, $"Bringing back {what}…");
        var problem = await restore(Id, version, name);
        (Restoring, RestoreFailed) = (false, problem is not null);
        RestoreNote = problem ?? $"{char.ToUpperInvariant(what[0])}{what[1..]} is back in place. The files it replaced are kept in the history, so you can go back to them.";
    }

    /// <summary>Marks a version in the history, where its Restore is, as a row of the Versions tab opens it (MGR-08).</summary>
    public void Pick(VersionId version)
    {
        Picked = version;
        Versions = Versions.Select(v => v with { IsPicked = v.Id == version }).ToList();
    }

    /// <summary>The page's content, from what this PC knows of the game.</summary>
    public void Show(GameDetail detail, DateTime nowLocal)
    {
        Syncs = detail.Syncs;
        FoundBy = detail.FoundBy;
        KeepLine = KeepNoteFor(detail.FoundFiles, detail.FoundBytes);
        PlaceItem Place(GamePlace p) => new(p.Portable, p.Folder, p.Tag, p.Evidence,
            p.Folder is { } folder && _actions?.OpenFolder is { } open ? new RelayCommand(() => open(folder)) : null);
        Places = detail.Places.Select(Place).ToList();
        Suggestions = detail.Suggestions.Select(Place).ToList();
        NamedSaves = detail.NamedSaves.Select(n => new NamedSaveItem(n.Name, n.Version, When(n.SavedUtc, nowLocal), n.Pc.ToUpperInvariant(), n.InPlace)).ToList();
        Versions = detail.Versions.Select(v => new VersionItem(v.Id, When(v.SavedUtc, nowLocal), v.Pc.ToUpperInvariant(), v.Note, v.IsCurrent, v.IsPinned,
            Cli.FormatSize(v.Bytes), v.Uploaded) { IsPicked = v.Id == Picked }).ToList();
        _current = detail.Versions.FirstOrDefault(v => v.IsCurrent)?.Id;
        HistorySummary = detail.Versions.Count == 0 ? "no versions yet"
            : $"{(detail.Versions.Count == 1 ? "1 version" : $"{detail.Versions.Count.ToString(CultureInfo.InvariantCulture)} versions")} · {Cli.FormatSize(detail.HistoryBytes)}";
        Settled = detail.Conflict is { Waiting: false } settled ? settled : null;
        OnPropertyChanged(nameof(SettledText));
        LogLines = detail.Log.Select(l => ActivityLog.Line(When(l.AtUtc, nowLocal), l.Level, l.Tag, l.Message)).ToList();
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
        { Syncs: false } => "GameSync found its saves on this PC. Nothing is backed up yet.",
        { Status: GameStatus.HeldForReview } =>
            "Its save changed while the game wasn't running, so GameSync held it for you to look at. Keep the new save, or restore the previous one; either way the other stays in the history.",
        { Status: GameStatus.Conflict } =>
            $"{DecisionEngine.WaitingReason(game.StatusDetail) ?? "It changed on two PCs since they last agreed."} Both copies are safe; Resolve shows them side by side.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { Status: GameStatus.Playing } => "Running now. Its save syncs a few seconds after you quit.",
        { FirstBackupPending: true } => "It isn't backed up yet: its first backup happens in a moment, when GameSync next syncs. Back up now does it at once.",
        { Status: GameStatus.BackupOnly, StoreSyncs: false } =>
            "GameSync keeps every version of its saves, backed up on this PC and in the cloud, and they don't sync between your PCs. Sync between PCs keeps them in step.",
        { Status: GameStatus.BackupOnly } => $"{StoreNames.SyncingStore(game.Store)} syncs its saves between your PCs; GameSync keeps a backup of every version.",
        { Cloud: { } cloud } => $"Its saves are backed up on this PC and in {cloud}, and every version is kept.",
        _ => "Its saves are backed up on this PC, and every version is kept; they go up once you connect a cloud.",
    };
}
