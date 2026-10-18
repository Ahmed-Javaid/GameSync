using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>A heading or a game in the library's list.</summary>
public abstract record GameListItem;

/// <summary>A group's heading in the library's list: Favourites, or the view's other games, with a count; a click opens or closes it.</summary>
public sealed record GameListHeading(string GroupId, string Label, int Count, bool IsOpen) : GameListItem
{
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>What a screen reader says for the heading (A11Y-03).</summary>
    public override string ToString() => $"{Label}, {Count} {(Count == 1 ? "game" : "games")}, {(IsOpen ? "open" : "closed")}";
}

/// <summary>A game in the library's list: its small cover and name, and its status when it needs the person or is running.</summary>
public sealed record GameListRow(TileItem Game) : GameListItem
{
    public override string ToString() => Game.SpokenName;
}

/// <summary>A notice over the library's covers: busy while a folder is looked in, then what it found, or a problem.</summary>
public sealed record LibraryNotice(string Kind, string Title, string? Detail)
{
    public const string Busy = "busy";
    public const string Done = "done";
    public const string Problem = "problem";

    /// <summary>Games were found, so Show them has somewhere to go.</summary>
    public bool Found { get; init; }

    public override string ToString() => Detail is null ? Title : $"{Title}. {Detail}";
}

/// <summary>A page that has a search field the rail's Search button (and Ctrl+F) can put the cursor in.</summary>
public interface ISearchablePage
{
    void FocusSearch();
}

/// <summary>
/// A page the rail can open while one of its inner pages is open (a game's page in the library, a game's saves in the
/// save manager). Reached from the rail, it forgets where an earlier visit came from, so Back leads to the top of its own
/// area rather than to the page that opened it before (LIB-21).
/// </summary>
public interface IRailPage
{
    void ReachedFromRail();
}

/// <summary>
/// The game library, laid out like Steam's (LIB-14 to LIB-18): every game by name in a list on the left, with the
/// search, the sort and favourites first, and the covers, or the page of the game picked, on the right (design system →
/// LibraryScreen). Its pill tabs choose the view (LIB-31, design system version 57): Installed, the one it opens on,
/// then Saves only (games whose saves are here but the game isn't), All games, those in their own folders (Local, with
/// Scan a folder for games…), Steam's software and games hidden from the launcher; the view and the search apply to
/// both sides. What needs you is the save manager's, not a kind of game (KAN-46). It lives as long as the window, so
/// what the person typed, the order they chose and the game they opened stay when the games refresh; the order is kept
/// per PC, and so is the view when it's Installed or All games, which the rail opens on (Installed until another is
/// picked), never a narrower view left from before.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IRailPage, IPageSurface, ISearchablePage
{
    /// <summary>LIB-31: the games installed on this PC, the view the library opens on until another is picked.</summary>
    public const string InstalledView = "installed";

    /// <summary>LIB-31: the games whose saves are here but that aren't installed on this PC.</summary>
    public const string SavesView = "saves";

    public const string AllView = "all";

    /// <summary>For another page opening the library (Home's My games): the view kept on this PC.</summary>
    public const string KeptView = "kept";

    /// <summary>The views kept on this PC when picked; the narrower ones are visits.</summary>
    public static bool IsKeptView(string? view) => view is InstalledView or AllView;
    /// <summary>The orders on offer, as the sort menu names them (LIB-16).</summary>
    public static readonly IReadOnlyList<(LibrarySort Sort, string Label)> Sorts =
    [
        (LibrarySort.RecentlyPlayed, "Recently played"),
        (LibrarySort.Name, "Name"),
        (LibrarySort.HoursPlayed, "Hours played"),
        (LibrarySort.RecentlyAdded, "Recently added"),
    ];

    private static readonly IReadOnlyDictionary<string, string> ViewNames = new Dictionary<string, string>
    {
        [InstalledView] = "Installed",
        [SavesView] = "Saves only",
        [AllView] = "Games",
        ["local"] = "Local",
        ["software"] = "Software",
        ["hidden"] = "Hidden",
    };

    private readonly HashSet<string> _closed = [];
    private readonly Func<LauncherGame, TileItem, GameViewModel>? _makePage;
    private IReadOnlyList<LauncherGame> _games = [];
    private IReadOnlyDictionary<GameId, TileItem> _tiles = new Dictionary<GameId, TileItem>();

    /// <summary>LIB-31: the view kept on this PC, Installed or All games, which the rail and Home's My games open on.</summary>
    private string _keptView;

    /// <summary>The view is being set by the app, not picked by the person, so it isn't kept.</summary>
    private bool _visiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalView), nameof(IsSavesView), nameof(IsHiddenView), nameof(ViewLabel), nameof(ViewName), nameof(ViewIsInstalled),
        nameof(ViewIsSaves), nameof(ViewIsAll), nameof(ViewIsLocal), nameof(ViewIsSoftware), nameof(ViewIsHidden))]
    private string _selectedTab;

    /// <summary>How many games each view holds, for its line in the view's menu.</summary>
    private IReadOnlyDictionary<string, int> _viewCounts = new Dictionary<string, int>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchCount), nameof(ShowsShortcut))]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(SortName), nameof(SortsByRecent), nameof(SortsByName), nameof(SortsByHours), nameof(SortsByAdded))]
    private LibrarySort _sort;

    /// <summary>The game whose page is open beside the list; null shows the covers.</summary>
    [ObservableProperty]
    private GameId? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCovers), nameof(Strength), nameof(BackdropArt), nameof(Shown))]
    private GameViewModel? _page;

    /// <summary>A game's achievements, every one, open beside the list over its page (design system → AchievementsScreen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    private AchievementsViewModel? _achievements;

    /// <summary>The page Back from a game's achievements returns to when another page opened them (Home's card); null: the game's page.</summary>
    private string? _achievementsReturn;

    [ObservableProperty]
    private IReadOnlyList<NavItem> _tabs = [];

    [ObservableProperty]
    private IReadOnlyList<GameListItem> _listItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavouriteTiles))]
    private IReadOnlyList<TileItem> _favouriteTiles = [];

    [ObservableProperty]
    private IReadOnlyList<TileItem> _otherTiles = [];

    [ObservableProperty]
    private string _otherHeading = "All games";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchCount))]
    private string _countLabel = "";

    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>What the covers say when nothing is in them: nothing matches the search, or the view is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNothing))]
    private string? _nothing;

    /// <summary>Bumped when the search field should take the cursor (the rail's Search, Ctrl+F); the page follows it.</summary>
    [ObservableProperty]
    private int _searchFocusRequests;

    /// <summary>A folder is being scanned for games (LIB-23).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanLabel))]
    private string? _scanning;

    /// <summary>
    /// What Add game folder or Scan a folder for games… is doing, found, or why it couldn't, as a notice over the covers
    /// until it's closed (the owner, 18 Oct 2026: "That information line is too bare. it need to be apparent that this is
    /// something we want you to see").
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScanNote), nameof(ScanNoteBusy), nameof(ScanNoteDone), nameof(ScanNoteProblem), nameof(ScanNoteFound))]
    private LibraryNotice? _scanNote;

    /// <param name="actions">What the page asks of the app; null where it only shows, as in the snapshot tool.</param>
    /// <param name="sort">The order kept on this PC (LIB-16).</param>
    /// <param name="makePage">A game's page, opened beside the list; by default one made from what the library knows of it.</param>
    /// <param name="view">The view kept on this PC (LIB-31): Installed until the person picks All games.</param>
    public LibraryViewModel(LauncherActions? actions = null, LibrarySort sort = LibrarySort.RecentlyPlayed, Func<LauncherGame, TileItem, GameViewModel>? makePage = null,
        string view = InstalledView)
    {
        Actions = actions;
        _sort = sort;
        _makePage = makePage;
        _keptView = IsKeptView(view) ? view : InstalledView;
        _selectedTab = _keptView;
        OpenCommand = new RelayCommand<GameId>(game =>
        {
            ReturnTo = null;
            Selected = game;
        });
        BackCommand = new RelayCommand(Back);
        CoversCommand = new RelayCommand(() =>
        {
            ReturnTo = null;
            Selected = null;
        });
        SortByCommand = new RelayCommand<string>(name =>
        {
            if (Enum.TryParse<LibrarySort>(name, out var chosen))
            {
                Sort = chosen;
            }
        });
        ToggleGroupCommand = new RelayCommand<string>(group =>
        {
            if (group is not null && !_closed.Remove(group))
            {
                _closed.Add(group);
            }

            Rebuild();
        });
        OpenFirstMatchCommand = new RelayCommand(() =>
        {
            if (FavouriteTiles.Concat(OtherTiles).FirstOrDefault() is { } first)
            {
                Selected = first.Id;
            }
        });
        AddGameCommand = new RelayCommand(() => Actions?.OpenAddGame?.Invoke());
        CloseScanNoteCommand = new RelayCommand(() => ScanNote = null);
        ShowFoundCommand = new RelayCommand(() =>
        {
            ScanNote = null;
            Search = "";
            SelectedTab = "local";
        });
        ViewCommand = new RelayCommand<string>(view =>
        {
            if (view is not null && Tabs.Any(t => t.Id == view))
            {
                SelectedTab = view;
            }
        });
    }

    public LauncherActions? Actions { get; }

    /// <summary>Add game: a game or folder of the person's own (LIB-13), in its dialog over the library.</summary>
    public ICommand AddGameCommand { get; }

    public bool CanAddGame => Actions?.OpenAddGame is not null;

    /// <summary>
    /// Full glass, with a game's page open or not, so the list beside the covers keeps the art's colour as pages open
    /// and close (the owner, 1 Oct 2026; LOOK-17); a game's page shows that game's own art.
    /// </summary>
    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => Page?.BackdropArt;

    public string Title => "My games";

    public bool ShowsCovers => Page is null;

    /// <summary>What's beside the list: a game's achievements, its page, or nothing while the covers show.</summary>
    public object? Shown => (object?)Achievements ?? Page;

    public bool HasFavouriteTiles => FavouriteTiles.Count > 0;

    public bool HasNothing => Nothing is not null;

    /// <summary>While searching, how many games match, in the search field where Ctrl F was ("3 of 61").</summary>
    public string? SearchCount => string.IsNullOrWhiteSpace(Search) ? null : CountLabel;

    public bool ShowsShortcut => string.IsNullOrEmpty(Search);

    /// <summary>The Local view, which says what it holds and offers Scan a folder for games… (LIB-22, LIB-23).</summary>
    public bool IsLocalView => SelectedTab == "local";

    /// <summary>LIB-31: Saves only, which says what it holds.</summary>
    public bool IsSavesView => SelectedTab == SavesView;

    /// <summary>LIB-30, LIB-31: Hidden, which says it holds what a later scan took for no game, too.</summary>
    public bool IsHiddenView => SelectedTab == "hidden";

    /// <summary>The view kept on this PC (LIB-31).</summary>
    public string KeptViewId => _keptView;

    /// <summary>
    /// The view, on its dropdown beside the sort in the list's header (the owner, 18 Oct 2026: "Make it into a dropdown
    /// next to name with all these settings"): its name, and a menu of every view with how many games each holds.
    /// </summary>
    public string ViewLabel => Tabs.FirstOrDefault(t => t.Id == SelectedTab)?.Label ?? "Installed";

    /// <summary>What a screen reader says for the view's dropdown.</summary>
    public string ViewName => $"Library view: {ViewLabel}";

    public bool ViewIsInstalled => SelectedTab == InstalledView;

    public bool ViewIsSaves => SelectedTab == SavesView;

    public bool ViewIsAll => SelectedTab == AllView;

    public bool ViewIsLocal => SelectedTab == "local";

    public bool ViewIsSoftware => SelectedTab == "software";

    public bool ViewIsHidden => SelectedTab == "hidden";

    /// <summary>Software and Hidden are in the menu only when there's something in them.</summary>
    public bool HasSoftware => Tabs.Any(t => t.Id == "software");

    public bool HasHidden => Tabs.Any(t => t.Id == "hidden");

    /// <summary>How many games each view holds, on the right of its line in the menu (GsMenuItem.Hint).</summary>
    public string? InstalledCount => CountOf(InstalledView);

    public string? SavesCount => CountOf(SavesView);

    public string? AllCount => CountOf(AllView);

    public string? LocalCount => CountOf("local");

    public string? SoftwareCount => CountOf("software");

    public string? HiddenCount => CountOf("hidden");

    /// <summary>Picks a view from the dropdown's menu; Installed or All games is kept on this PC (LIB-31).</summary>
    public ICommand ViewCommand { get; }

    private string? CountOf(string view) => _viewCounts.TryGetValue(view, out var count) ? count.ToString(CultureInfo.InvariantCulture) : null;

    public bool IsScanning => Scanning is not null;

    public string ScanLabel => Scanning is null ? "Scan a folder for games…" : "Looking for games…";

    public bool HasScanNote => ScanNote is not null;

    public bool ScanNoteBusy => ScanNote?.Kind == LibraryNotice.Busy;

    public bool ScanNoteDone => ScanNote?.Kind == LibraryNotice.Done;

    public bool ScanNoteProblem => ScanNote?.Kind == LibraryNotice.Problem;

    /// <summary>The scan found games: Show them opens the Local view, where they are.</summary>
    public bool ScanNoteFound => ScanNote is { Kind: LibraryNotice.Done, Found: true };

    /// <summary>Closes the scan's notice.</summary>
    public ICommand CloseScanNoteCommand { get; }

    /// <summary>Show them: the Local view, where the games a folder held are, and the notice closed.</summary>
    public ICommand ShowFoundCommand { get; }

    public bool CanScan => Actions?.ScanFolder is not null;

    public string SortLabel => Sorts.First(s => s.Sort == Sort).Label;

    /// <summary>What a screen reader says for the sort button.</summary>
    public string SortName => $"Sort by: {SortLabel}";

    public bool SortsByRecent => Sort == LibrarySort.RecentlyPlayed;

    public bool SortsByName => Sort == LibrarySort.Name;

    public bool SortsByHours => Sort == LibrarySort.HoursPlayed;

    public bool SortsByAdded => Sort == LibrarySort.RecentlyAdded;

    /// <summary>Opens a game's page beside the list: a click on its row or cover (LIB-18).</summary>
    public ICommand OpenCommand { get; }

    /// <summary>
    /// Back, from a game's page: to where the person came from (Home, when a cover or a Needs you row there opened it),
    /// or else the covers. Esc, Alt+Left and the mouse's back button do the same.
    /// </summary>
    public ICommand BackCommand { get; }

    /// <summary>The breadcrumb's My games: the covers, whichever page opened the game.</summary>
    public ICommand CoversCommand { get; }

    /// <summary>The page Back returns to from a game's page, when another page opened it; null for the covers.</summary>
    public string? ReturnTo { get; private set; }

    public ICommand SortByCommand { get; }

    public ICommand ToggleGroupCommand { get; }

    /// <summary>Enter in the search field: the first game that matches.</summary>
    public ICommand OpenFirstMatchCommand { get; }

    /// <summary>
    /// The tiles for every game, each picture decoded once whichever views show it: the cover for the covers and a small
    /// one for the list. Made off the UI thread, since decoding takes a while.
    /// </summary>
    /// <param name="progress">ACH-02: how far each game's achievements are, for the ring on its cover (design system version 39).</param>
    public static IReadOnlyDictionary<GameId, TileItem> Tiles(IReadOnlyList<LauncherGame> games, DateTime nowLocal, LauncherActions? actions,
        IReadOnlyDictionary<GameId, (int Done, int Total)>? progress = null)
    {
        var tiles = new Dictionary<GameId, TileItem>();
        foreach (var game in games)
        {
            var tile = HomeViewModel.Tile(game, nowLocal, actions: actions, smallWidth: 48,
                achievements: progress is not null && progress.TryGetValue(game.Id, out var done) ? done : null);
            // LIB-31: a game not installed here looks it wherever its cover shows: greyed, tagged, and said under its name.
            tiles.TryAdd(game.Id, game.Installed ? tile : tile with { SavesOnly = true, Meta = SavesOnlyMeta(game) });
        }

        return tiles;
    }

    /// <summary>
    /// LIB-31: under a game's name when only its saves are here: kept, when GameSync backs them up (from another PC, a
    /// friend's zip, or before the game was uninstalled); found, when the scan found them here and nothing keeps them yet.
    /// </summary>
    public static string SavesOnlyMeta(LauncherGame game) => game.Syncs ? "Not installed here · saves kept" : "Not installed here · saves found";

    /// <summary>A library from a data folder's games, as the snapshot tool and the tests make it.</summary>
    /// <param name="tab">
    /// <c>installed</c> for the games installed here, <c>saves</c> for those whose saves only are here, <c>all</c> for every
    /// game, <c>local</c> for those in their own folders, <c>software</c> for Steam's software, <c>hidden</c> for games
    /// hidden from the launcher.
    /// </param>
    public static LibraryViewModel From(IReadOnlyList<LauncherGame> all, DateTime nowLocal, string tab = AllView, LauncherActions? actions = null,
        LibrarySort sort = LibrarySort.RecentlyPlayed, IReadOnlyDictionary<GameId, (int Done, int Total)>? progress = null, string keptView = InstalledView)
    {
        var library = new LibraryViewModel(actions, sort, view: keptView);
        library.Update(all, Tiles(all, nowLocal, actions, progress));
        library.ShowView(tab);
        return library;
    }

    /// <summary>The games as they are now, with their tiles; the view, the search, the order and the open game stay.</summary>
    public void Update(IReadOnlyList<LauncherGame> games, IReadOnlyDictionary<GameId, TileItem> tiles)
    {
        (_games, _tiles) = (games, tiles);
        var shown = games.Where(g => g.Shown).ToList();
        // LIB-31 (design system version 57): Installed first, the view the library opens on, then Saves only beside it.
        var tabs = new List<NavItem>
        {
            new(InstalledView, "Installed"),
            new(SavesView, "Saves only"),
            new(AllView, "All games"),
            new("local", "Local"),
        };
        if (games.Any(g => g.IsSoftware && !g.IsHidden))
        {
            tabs.Add(new NavItem("software", "Software"));
        }

        if (games.Any(g => g.IsHidden))
        {
            tabs.Add(new NavItem("hidden", "Hidden"));
        }

        if (!tabs.Select(t => t.Id).SequenceEqual(Tabs.Select(t => t.Id)))
        {
            Tabs = tabs;
        }

        _viewCounts = new Dictionary<string, int>
        {
            [InstalledView] = shown.Count(g => g.Installed),
            [SavesView] = shown.Count(g => !g.Installed),
            [AllView] = shown.Count,
            ["local"] = shown.Count(g => g.IsLocal),
            ["software"] = games.Count(g => g.IsSoftware && !g.IsHidden),
            ["hidden"] = games.Count(g => g.IsHidden),
        };
        foreach (var name in new[] { nameof(ViewLabel), nameof(ViewName), nameof(HasSoftware), nameof(HasHidden), nameof(InstalledCount), nameof(SavesCount),
                     nameof(AllCount), nameof(LocalCount), nameof(SoftwareCount), nameof(HiddenCount) })
        {
            OnPropertyChanged(name);
        }

        Subtitle = $"{Count(shown.Count)} · {shown.Count(g => g.Installed)} installed";
        if (tabs.All(t => t.Id != SelectedTab))
        {
            Visit(_keptView);
        }

        Rebuild();
        RefreshPage();
    }

    /// <summary>Opens a game's page, as Home's covers and Needs you rows do; <paramref name="returnTo"/> is the page Back goes back to.</summary>
    public void Open(GameId game, string? returnTo = null)
    {
        ReturnTo = returnTo;
        Selected = game;
        Achievements = null;
    }

    /// <summary>
    /// A game's achievements, every one, over its page: from its page's View all (Back returns to its page), or from
    /// another page such as Home's card (<paramref name="returnTo"/>: Back returns there).
    /// </summary>
    /// <param name="known">The game's achievements when the page that opened them has them read already.</param>
    public void ShowAchievements(GameId game, string? returnTo = null, GameAchievementsView? known = null)
    {
        if (Selected != game || returnTo is not null)
        {
            ReturnTo = returnTo;
            Selected = game;
        }

        var shown = _games.FirstOrDefault(g => g.Id == game);
        var title = shown?.Title ?? Page?.Title ?? game.Value;
        var achievements = new AchievementsViewModel(game, title, Actions, Back,
        [
            new Crumb("My games", CoversCommand, "My games: every game's cover"),
            new Crumb(title, new RelayCommand(() => Achievements = null), $"{title}: its page"),
        ])
        {
            // Decoded small and drawn large: soft, as the design blurs it, for nothing.
            HeadArt = ArtImages.Load(shown?.HeroPath ?? shown?.CoverPath, 160),
        };
        _achievementsReturn = returnTo;
        Achievements = achievements;
        achievements.Load(known ?? (Page?.Id == game ? Page.AchievementsView : null));
    }

    private void Back()
    {
        // From a game's achievements: its page, unless another page opened them, which Back returns to.
        if (Achievements is not null)
        {
            Achievements = null;
            if (_achievementsReturn is null)
            {
                return;
            }
        }

        var returnTo = ReturnTo;
        ReturnTo = null;
        Selected = null;
        if (returnTo is not null)
        {
            Actions?.Show(returnTo, null);
        }
    }

    public void FocusSearch() => SearchFocusRequests++;

    /// <summary>
    /// KAN-46: the rail's Game library always opens the library itself: no game's page, no search or narrower view left
    /// from before; the view kept on this PC, Installed or All games (LIB-31), and the order stay, since they're the
    /// person's settings.
    /// </summary>
    public void ReachedFromRail()
    {
        ReturnTo = null;
        Selected = null;
        Search = "";
        Visit(_keptView);
    }

    /// <summary>
    /// A view another page opened (Home's My games: <see cref="KeptView"/>, the view kept on this PC): the view asked for
    /// when the library has it, else the kept one. A visit, so it isn't kept.
    /// </summary>
    public void ShowView(string view) => Visit(view != KeptView && Tabs.Any(t => t.Id == view) ? view : _keptView);

    /// <summary>Shows a view without keeping it on this PC: only a view the person picks is kept (LIB-31).</summary>
    private void Visit(string view)
    {
        _visiting = true;
        try
        {
            SelectedTab = view;
        }
        finally
        {
            _visiting = false;
        }
    }

    /// <summary>
    /// LIB-23: Scan a folder for games…, with the folder picked in Windows' picker: the note says it's looking, then what
    /// it found, or why it couldn't; the games found join the view as the library refreshes.
    /// </summary>
    public async void ScanFolder(string folder)
    {
        if (Actions?.ScanFolder is not { } scan || Scanning is not null)
        {
            return;
        }

        Scanning = folder;
        ScanNote = new LibraryNotice(LibraryNotice.Busy, $"Looking for games in {folder}", "GameSync scans the rest of this PC with it, which takes a minute or so.");
        try
        {
            ScanNote = await scan(folder, CancellationToken.None) is { } found
                ? new LibraryNotice(LibraryNotice.Done, found.Headline, found.Detail) { Found = found.Games.Count > 0 }
                : null;
        }
        catch (OperationCanceledException)
        {
            ScanNote = null;
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException)
        {
            ScanNote = new LibraryNotice(LibraryNotice.Problem, $"Couldn't look in {folder}", e.Message);
        }
        catch (Exception e)
        {
            ScanNote = new LibraryNotice(LibraryNotice.Problem, "The scan stopped before it finished", e.Message);
        }
        finally
        {
            Scanning = null;
        }
    }

    partial void OnSelectedTabChanged(string value)
    {
        // LIB-31: Installed or All games, picked by the person, is the view the library opens on next time, on this PC.
        if (!_visiting && IsKeptView(value) && value != _keptView)
        {
            _keptView = value;
            OnPropertyChanged(nameof(KeptViewId));
            Actions?.SetView?.Invoke(value);
        }

        Rebuild();
    }

    partial void OnScanningChanged(string? value) => OnPropertyChanged(nameof(IsScanning));

    partial void OnSearchChanged(string value) => Rebuild();

    partial void OnSortChanged(LibrarySort value)
    {
        Actions?.SetSort?.Invoke(value);
        Rebuild();
    }

    partial void OnSelectedChanged(GameId? value)
    {
        Achievements = null;
        RefreshPage();
    }

    /// <summary>The open game's page, made again from what's known now, or none; a game that's gone closes it.</summary>
    private void RefreshPage()
    {
        if (Selected is not { } id || _games.FirstOrDefault(g => g.Id == id) is not { } game || !_tiles.TryGetValue(id, out var tile))
        {
            Page = null;
            if (Selected is not null && _games.Count > 0)
            {
                Selected = null;
            }

            return;
        }

        // The page's places, saves and history are read again too: a sync may have added a version.
        if (Page is { } page && page.Id == id)
        {
            page.Update(game, tile);
            page.Reload();
            return;
        }

        Page = _makePage?.Invoke(game, tile) ?? new GameViewModel(game, tile, Actions);
        Page.Reload();
    }

    private void Rebuild()
    {
        var inView = _games.Where(g => SelectedTab switch
        {
            InstalledView => g.Shown && g.Installed,
            SavesView => g.Shown && !g.Installed,
            "local" => g.Shown && g.IsLocal,
            "software" => g.IsSoftware && !g.IsHidden,
            "hidden" => g.IsHidden,
            _ => g.Shown,
        }).ToList();
        var matching = Launcher.Sort(inView.Where(g => Launcher.Matches(g.Title, Search)), Sort);
        var favourites = matching.Where(g => g.IsFavourite).Select(g => _tiles.GetValueOrDefault(g.Id)).OfType<TileItem>().ToList();
        var others = matching.Where(g => !g.IsFavourite).Select(g => _tiles.GetValueOrDefault(g.Id)).OfType<TileItem>().ToList();
        var viewName = ViewNames.GetValueOrDefault(SelectedTab, "Games");

        var list = new List<GameListItem>();
        foreach (var (id, label, rows) in new[] { ("favourites", "Favourites", favourites), ("view", viewName, others) })
        {
            if (rows.Count == 0)
            {
                continue;
            }

            var open = !_closed.Contains(id);
            list.Add(new GameListHeading(id, label, rows.Count, open));
            if (open)
            {
                list.AddRange(rows.Select(r => new GameListRow(r)));
            }
        }

        ListItems = list;
        FavouriteTiles = favourites;
        OtherTiles = others;
        OtherHeading = SelectedTab == AllView ? "All games" : viewName;
        var searching = !string.IsNullOrWhiteSpace(Search);
        CountLabel = searching ? $"{matching.Count.ToString(CultureInfo.InvariantCulture)} of {inView.Count.ToString(CultureInfo.InvariantCulture)}" : Count(inView.Count);
        Nothing = matching.Count > 0 ? null
            : searching ? $"No game here matches “{Search.Trim()}”."
            : SelectedTab switch
            {
                InstalledView => "No games are installed on this PC yet, as far as GameSync has looked. Add game folder points it at a folder of games; All games shows every game it knows.",
                SavesView => "No saves are waiting for a game: every game with saves here is installed on this PC.",
                "local" => "No games in their own folders yet.",
                "hidden" => "No games are hidden. Right-click a game to hide it from the launcher on this PC.",
                _ => "No games yet. Add game folder points GameSync at a folder of games, and Add game adds one it didn't find.",
            };
    }

    private static string Count(int games) => games == 1 ? "1 game" : $"{games.ToString(CultureInfo.InvariantCulture)} games";
}
