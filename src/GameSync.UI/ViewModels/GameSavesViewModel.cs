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

    /// <summary>
    /// KAN-81: what's kept there, in words, then its numbers: "The folder SPRJ0005 and everything in it · 34 files ·
    /// 33.6 MB · newest …", so it's plain the folder around it isn't part of the save. A place taking some files only,
    /// or a registry key, says its numbers alone.
    /// </summary>
    public string Caption => Tag is null && Folder is { } folder && !Portable.Contains("  (", StringComparison.Ordinal)
        && Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } leaf
        ? $"The folder {leaf} and everything in it · {Evidence}"
        : Evidence;

    /// <summary>R2 (design system version 51): the program files found here, by their full paths; never backed up (R1).</summary>
    public IReadOnlyList<string> Programs { get; init; } = [];

    /// <summary>"A program is in this folder and isn't backed up: Uninstall.exe. Programs never travel with saves; …"</summary>
    public string? ProgramNote
    {
        get
        {
            var names = Programs.Select(Path.GetFileName).OfType<string>().ToList();
            return names.Count switch
            {
                0 => null,
                1 => $"A program is in this folder and isn't backed up: {names[0]}. Programs never travel with saves; if you didn't put it there, check where it came from.",
                _ => $"{names.Count} programs are in this folder and aren't backed up: " +
                    (names.Count == 2 ? $"{names[0]} and {names[1]}" : $"{names[0]}, {names[1]} and {names.Count - 2} more") +
                    ". Programs never travel with saves; if you didn't put them there, check where they came from.",
            };
        }
    }

    public bool HasProgramNote => Programs.Count > 0;

    public override string ToString() => string.Join(", ", new[] { Folder ?? Portable, Tag, Evidence, ProgramNote }.OfType<string>());
}

/// <summary>A named save: its name, when and on which PC it was saved, and the new name typed to rename it.</summary>
public sealed partial class NamedSaveItem(string name, VersionId version, string when, string pc, bool inPlace = false, string game = "The game") : ObservableObject
{
    /// <summary>KAN-89: Restore's ask: "Restore Bloodborne GOTY's save to “befo ludwig”?".</summary>
    public string Question => $"Restore {game}'s save to “{Name}”?";

    /// <summary>What the ask says under it: when and where it was saved, and that the save there now is kept first.</summary>
    public string Detail => $"Saved {When} on {Pc}. Your current save is kept first, so you can go back to it.";

    /// <summary>KAN-80: being restored now: its Restore says Restoring….</summary>
    [ObservableProperty]
    private bool _isRestoring;

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

    /// <summary>KAN-87: where a named save is: no folder of its own, its files stored once by content.</summary>
    public static string WhereKept =>
        "In GameSync's history: its files are stored once, by their contents, in the backup folder on this PC and in the cloud, so saves that share files take their space once. It has no folder of its own; Export as a folder puts a plain copy wherever you choose.";

    public override string ToString() => $"{Name}, saved {When} on {Pc}{(IsInPlace ? ", in place now" : "")}";
}

/// <summary>A version in a game's history: when it was saved, the PC, what sets it apart, its size.</summary>
public sealed record VersionItem(VersionId Id, string Saved, string Pc, string? Note, bool IsCurrent, bool IsPinned, string Size, bool Uploaded)
{
    /// <summary>The version the Versions tab opened these saves on (MGR-08), marked where its Restore is.</summary>
    public bool IsPicked { get; init; }

    /// <summary>KAN-80: being restored now: its Restore says Restoring….</summary>
    public bool IsRestoring { get; init; }

    /// <summary>KAN-89: Restore's ask: "Restore Lantern Keep's save to the one from 23 Sep 21:02?".</summary>
    public string Question { get; init; } = $"Restore the save from {Saved}?";

    public string Detail => $"Saved on {Pc}. Your current save is kept first, so you can go back to it.";

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
    [NotifyPropertyChangedFor(nameof(IsHeld), nameof(IsConflict), nameof(IsFilesInUse), nameof(IsSavesMissing), nameof(NeedsYou), nameof(IsPlaying), nameof(BackUpTip), nameof(RunningNote), nameof(HasRunningNote),
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
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing), nameof(NotSyncingPlain), nameof(NotSyncingKept))]
    private bool _hasDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syncing), nameof(NotSyncing), nameof(IsKeptOnly), nameof(NotSyncingPlain), nameof(NotSyncingKept))]
    private bool _syncs;

    /// <summary>KAN-61: a game not syncing yet whose live save sits beside copies kept by hand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotSyncingPlain), nameof(NotSyncingKept), nameof(KeptTitle), nameof(KeptMeta))]
    private GameKept? _kept;

    [ObservableProperty]
    private string? _foundBy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaces), nameof(NothingFound))]
    private IReadOnlyList<PlaceItem> _places = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private IReadOnlyList<PlaceItem> _suggestions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNamedSaves), nameof(NoNamedSaves), nameof(NamedTitle), nameof(HasNamedTools))]
    private IReadOnlyList<NamedSaveItem> _namedSaves = [];

    /// <summary>KAN-82: what Find a named save holds; the list shows the named saves whose names have it.</summary>
    [ObservableProperty]
    private string _namedSearch = "";

    /// <summary>KAN-82: <c>newest</c> first (the default) or by <c>name</c>, kept per PC.</summary>
    [ObservableProperty]
    private string _namedSort = "newest";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoNamedMatch))]
    private IReadOnlyList<NamedSaveItem> _shownNamedSaves = [];

    /// <summary>KAN-92: the live save as the game has it now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent))]
    private string? _currentWritten;

    [ObservableProperty]
    private string? _currentSize;

    [ObservableProperty]
    private string? _currentBackedUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSameAs))]
    private string? _currentSameAs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentInUse))]
    private string? _currentInUse;

    /// <summary>KAN-92: the Current save card's facts, as a game's page shows its own.</summary>
    [ObservableProperty]
    private IReadOnlyList<Fact> _currentFacts = [];

    /// <summary>KAN-87: an export as a folder: what happened, the folder made, and whether it failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportNote), nameof(ExportDone))]
    private string? _exportNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDone))]
    private bool _exporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDone))]
    private bool _exportFailed;

    private string? _exportedFolder;
    private string? _backupFolder;
    private bool _sortRead;

    /// <summary>KAN-84: it ships an anti-cheat, so its saves can't be shared (R16).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareTip))]
    private bool _hasAntiCheat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersions), nameof(NoVersions))]
    private IReadOnlyList<VersionItem> _versions = [];

    [ObservableProperty]
    private string _historySummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLog))]
    private IReadOnlyList<LogLine> _logLines = [];

    /// <param name="back">Back and the breadcrumb: every game's saves, or the page the person came from.</param>
    /// <param name="root">What the breadcrumb's first step says: "Save manager".</param>
    public GameSavesViewModel(LauncherGame game, LauncherActions? actions, ICommand back, string root = "Save manager")
    {
        _actions = actions;
        _game = game;
        BackCommand = back;
        Root = root;
        Transfer = actions?.TransferOf?.Invoke(game.Id);
        SyncGameCommand = new AsyncRelayCommand(async () =>
        {
            // KAN-61: only the live save, the copies beside it as named saves, in its dialog.
            if (NotSyncingKept)
            {
                _actions?.OpenKeptCopies?.Invoke(new KeptCopiesStart(Id, Title, Backup: false));
            }
            else if (_actions?.SyncGame is { } sync)
            {
                await sync(Id);
            }
        }, () => _actions?.SyncGame is not null);
        OpenKeptCommand = new RelayCommand<string>(mode => _actions?.OpenKeptCopies?.Invoke(new KeptCopiesStart(Id, Title, Backup: mode == "backup")),
            _ => _actions?.OpenKeptCopies is not null);
        BackUpNowCommand = new AsyncRelayCommand(async () =>
        {
            if (_actions?.BackUpNow is { } backUp)
            {
                await backUp(Id);
            }
        }, () => _actions?.BackUpNow is not null);
        OpenNamedSaveCommand = new RelayCommand(() =>
        {
            if (NotSyncingKept)
            {
                _actions?.OpenKeptCopies?.Invoke(new KeptCopiesStart(Id, Title, Backup: true));
                return;
            }

            _actions?.OpenNamedSave?.Invoke(NamedSaveStart.For(Id, Title, Places.Select(p => (p.Folder, p.Tag, p.Evidence)), NamedSaves.Select(n => n.Name),
                Syncs ? null : KeepLine));
        }, () => _actions?.OpenNamedSave is not null);
        RestoreCommand = new RelayCommand<object>(item =>
        {
            switch (item)
            {
                case NamedSaveItem named:
                    Restore(named.Version, named.Name, $"“{named.Name}”", item);
                    break;
                case VersionItem version:
                    Restore(version.Id, null, $"the save from {version.Saved}", item);
                    break;
            }
        }, _ => _actions?.Restore is not null);
        ApproveCommand = new RelayCommand(() => _actions?.Approve?.Invoke(Id), () => _actions?.Approve is not null);
        ExportCommand = new RelayCommand<object>(item =>
        {
            // Export (KAN-23, KAN-85): the share window with this save picked, to put it in a zip.
            switch (item)
            {
                case VersionItem version:
                    _actions?.OpenShare?.Invoke(new ShareStart(ShareStart.Pick, [Id], Id, version.Id));
                    break;
                case NamedSaveItem named:
                    _actions?.OpenShare?.Invoke(new ShareStart(ShareStart.Pick, [Id], Id, named.Version));
                    break;
            }
        }, _ => _actions?.OpenShare is not null);
        ShareCommand = new RelayCommand(() => _actions?.OpenShare?.Invoke(new ShareStart(ShareStart.Pick, [Id], Id)),
            () => _actions?.OpenShare is not null);
        OpenBackupCommand = new RelayCommand(() =>
        {
            if (_backupFolder is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
        OpenExportedCommand = new RelayCommand(() =>
        {
            if (_exportedFolder is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
        RestorePreviousCommand = new RelayCommand(() =>
        {
            if (_current is { } previous)
            {
                Restore(previous, null, "the save from before the change", null);
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

    /// <summary>KAN-80: its upload or download, shown under its status as it goes; null where the page only shows.</summary>
    public TransferView? Transfer { get; }

    /// <summary>Sync these saves, for a game not syncing yet (FIND-06); Syncing… until its saves are kept here (KAN-80).</summary>
    public IAsyncRelayCommand SyncGameCommand { get; }

    /// <summary>Back up now (BAK-16); the primary unless the game needs you, and not while it runs. Backing up… until it's kept (KAN-80).</summary>
    public IAsyncRelayCommand BackUpNowCommand { get; }

    /// <summary>New named save… (BAK-18): this PC's save kept under a name, in its dialog (KAN-77).</summary>
    public ICommand OpenNamedSaveCommand { get; }

    /// <summary>KAN-61: the live save and the copies beside it, synced ("sync") or backed up only ("backup").</summary>
    public ICommand OpenKeptCommand { get; }

    /// <summary>A game not syncing yet, as before KAN-61: each button says first that GameSync starts keeping its saves.</summary>
    public bool NotSyncingPlain => NotSyncing && Kept is null;

    /// <summary>KAN-61: a game not syncing yet whose live save sits beside copies kept by hand; its buttons open KeptCopiesDialog.</summary>
    public bool NotSyncingKept => NotSyncing && Kept is not null;

    public string KeptTitle => Kept is { } kept ? $"Beside it: {Copies(kept.Copies)} you kept by hand" : "";

    public string KeptMeta => Kept is { } kept
        ? string.Join(" · ", new[]
        {
            kept.Names.Count > 0 ? $"In {Path.GetFileName(kept.Folder)}, such as {string.Join(" and ", kept.Names)}" : $"In {Path.GetFileName(kept.Folder)}",
            Cli.FormatSize(kept.CopiesBytes),
            "not part of the live save",
        })
        : "";

    private static string Copies(int count) => count == 1 ? "1 copy" : $"{count.ToString(CultureInfo.InvariantCulture)} copies";

    /// <summary>Brings back a named save or a version; this PC's files are kept as a version first (BAK-08).</summary>
    public ICommand RestoreCommand { get; }

    /// <summary>A held save: keep the new one (BAK-12).</summary>
    public ICommand ApproveCommand { get; }

    /// <summary>Export (KAN-23, KAN-85): the share window with this version or named save picked, to put it in a zip.</summary>
    public ICommand ExportCommand { get; }

    /// <summary>KAN-84: Share…, the share window with this game's current save ticked; another of its saves can be picked there.</summary>
    public ICommand ShareCommand { get; }

    /// <summary>KAN-87: the backup folder this PC keeps the game's history in, in Explorer.</summary>
    public ICommand OpenBackupCommand { get; }

    /// <summary>KAN-87: the folder an export made, in Explorer.</summary>
    public ICommand OpenExportedCommand { get; }

    /// <summary>KAN-84: Share…'s tooltip, saying why it's off for a game with an anti-cheat.</summary>
    public string ShareTip => HasAntiCheat
        ? "It ships an anti-cheat, so its saves can't be shared"
        : "Puts its current save, or any named save or version, in a zip a friend can import";

    public bool CanShare => !HasAntiCheat;

    /// <summary>KAN-82: "Named saves · 36", the count in the heading.</summary>
    public string NamedTitle => NamedSaves.Count == 0 ? "Named saves" : $"Named saves · {NamedSaves.Count.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The search and the sort, once there's more than one to find or order.</summary>
    public bool HasNamedTools => NamedSaves.Count > 1;

    public bool NoNamedMatch => NamedSaves.Count > 0 && ShownNamedSaves.Count == 0;

    public string NoMatchText => $"No named save is called anything like “{NamedSearch.Trim()}”.";

    public IReadOnlyList<SelectOption> NamedSortOptions { get; } = [new("newest", "Newest first"), new("name", "Name")];

    public bool HasCurrent => CurrentWritten is not null;

    public bool HasSameAs => CurrentSameAs is not null;

    public bool HasCurrentInUse => CurrentInUse is not null;

    public bool HasExportNote => ExportNote is not null;

    public bool ExportDone => HasExportNote && !Exporting && !ExportFailed;

    partial void OnNamedSearchChanged(string value)
    {
        Filter();
        OnPropertyChanged(nameof(NoMatchText));
    }

    partial void OnNamedSortChanged(string value)
    {
        Filter();
        if (_sortRead)
        {
            _actions?.SetNamedSort?.Invoke(value);
        }
    }

    partial void OnNamedSavesChanged(IReadOnlyList<NamedSaveItem> value) => Filter();

    partial void OnHasAntiCheatChanged(bool value) => OnPropertyChanged(nameof(CanShare));

    /// <summary>KAN-82: the named saves whose names have what's typed, newest first or by name.</summary>
    private void Filter()
    {
        var wanted = NamedSearch.Trim();
        var shown = NamedSaves.Where(n => wanted.Length == 0 || n.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase));
        ShownNamedSaves = (NamedSort == "name" ? shown.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase) : shown).ToList();
    }

    /// <summary>
    /// KAN-87: a named save as a plain folder in <paramref name="parent"/> (picked in Windows' folder picker), named after
    /// it; then a line under the status saying where, with Open, or why it couldn't be.
    /// </summary>
    public async Task ExportFolderAsync(NamedSaveItem named, string parent)
    {
        if (_actions?.ExportFolder is not { } export || Exporting)
        {
            return;
        }

        (Exporting, ExportFailed, ExportNote) = (true, false, $"Exporting “{named.Name}” as a folder");
        var (folder, problem) = await export(Id, named.Version, named.Name, parent);
        (Exporting, ExportFailed, _exportedFolder) = (false, problem is not null, folder);
        ExportNote = problem ?? $"“{named.Name}” is a folder now: {folder}. GameSync keeps it in its history as before.";
    }

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

    /// <summary>The game is running now, by its status or by the agent seeing it run (PLAY-12).</summary>
    public bool IsPlaying => Status == GameStatus.Playing || _game.IsRunning;

    /// <summary>
    /// KAN-91: Back up now works while the game runs, as New named save does (the owner, 2 Oct 2026): it keeps the save as
    /// it is at that moment here, and the upload waits until the game closes (BG-08).
    /// </summary>
    public string BackUpTip => IsPlaying
        ? "Keeps the save as it is now, on this PC; it uploads once you quit"
        : "Keeps a version of the save as it is now, on this PC and in the cloud, without waiting for the game to close";

    /// <summary>
    /// KAN-91: Restore's ask while the game runs: it goes ahead (the owner, 2 Oct 2026), and says what to expect. A file the
    /// game holds stops it, saying which, with nothing changed.
    /// </summary>
    public string? RunningNote => IsPlaying
        ? $"{Title} is running. Load the save again from its menu afterwards; if it saves as you quit, it may write over it."
        : null;

    public bool HasRunningNote => RunningNote is not null;

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

        // A game the agent sees running reads Playing here as everywhere (PLAY-12), whatever its last sync said.
        Status = game.Syncs ? game.IsRunning && !game.NeedsYou ? GameStatus.Playing : game.Status ?? GameStatus.Synced : null;
        StatusLabel = game.Syncs && !game.IsRunning ? HomeViewModel.StatusLabel(game) : null;
        Sentence = KeptSentence() ?? LearnSentence() ?? SentenceOf(game);
        OnPropertyChanged(nameof(BackdropArt));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(BackUpTip));
        OnPropertyChanged(nameof(RunningNote));
        OnPropertyChanged(nameof(HasRunningNote));
    }

    /// <summary>KAN-61: what the scan found, for a game not syncing yet whose live save sits beside copies kept by hand.</summary>
    private string? KeptSentence() => !_game.Syncs && Kept is { } kept
        ? $"GameSync found its live save, {Path.GetFileName(kept.LiveFolder)}, and {Copies(kept.Copies)} of it you kept by hand beside it. Nothing is backed up yet: Sync these saves syncs the live save and brings the copies in as named saves."
        : null;

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

    /// <summary>
    /// One question was asked already (the flyout); this brings the save back and says how it went. Meanwhile the row's
    /// Restore says Restoring… and a line under the status says what's coming back, both moving (KAN-80).
    /// </summary>
    private async void Restore(VersionId version, string? name, string what, object? item)
    {
        if (_actions?.Restore is not { } restore || Restoring)
        {
            return;
        }

        Busy(item, true);
        (Restoring, RestoreFailed, RestoreNote) = (true, false, $"Bringing back {what}");
        var problem = await restore(Id, version, name);
        Busy(item, false);
        (Restoring, RestoreFailed) = (false, problem is not null);
        RestoreNote = problem ?? $"{char.ToUpperInvariant(what[0])}{what[1..]} is back in place. The files it replaced are kept in the history, so you can go back to them.";
    }

    /// <summary>The row whose Restore was pressed says Restoring… while it runs.</summary>
    private void Busy(object? item, bool restoring)
    {
        switch (item)
        {
            case NamedSaveItem named:
                named.IsRestoring = restoring;
                break;
            case VersionItem version:
                Versions = Versions.Select(v => v.Id == version.Id ? v with { IsRestoring = restoring } : v).ToList();
                break;
        }
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
        Kept = detail.Syncs ? null : detail.Kept;
        Sentence = KeptSentence() ?? SentenceOf(_game);
        FoundBy = detail.FoundBy;
        KeepLine = KeepNoteFor(detail.FoundFiles, detail.FoundBytes);
        PlaceItem Place(GamePlace p) => new(p.Portable, p.Folder, p.Tag, p.Evidence,
            p.Folder is { } folder && _actions?.OpenFolder is { } open ? new RelayCommand(() => open(folder)) : null) { Programs = p.Programs };
        Places = detail.Places.Select(Place).ToList();
        Suggestions = detail.Suggestions.Select(Place).ToList();
        NamedSaves = detail.NamedSaves.Select(n => new NamedSaveItem(n.Name, n.Version, When(n.SavedUtc, nowLocal), n.Pc.ToUpperInvariant(), n.InPlace, Title)).ToList();
        Versions = detail.Versions.Select(v => new VersionItem(v.Id, When(v.SavedUtc, nowLocal), v.Pc.ToUpperInvariant(), v.Note, v.IsCurrent, v.IsPinned,
            Cli.FormatSize(v.Bytes), v.Uploaded)
        {
            IsPicked = v.Id == Picked,
            Question = $"Restore {Title}'s save to the one from {When(v.SavedUtc, nowLocal)}?",
        }).ToList();
        _current = detail.Versions.FirstOrDefault(v => v.IsCurrent)?.Id;
        HistorySummary = detail.Versions.Count == 0 ? "no versions yet"
            : $"{(detail.Versions.Count == 1 ? "1 version" : $"{detail.Versions.Count.ToString(CultureInfo.InvariantCulture)} versions")} · {Cli.FormatSize(detail.HistoryBytes)}";
        Settled = detail.Conflict is { Waiting: false } settled ? settled : null;
        OnPropertyChanged(nameof(SettledText));
        HasAntiCheat = detail.HasAntiCheat;
        _backupFolder = detail.BackupFolder;
        if (!_sortRead)
        {
            // The order kept on this PC, once; what the person picks after is kept as they pick it.
            NamedSort = detail.NamedSort;
            _sortRead = true;
        }

        ShowCurrent(detail.Current, nowLocal);
        LogLines = detail.Log.Select(l => ActivityLog.Line(When(l.AtUtc, nowLocal), l.Level, l.Tag, l.Message)).ToList();
        HasDetail = true;

        // FIND-04: learn mode, once the places are known, and the status line as it has it.
        Learn = detail.Learn;
        Sentence = KeptSentence() ?? LearnSentence() ?? SentenceOf(_game);
        foreach (var name in new[] { nameof(ShowsLearn), nameof(LearnNote), nameof(HasLearnNote), nameof(SeesLearnFinds), nameof(NothingFoundNote), nameof(LearnNothingFoundNote), nameof(ChoosesFiles) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>KAN-92: the Current save card's facts, from the live save as it is now.</summary>
    private void ShowCurrent(CurrentSave? current, DateTime nowLocal)
    {
        if (current is null)
        {
            (CurrentWritten, CurrentSize, CurrentBackedUp, CurrentSameAs, CurrentInUse, CurrentFacts) = (null, null, null, null, null, []);
            return;
        }

        if (current.InUse is { } held)
        {
            (CurrentWritten, CurrentSize, CurrentBackedUp, CurrentSameAs) = ("Now, by the game", null, null, null);
            CurrentInUse = $"{Title} has {held} open, so it can't be read right now.";
            CurrentFacts = [new Fact("Last written", CurrentWritten)];
            return;
        }

        CurrentInUse = null;
        CurrentWritten = current.NewestUtc is { } newest ? When(newest, nowLocal) : "Unknown";
        CurrentSize = $"{(current.Files == 1 ? "1 file" : $"{current.Files.ToString("N0", CultureInfo.InvariantCulture)} files")} · {Cli.FormatSize(current.Bytes)}";
        var cloud = _game.Cloud;
        CurrentBackedUp = current.BackedUpUtc is not { } backed
            ? Versions.Count == 0 ? "Not yet" : "Not since it last changed: Back up now keeps it as a version"
            : current.Uploaded && cloud is not null ? $"{When(backed, nowLocal)}, on this PC and in {cloud}"
            : cloud is not null ? $"{When(backed, nowLocal)}, on this PC; it uploads next"
            : $"{When(backed, nowLocal)}, on this PC";
        CurrentSameAs = current.SameAs is { } same
            ? $"“{same}”{(current.RestoredUtc is { } restored ? $", restored {When(restored, nowLocal)}" : "")}"
            : null;
        CurrentFacts = new[]
        {
            new Fact("Last written", CurrentWritten),
            new Fact("Size", CurrentSize, Mono: true),
            new Fact("Backed up", CurrentBackedUp),
            CurrentSameAs is null ? null : new Fact("Same as", CurrentSameAs),
        }.OfType<Fact>().ToList();
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
        { IsRunning: true } => "Running now. Its save syncs a few seconds after you quit; Back up now and named saves keep it as it is meanwhile.",
        { FirstBackupPending: true } => "It isn't backed up yet: its first backup happens in a moment, when GameSync next syncs. Back up now does it at once.",
        { Status: GameStatus.BackupOnly, StoreSyncs: false } =>
            "GameSync keeps every version of its saves, backed up on this PC and in the cloud, and they don't sync between your PCs. Sync between PCs keeps them in step.",
        { Status: GameStatus.BackupOnly } => $"{StoreNames.SyncingStore(game.Store)} syncs its saves between your PCs; GameSync keeps a backup of every version.",
        { Cloud: { } cloud } => $"Its saves are backed up on this PC and in {cloud}, and every version is kept.",
        _ => "Its saves are backed up on this PC, and every version is kept; they go up once you connect a cloud.",
    };
}
