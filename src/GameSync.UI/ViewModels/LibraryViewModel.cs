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
/// LibraryScreen). Its pill tabs choose the view: all games, those installed on this PC, those in their own folders
/// (Local, with Scan a folder for games…), those that need you, Steam's software or games hidden from the launcher; the
/// view, the search and the sort apply to both sides. It lives as long as the window, so what the person typed, the
/// order they chose and the game they opened stay when the games refresh; the view and the order are kept per PC.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IRailPage, IPageSurface, ISearchablePage
{
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
        ["all"] = "Games",
        ["installed"] = "Installed",
        ["local"] = "Local",
        ["attn"] = "Needs you",
        ["software"] = "Software",
        ["hidden"] = "Hidden",
    };

    /// <summary>The views kept per PC (LIB-22): what's on this PC. Needs you, Software and Hidden are visits, not a way to browse.</summary>
    public static readonly IReadOnlySet<string> KeptViews = new HashSet<string> { "all", "installed", "local" };

    private readonly HashSet<string> _closed = [];
    private readonly Func<LauncherGame, TileItem, GameViewModel>? _makePage;
    private IReadOnlyList<LauncherGame> _games = [];
    private IReadOnlyDictionary<GameId, TileItem> _tiles = new Dictionary<GameId, TileItem>();

    [ObservableProperty]
    private string _selectedTab = "all";

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(SortName), nameof(SortsByRecent), nameof(SortsByName), nameof(SortsByHours), nameof(SortsByAdded))]
    private LibrarySort _sort;

    /// <summary>The game whose page is open beside the list; null shows the covers.</summary>
    [ObservableProperty]
    private GameId? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCovers), nameof(Strength), nameof(BackdropArt))]
    private GameViewModel? _page;

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

    /// <summary>What the last Scan a folder for games… found, or why it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScanNote))]
    private string? _scanNote;

    /// <param name="actions">What the page asks of the app; null where it only shows, as in the snapshot tool.</param>
    /// <param name="sort">The order kept on this PC (LIB-16).</param>
    /// <param name="makePage">A game's page, opened beside the list; by default one made from what the library knows of it.</param>
    /// <param name="view">The view kept on this PC (LIB-22).</param>
    public LibraryViewModel(LauncherActions? actions = null, LibrarySort sort = LibrarySort.RecentlyPlayed, Func<LauncherGame, TileItem, GameViewModel>? makePage = null,
        string view = "all")
    {
        Actions = actions;
        _sort = sort;
        _makePage = makePage;
        _selectedTab = KeptViews.Contains(view) ? view : "all";
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
    }

    public LauncherActions? Actions { get; }

    /// <summary>Add game: a game or folder of the person's own (LIB-13), in its dialog over the library.</summary>
    public ICommand AddGameCommand { get; }

    public bool CanAddGame => Actions?.OpenAddGame is not null;

    /// <summary>Home's strength over the covers; a game's page is full glass over that game's own art, as game detail is (LOOK-17).</summary>
    public GlassStrength Strength => Page is null ? GlassStrength.Home : GlassStrength.Glass;

    public string? BackdropArt => Page?.BackdropArt;

    public string Title => "My games";

    public bool ShowsCovers => Page is null;

    public bool HasFavouriteTiles => FavouriteTiles.Count > 0;

    public bool HasNothing => Nothing is not null;

    /// <summary>The Local view, which says what it holds and offers Scan a folder for games… (LIB-22, LIB-23).</summary>
    public bool IsLocalView => SelectedTab == "local";

    public bool IsScanning => Scanning is not null;

    public string ScanLabel => Scanning is null ? "Scan a folder for games…" : "Looking for games…";

    public bool HasScanNote => ScanNote is not null;

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
    public static IReadOnlyDictionary<GameId, TileItem> Tiles(IReadOnlyList<LauncherGame> games, DateTime nowLocal, LauncherActions? actions)
    {
        var tiles = new Dictionary<GameId, TileItem>();
        foreach (var game in games)
        {
            tiles.TryAdd(game.Id, HomeViewModel.Tile(game, nowLocal, actions: actions, smallWidth: 48));
        }

        return tiles;
    }

    /// <summary>A library from a data folder's games, as the snapshot tool and the tests make it.</summary>
    /// <param name="tab"><c>all</c> for the games, <c>attn</c> for those that need you, <c>software</c> for Steam's software, <c>hidden</c> for games hidden from the launcher.</param>
    public static LibraryViewModel From(IReadOnlyList<LauncherGame> all, DateTime nowLocal, string tab = "all", LauncherActions? actions = null,
        LibrarySort sort = LibrarySort.RecentlyPlayed)
    {
        var library = new LibraryViewModel(actions, sort);
        library.Update(all, Tiles(all, nowLocal, actions));
        library.SelectedTab = library.Tabs.Any(t => t.Id == tab) ? tab : "all";
        return library;
    }

    /// <summary>The games as they are now, with their tiles; the view, the search, the order and the open game stay.</summary>
    public void Update(IReadOnlyList<LauncherGame> games, IReadOnlyDictionary<GameId, TileItem> tiles)
    {
        (_games, _tiles) = (games, tiles);
        var shown = games.Where(g => g.Shown).ToList();
        var needsYou = shown.Count(g => g.NeedsYou);
        var tabs = new List<NavItem>
        {
            new("all", "All games"),
            new("installed", "Installed"),
            new("local", "Local"),
            new("attn", "Needs you", Count: needsYou > 0 ? needsYou.ToString(CultureInfo.InvariantCulture) : null),
        };
        if (games.Any(g => g.IsSoftware && !g.IsHidden))
        {
            tabs.Add(new NavItem("software", "Software"));
        }

        if (games.Any(g => g.IsHidden))
        {
            tabs.Add(new NavItem("hidden", "Hidden"));
        }

        Tabs = tabs;
        Subtitle = $"{Count(shown.Count)} · {shown.Count(g => g.Installed)} installed";
        if (tabs.All(t => t.Id != SelectedTab))
        {
            SelectedTab = "all";
        }

        Rebuild();
        RefreshPage();
    }

    /// <summary>Opens a game's page, as Home's covers and Needs you rows do; <paramref name="returnTo"/> is the page Back goes back to.</summary>
    public void Open(GameId game, string? returnTo = null)
    {
        ReturnTo = returnTo;
        Selected = game;
    }

    private void Back()
    {
        var returnTo = ReturnTo;
        ReturnTo = null;
        Selected = null;
        if (returnTo is not null)
        {
            Actions?.Show(returnTo, null);
        }
    }

    public void FocusSearch() => SearchFocusRequests++;

    /// <summary>The rail's Game library: a game's page still open keeps showing, and Back from it leads to the covers.</summary>
    public void ReachedFromRail() => ReturnTo = null;

    /// <summary>A view another page opened (Home's My games and Needs you): shown, not kept as the library's own.</summary>
    public void ShowView(string view)
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

    private bool _visiting;

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
        ScanNote = $"Looking for games in {folder}, and scanning the rest of this PC with it. It takes a minute or so.";
        try
        {
            ScanNote = (await scan(folder, CancellationToken.None)).Sentence;
        }
        catch (OperationCanceledException)
        {
            ScanNote = null;
        }
        catch (Exception e) when (e is UsageException or InvalidOperationException)
        {
            ScanNote = e.Message;
        }
        catch (Exception e)
        {
            ScanNote = $"The scan stopped before it finished: {e.Message}";
        }
        finally
        {
            Scanning = null;
        }
    }

    partial void OnSelectedTabChanged(string value)
    {
        if (!_visiting && KeptViews.Contains(value))
        {
            Actions?.SetView?.Invoke(value);
        }

        OnPropertyChanged(nameof(IsLocalView));
        Rebuild();
    }

    partial void OnScanningChanged(string? value) => OnPropertyChanged(nameof(IsScanning));

    partial void OnSearchChanged(string value) => Rebuild();

    partial void OnSortChanged(LibrarySort value)
    {
        Actions?.SetSort?.Invoke(value);
        Rebuild();
    }

    partial void OnSelectedChanged(GameId? value) => RefreshPage();

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
            "installed" => g.Shown && g.Installed,
            "local" => g.Shown && g.IsLocal,
            "attn" => g.Shown && g.NeedsYou,
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
        OtherHeading = SelectedTab == "all" ? "All games" : viewName;
        var searching = !string.IsNullOrWhiteSpace(Search);
        CountLabel = searching ? $"{matching.Count.ToString(CultureInfo.InvariantCulture)} of {inView.Count.ToString(CultureInfo.InvariantCulture)}" : Count(inView.Count);
        Nothing = matching.Count > 0 ? null
            : searching ? $"No game here matches “{Search.Trim()}”."
            : SelectedTab switch
            {
                "installed" => "No games are installed on this PC yet, as far as GameSync has looked.",
                "local" => "No games in their own folders yet.",
                "attn" => "Nothing needs you. Every game that syncs is fine.",
                "hidden" => "No games are hidden. Right-click a game to hide it from the launcher on this PC.",
                _ => "No games yet. First run looks for them on this PC; until it's built, run gamesync scan.",
            };
    }

    private static string Count(int games) => games == 1 ? "1 game" : $"{games.ToString(CultureInfo.InvariantCulture)} games";
}
