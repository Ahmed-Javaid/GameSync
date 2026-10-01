using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>A row of the save manager's table: the game, its status, where its saves are, how many versions, their size, the last backup.</summary>
public sealed record SaveRow(GameId Id, string Title, GameStatus? Status, string? StatusLabel, string Path, string Versions, string Size, string LastBackup)
{
    public override string ToString() => string.Join(", ", new[] { Title, StatusLabel ?? GsStatusBadge.Describe(Status).Word, Versions == "" ? null : $"{Versions} versions", LastBackup == "" ? null : $"last backup {LastBackup}" }.OfType<string>());
}

/// <summary>
/// A game on the save manager's Needs you tab (KAN-46): its status, what happened in a sentence, and the button that
/// deals with it (Resolve opens its conflict; Review, See where and See why its saves).
/// </summary>
public sealed record NeedsRow(GameId Id, string Title, GameStatus? Status, string Sentence, string Action, string ActionIcon)
{
    /// <summary>What a screen reader says for the row's button: "Resolve Lantern Keep".</summary>
    public string ActionName => $"{Action} {Title}";

    public override string ToString() => $"{Title}, {GsStatusBadge.Describe(Status).Word}. {Sentence}";
}

/// <summary>The Saves tab's order (KAN-49): as they need you and were played, or by a heading, Game or Status.</summary>
public enum SaveSort
{
    LastPlayed,
    Name,
    Status,
}

/// <summary>
/// One table's order on the Saves tab (KAN-49): as the games need you and were played, until a click on its GAME or
/// STATUS heading sorts it by that, and a second click reverses it. Each table has its own (the owner, 30 Sep 2026).
/// </summary>
public sealed partial class TableSort : ObservableObject
{
    private readonly Action _changed;

    /// <param name="table">The table's word in its command line: <c>syncing</c> or <c>all</c>.</param>
    /// <param name="changed">Makes the table's rows again in the new order.</param>
    public TableSort(string table, Action changed)
    {
        Table = table;
        _changed = changed;
        SortCommand = new RelayCommand<string>(column =>
        {
            if (!Enum.TryParse<SaveSort>(column, out var by))
            {
                return;
            }

            if (By == by)
            {
                Reversed = !Reversed;
            }
            else
            {
                (By, Reversed) = (by, false);
            }

            _changed();
        });
    }

    public string Table { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ByName), nameof(ByStatus), nameof(NameIdle), nameof(StatusIdle), nameof(CommandLine), nameof(GameHeadingName), nameof(StatusHeadingName))]
    private SaveSort _by = SaveSort.LastPlayed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Arrow), nameof(CommandLine), nameof(GameHeadingName), nameof(StatusHeadingName))]
    private bool _reversed;

    /// <summary>A heading's click: sorts this table by it, and a second click reverses it.</summary>
    public ICommand SortCommand { get; }

    public bool ByName => By == SaveSort.Name;

    public bool ByStatus => By == SaveSort.Status;

    /// <summary>The heading doesn't sort the table now: it shows the small sort mark that says it can.</summary>
    public bool NameIdle => !ByName;

    public bool StatusIdle => !ByStatus;

    /// <summary>The arrow on the heading sorting: <c>chevronDown</c> turned up for A to Z or what needs you first, down when reversed.</summary>
    public double Arrow => Reversed ? 0 : 180;

    /// <summary>The console's command line over the table, saying its order as the command would ("saves --all --sort name").</summary>
    public string CommandLine => $"saves --{Table} " + By switch
    {
        SaveSort.Name => "--sort name",
        SaveSort.Status => "--sort status",
        _ => "--sort last-played",
    } + (Reversed ? " --reverse" : "");

    /// <summary>What a screen reader says for the Game heading: how it sorts now, and what a click does.</summary>
    public string GameHeadingName => ByName ? $"Game, sorted {(Reversed ? "Z to A" : "A to Z")}; click to reverse" : "Game; click to sort by name";

    public string StatusHeadingName => ByStatus
        ? $"Status, sorted {(Reversed ? "with what needs you last" : "with what needs you first")}; click to reverse"
        : "Status; click to sort by status";

    /// <summary>The games in this table's order; ties keep the order they came in.</summary>
    public IReadOnlyList<LauncherGame> Order(IEnumerable<LauncherGame> games)
    {
        IEnumerable<LauncherGame> ordered = By switch
        {
            SaveSort.Name => games.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase),
            SaveSort.Status => games.OrderBy(g => SaveManagerViewModel.StatusRank(g.Status, g.Syncs)),
            _ => games,
        };
        return (Reversed ? ordered.Reverse() : ordered).ToList();
    }
}

/// <summary>A number in the save manager's strip: GAMES 12; with a line under it when it needs one ("E: · 312 GB free").</summary>
public sealed record SaveStat(string Label, string Value, string? Sub = null)
{
    public bool HasSub => Sub is not null;

    public override string ToString() => Sub is null ? $"{Label}: {Value}" : $"{Label}: {Value}, {Sub}";
}

/// <summary>
/// The save manager (design system → SaveManagerScreen, GameSavesScreen): every game's saves in one console-style
/// table, a strip of numbers and the live log; a game's name opens its saves, where the save work is (MGR-07). It lives
/// as long as the window, so the game open in it stays when the games refresh.
/// </summary>
public sealed partial class SaveManagerViewModel : ObservableObject, IPageSurface, IRailPage
{
    private readonly LauncherActions? _actions;
    private IReadOnlyList<LauncherGame> _games = [];
    private IReadOnlyDictionary<GameId, GameSaveSummary> _summaries = new Dictionary<GameId, GameSaveSummary>();
    private BackupSpace? _space;
    private bool _spaceRead;
    private int _loads;

    /// <summary>The Every game table: every game with saves, syncing or not (KAN-49).</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveRow> _rows = [];

    /// <summary>The Syncing table above it: the games that sync, in its own order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncingRows))]
    private IReadOnlyList<SaveRow> _syncingRows = [];

    [ObservableProperty]
    private IReadOnlyList<SaveStat> _stats = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopSubtitle))]
    private string _subtitle = "";

    /// <summary>The game whose saves are open; null shows every game's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(ShowsNeeds), nameof(ShowsPlan), nameof(ShowsVersions), nameof(ShowsLog), nameof(HasPage), nameof(Page), nameof(BackdropArt))]
    private GameSavesViewModel? _game;

    /// <summary>A game's conflict, open over its saves or on its own (SYNC-10).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(ShowsNeeds), nameof(ShowsPlan), nameof(ShowsVersions), nameof(ShowsLog), nameof(HasPage), nameof(Page), nameof(BackdropArt))]
    private ConflictViewModel? _conflict;

    /// <summary>
    /// The view: <c>saves</c>, every game's saves; <c>needs</c>, the games that need you (KAN-46); <c>plan</c>, what the
    /// next sync would do (SYNC-14); <c>versions</c>, every version of every game (MGR-08); or <c>log</c>, everything
    /// GameSync did (MGR-09).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(ShowsNeeds), nameof(ShowsPlan), nameof(ShowsVersions), nameof(ShowsLog), nameof(TopSubtitle))]
    private string _tab = "saves";

    /// <summary>The Needs you tab's games, what needs you first as the table has them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNeeds), nameof(NoNeeds), nameof(TopSubtitle))]
    private IReadOnlyList<NeedsRow> _needsRows = [];

    /// <summary>The save manager's views, as the design system's PillTabs: Saves, Needs you (with its count), Plan, Versions and Log.</summary>
    [ObservableProperty]
    private IReadOnlyList<Controls.NavItem> _tabs = TabsWith(0);

    /// <summary>The Needs you tab's id, which Home's Needs you opens (KAN-46).</summary>
    public const string NeedsTab = "needs";

    /// <param name="log">The agent's log, which the page shows as it happens.</param>
    public SaveManagerViewModel(LauncherActions? actions = null, ObservableCollection<LogLine>? log = null)
    {
        _actions = actions;
        Log = log ?? [];
        Plan = new SyncPlanViewModel(actions, (id, conflict) =>
        {
            if (conflict)
            {
                OpenConflict(id);
            }
            else
            {
                Open(id);
            }
        });
        Versions = new VersionsViewModel(actions, OpenVersion);
        Versions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VersionsViewModel.Subtitle))
            {
                OnPropertyChanged(nameof(TopSubtitle));
            }
        };
        LogTab = new LogViewModel(actions);
        OpenCommand = new RelayCommand<GameId>(id => Open(id));
        SyncingSort = new TableSort("syncing", () => Rebuild(DateTime.Now));
        AllSort = new TableSort("all", () => Rebuild(DateTime.Now));
        NeedsCommand = new RelayCommand<NeedsRow>(row =>
        {
            if (row is { Status: GameStatus.Conflict })
            {
                OpenConflict(row.Id);
            }
            else if (row is not null)
            {
                Open(row.Id);
            }
        });
        SyncNowCommand = new RelayCommand(() => _actions?.SyncNow(), () => _actions is not null);
        BackCommand = new RelayCommand(Back);
        ConflictBackCommand = new RelayCommand(ConflictBack);
        TableCommand = new RelayCommand(() =>
        {
            ReturnTo = null;
            _conflictReturnTo = null;
            Conflict = null;
            Game = null;
        });
        ConflictSavesCommand = new RelayCommand(() =>
        {
            if (Conflict is { } conflict)
            {
                Conflict = null;
                Open(conflict.Id);
            }
        });
    }

    private string? _conflictReturnTo;

    public ObservableCollection<LogLine> Log { get; }

    public string Title => "Save manager";

    public bool ShowsTable => Game is null && Conflict is null && Tab is not (NeedsTab or "plan" or "versions" or "log");

    public bool ShowsNeeds => Game is null && Conflict is null && Tab == NeedsTab;

    public bool HasSyncingRows => SyncingRows.Count > 0;

    /// <summary>The Syncing table's order, its own (KAN-49).</summary>
    public TableSort SyncingSort { get; }

    /// <summary>The Every game table's order, its own.</summary>
    public TableSort AllSort { get; }

    /// <summary>
    /// Status's order (KAN-49): what needs you first, then playing, then waiting to move, synced, backed up while its
    /// store syncs it, not available, and games not syncing yet last.
    /// </summary>
    public static int StatusRank(GameStatus? status, bool syncs) => !syncs ? 6 : status switch
    {
        _ when SyncCounts.NeedsYou(status) => 0,
        GameStatus.Playing => 1,
        GameStatus.UploadPending or GameStatus.NewerInCloud => 2,
        GameStatus.Synced or null => 3,
        GameStatus.BackupOnly => 4,
        GameStatus.NotAvailable => 5,
        _ => 0,
    };

    public bool HasNeeds => NeedsRows.Count > 0;

    public bool NoNeeds => NeedsRows.Count == 0;

    /// <summary>A Needs you row's button: Resolve opens the game's conflict, the others its saves, where the details are.</summary>
    public ICommand NeedsCommand { get; }

    public bool ShowsPlan => Game is null && Conflict is null && Tab == "plan";

    public bool ShowsVersions => Game is null && Conflict is null && Tab == "versions";

    public bool ShowsLog => Game is null && Conflict is null && Tab == "log";

    /// <summary>The top bar's line under the title, as each view's design has it.</summary>
    public string TopSubtitle => Tab switch
    {
        NeedsTab => NeedsRows.Count switch
        {
            0 => "Nothing needs you",
            1 => "1 game needs you",
            var n => $"{n.ToString(CultureInfo.InvariantCulture)} games need you",
        },
        "versions" => Versions.Subtitle,
        "log" => "Everything GameSync did, per game, newest first",
        _ => Subtitle,
    };

    public bool HasPage => Game is not null || Conflict is not null;

    private static IReadOnlyList<Controls.NavItem> TabsWith(int needYou) =>
    [
        new("saves", "Saves", "saves"),
        new(NeedsTab, "Needs you", "alert", needYou > 0 ? needYou.ToString(CultureInfo.InvariantCulture) : null),
        new("plan", "Plan", "chevronsRight"),
        new("versions", "Versions", "clock"),
        new("log", "Log", "terminal"),
    ];

    /// <summary>
    /// A view another page asked for (Home's Needs you): every game's list on that tab, with no game's saves or conflict
    /// over it and Back leading nowhere else.
    /// </summary>
    public void ShowTab(string tab)
    {
        ReturnTo = null;
        _conflictReturnTo = null;
        Conflict = null;
        Game = null;
        Tab = Tabs.Any(t => t.Id == tab) ? tab : "saves";
    }

    /// <summary>The Plan tab: made the first time it's shown, and again on Check again.</summary>
    public SyncPlanViewModel Plan { get; }

    /// <summary>The Versions tab: read each time it's shown, and as the games refresh while it shows.</summary>
    public VersionsViewModel Versions { get; }

    /// <summary>The Log tab, the whole of the log the Saves tab shows live: read each time it's shown, and as the games refresh.</summary>
    public LogViewModel LogTab { get; }

    partial void OnTabChanged(string value)
    {
        switch (value)
        {
            case "plan":
                Plan.CheckIfFirst();
                break;
            case "versions":
                _ = Versions.LoadAsync();
                break;
            case "log":
                _ = LogTab.LoadAsync();
                break;
        }
    }

    /// <summary>A row of the Versions tab: the game's saves, with that version picked where Restore is (MGR-08).</summary>
    public void OpenVersion(GameId id, VersionId version)
    {
        Open(id);
        Game?.Pick(version);
    }

    /// <summary>What shows instead of the table: a game's conflict, or its saves.</summary>
    public object? Page => (object?)Conflict ?? Game;

    /// <summary>Full glass (LOOK-17); a game's saves and its conflict show that game's own art.</summary>
    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => Conflict?.BackdropArt ?? Game?.BackdropArt;

    /// <summary>A game's name in the table: its saves.</summary>
    public ICommand OpenCommand { get; }

    public ICommand SyncNowCommand { get; }

    /// <summary>Back from a game's saves: to where the person came from (its page in the library), or to every game's saves.</summary>
    public ICommand BackCommand { get; }

    /// <summary>Back from a conflict, and Decide later: to the game's saves when it was opened there, or where the person came from.</summary>
    public ICommand ConflictBackCommand { get; }

    /// <summary>The breadcrumb's first step: every game's saves.</summary>
    public ICommand TableCommand { get; }

    /// <summary>The breadcrumb's game, from its conflict: its saves.</summary>
    public ICommand ConflictSavesCommand { get; }

    /// <summary>The page Back returns to from a game's saves, when they were opened from another page (a game's page).</summary>
    public string? ReturnTo { get; private set; }

    /// <summary>Opens a game's saves; <paramref name="returnTo"/> is the page Back goes back to, when it isn't every game's saves.</summary>
    public void Open(GameId id, string? returnTo = null)
    {
        ReturnTo = returnTo;
        Conflict = null;
        if (_games.FirstOrDefault(g => g.Id == id) is not { } game)
        {
            Game = null;
            return;
        }

        if (Game?.Id != id)
        {
            Game = new GameSavesViewModel(game, _actions, BackCommand, returnTo == "library" ? "My games" : "Save manager");
        }

        Game.Reload();
    }

    /// <summary>
    /// Opens a game's conflict (SYNC-10). Opened from its saves, Back returns there; from another page
    /// (<paramref name="returnTo"/>: Home, the library), Back returns to that page.
    /// </summary>
    public void OpenConflict(GameId id, string? returnTo = null)
    {
        if (_games.FirstOrDefault(g => g.Id == id) is not { } game)
        {
            return;
        }

        _conflictReturnTo = returnTo;
        if (Game?.Id != id)
        {
            // Not over its own saves: Back goes back where it came from, or to every game's saves.
            Game = null;
            ReturnTo = null;
        }

        if (Conflict?.Id != id)
        {
            Conflict = new ConflictViewModel(game, _actions, ConflictBackCommand, ConflictSavesCommand, TableCommand);
        }

        Conflict.Reload();
    }

    /// <summary>The rail's Save manager: a game's saves or conflict still open keeps showing, and Back leads to every game's saves.</summary>
    public void ReachedFromRail()
    {
        ReturnTo = null;
        _conflictReturnTo = null;
    }

    private void ConflictBack()
    {
        var returnTo = _conflictReturnTo;
        _conflictReturnTo = null;
        Conflict = null;
        if (Game is null && returnTo is not null)
        {
            _actions?.Show(returnTo, null);
        }
    }

    /// <summary>The games as they are now; the table is read again, off the UI thread, and the open game's saves too.</summary>
    public void Update(IReadOnlyList<LauncherGame> games)
    {
        _games = games;
        if (Conflict is { } conflict)
        {
            if (games.FirstOrDefault(g => g.Id == conflict.Id) is { } game)
            {
                conflict.Update(game);
                conflict.Reload();
            }
            else
            {
                Conflict = null;
            }
        }

        if (Game is { } open)
        {
            if (games.FirstOrDefault(g => g.Id == open.Id) is { } game)
            {
                open.Update(game);
                open.Reload();
            }
            else
            {
                Game = null;
            }
        }

        Rebuild(DateTime.Now);
        LoadSummaries();
        if (ShowsVersions)
        {
            _ = Versions.LoadAsync();
        }
        else if (ShowsLog)
        {
            _ = LogTab.LoadAsync();
        }
    }

    /// <summary>The table from what's known: with the saves at a glance once they're read.</summary>
    public void Show(IReadOnlyList<GameSaveSummary> summaries, DateTime nowLocal)
    {
        _summaries = summaries.ToDictionary(s => s.Id);
        Rebuild(nowLocal);
    }

    /// <summary>The space the backups take on this PC's drive, once it's been counted (MGR-03).</summary>
    public void ShowSpace(BackupSpace? space, DateTime nowLocal)
    {
        (_space, _spaceRead) = (space, true);
        Rebuild(nowLocal);
    }

    private async void LoadSummaries()
    {
        if (_actions?.LoadSaves is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        try
        {
            var summaries = await load(CancellationToken.None);
            if (ticket == _loads)
            {
                Show(summaries, DateTime.Now);
            }

            if (_actions.LoadSpace is { } space && await space(CancellationToken.None) is var counted && ticket == _loads)
            {
                ShowSpace(counted, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                Subtitle = $"GameSync couldn't read the saves on this PC: {e.Message}";
            }
        }
    }

    private void Back()
    {
        var returnTo = ReturnTo;
        ReturnTo = null;
        Conflict = null;
        Game = null;
        if (returnTo is not null)
        {
            _actions?.Show(returnTo, null);
        }
    }

    private void Rebuild(DateTime nowLocal)
    {
        // Games that sync first, the ones that need you at the top, then the ones found but not syncing yet; each by last play.
        var shown = _games.Where(g => !g.IsSoftware || g.Syncs)
            .OrderByDescending(g => g.NeedsYou)
            .ThenByDescending(g => g.Syncs)
            .ThenByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // KAN-49: each table in its own order, from a heading; ties keep the order above.
        SaveRow Row(LauncherGame g)
        {
            var summary = _summaries.GetValueOrDefault(g.Id);
            return new SaveRow(g.Id, g.Title, g.Syncs ? g.Status ?? GameStatus.Synced : null, g.Syncs ? HomeViewModel.StatusLabel(g) : null,
                summary?.Folder ?? "",
                g.Syncs && summary is not null ? summary.Versions.ToString(CultureInfo.InvariantCulture) : "",
                summary is { HistoryBytes: > 0 } ? Cli.FormatSize(summary.HistoryBytes) : "",
                summary?.LastBackupUtc is { } at ? Launcher.WhenText(at, nowLocal) ?? "" : "");
        }

        Rows = AllSort.Order(shown).Select(Row).ToList();
        SyncingRows = SyncingSort.Order(shown.Where(g => g.Syncs)).Select(Row).ToList();

        NeedsRows = shown.Where(g => g.NeedsYou).Select(g =>
        {
            var (action, icon) = GameViewModel.ActionFor(g.Status);
            return new NeedsRow(g.Id, g.Title, g.Status, GameSavesViewModel.SentenceOf(g), action, icon);
        }).ToList();
        if (Tabs.First(t => t.Id == NeedsTab).Count != TabsWith(NeedsRows.Count).First(t => t.Id == NeedsTab).Count)
        {
            Tabs = TabsWith(NeedsRows.Count);
        }

        var syncing = shown.Count(g => g.Syncs);
        var latest = _summaries.Values.Select(s => s.LastBackupUtc).Max();
        Stats =
        [
            new SaveStat("Games", syncing.ToString(CultureInfo.InvariantCulture)),
            // MGR-03: the backup folder as it is on its drive, not the versions' own sizes added up.
            new SaveStat("Backups on this PC", _space is { } space ? Cli.FormatSize(space.Bytes) : _spaceRead ? "None yet" : "…",
                _space is { } on ? on.FreeBytes is { } free ? $"{on.Drive} · {Cli.FormatSize(free)} free" : on.Drive : null),
            new SaveStat("Versions", _summaries.Values.Sum(s => s.Versions).ToString(CultureInfo.InvariantCulture)),
            new SaveStat("Last backup", latest is { } at ? Launcher.WhenText(at, nowLocal) ?? "None yet" : "None yet"),
            new SaveStat("Needs you", shown.Count(g => g.NeedsYou).ToString(CultureInfo.InvariantCulture)),
        ];
        var found = shown.Count - syncing;
        Subtitle = syncing == 0 ? $"No games syncing yet · {found} found with saves" : $"{syncing} {(syncing == 1 ? "game" : "games")} syncing · {found} found, not syncing yet";
    }
}
