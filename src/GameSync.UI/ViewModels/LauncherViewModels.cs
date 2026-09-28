using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>What the launcher's pages ask of the app. Pages made without it (the snapshot tool) only show.</summary>
/// <param name="Play">Starts a game, with the check before playing when it syncs (PLAY-02, PLAY-03).</param>
/// <param name="SyncNow">Every game syncs at the agent's next round with nothing playing.</param>
/// <param name="Show">Opens a page by its rail id, on a tab when given: <c>("library", "attn")</c>.</param>
/// <param name="SetHidden">Hides a game from the launcher on this PC, or shows it again.</param>
public sealed record LauncherActions(Action<GameId> Play, Action SyncNow, Action<string, string?> Show, Action<GameId, bool> SetHidden);

/// <summary>A game tile: its cover (or none, for a title cover), status and meta line, and hiding it from the launcher.</summary>
public sealed record TileItem(GameId Id, string Title, IImage? Art, GameStatus? Status, string? Meta)
{
    /// <summary>The badge's words when the status's own aren't enough: which store syncs it (LIB-10).</summary>
    public string? StatusLabel { get; init; }

    public bool IsHidden { get; init; }

    /// <summary>Hides the game from the launcher on this PC, or shows it again; null where the page can't.</summary>
    public ICommand? ToggleHidden { get; init; }

    public string HideLabel => IsHidden ? "Show in the launcher" : "Hide from the launcher";
}

/// <summary>A row of the Needs you card: a small cover, the game, its status and the button that deals with it.</summary>
public sealed record NeedsYouItem(GameId Id, string Title, IImage? Art, string Initial, GameStatus? Status, string Action);

/// <summary>
/// The launcher home (PLAY-01): the hero, Needs you, Jump back in and this month's activity. Its pill tabs are the
/// library's views, as the design system labels them: Recently played is this page, and My games or Needs you open the
/// library on that view.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IPageSurface
{
    public const string ThisPage = "recent";

    /// <summary>Glossy's Home strength: a step more solid than full glass (LOOK-17).</summary>
    public GlassStrength Strength => GlassStrength.Home;

    [ObservableProperty]
    private string _selectedTab = ThisPage;

    public HomeViewModel()
    {
        PlayCommand = new RelayCommand(() =>
        {
            if (HeroId is { } game)
            {
                Actions?.Play(game);
            }
        });
        SyncNowCommand = new RelayCommand(() => Actions?.SyncNow());
        OpenLibraryCommand = new RelayCommand(() => Actions?.Show("library", "all"));
        OpenSavesCommand = new RelayCommand(() => Actions?.Show("saves", null));
    }

    /// <summary>What the page asks of the app; without it, the buttons do nothing.</summary>
    public LauncherActions? Actions { get; init; }

    /// <summary>Continue playing: the hero game, the way its store starts it.</summary>
    public ICommand PlayCommand { get; }

    public ICommand SyncNowCommand { get; }

    /// <summary>All games, from Jump back in; and Choose games to sync, until first run's own list exists.</summary>
    public ICommand OpenLibraryCommand { get; }

    /// <summary>The save manager, from the Needs you card.</summary>
    public ICommand OpenSavesCommand { get; }

    public string Greeting { get; init; } = "Welcome back";

    public string Subtitle { get; init; } = "Here’s where you left off";

    public IReadOnlyList<NavItem> Tabs { get; init; } = [];

    public GameId? HeroId { get; init; }

    /// <summary>There's a game to put in the banner: the last one played, or else one that's installed.</summary>
    public bool HasHero => HeroId is not null;

    /// <summary>GameSync hasn't found any games on this PC yet (it hasn't scanned): Home says so instead of an empty banner.</summary>
    public bool NoGames { get; init; }

    /// <summary>There are games to choose from, so the Needs you card can offer to choose the ones to sync.</summary>
    public bool CanChoose => NoneSyncing && !NoGames;

    public IImage? HeroArt { get; init; }

    public IImage? HeroLogo { get; init; }

    public string? HeroTitle { get; init; }

    public string? HeroEyebrow { get; init; }

    public string? HeroChip { get; init; }

    public GameStatus? HeroStatus { get; init; }

    public string? HeroStatusLabel { get; init; }

    public string? HeroBlurb { get; init; }

    public string HeroPlayLabel { get; init; } = "Continue playing";

    public IReadOnlyList<NeedsYouItem> NeedsYou { get; init; } = [];

    /// <summary>Some games sync, and none needs the person.</summary>
    public bool AllFine => NeedsYou.Count == 0 && AnySyncing;

    /// <summary>At least one game syncs; until then the card offers to choose games instead of showing progress.</summary>
    public bool AnySyncing { get; init; }

    public bool NoneSyncing => !AnySyncing;

    /// <summary>GameSync has recorded play; until then the calendar says where play will show.</summary>
    public bool HasActivity { get; init; }

    public bool NoActivity => !HasActivity;

    public string SyncedLabel { get; init; } = "";

    public string SyncedRight { get; init; } = "";

    public double SyncedPercent { get; init; }

    public IReadOnlyList<TileItem> JumpBackIn { get; init; } = [];

    public string ActivityTitle { get; init; } = "Activity";

    public IReadOnlyList<int> Days { get; init; } = [];

    public int StartWeekday { get; init; }

    /// <param name="actions">What the page asks of the app; null where it only shows, as in the snapshot tool.</param>
    public static HomeViewModel From(LauncherHome home, IReadOnlyList<LauncherGame> all, DateTime nowLocal, LauncherActions? actions = null)
    {
        var games = all.Where(g => g.Shown).ToList();
        var hero = home.Hero;
        var needsYouCount = games.Count(g => g.NeedsYou);
        var setHidden = actions?.SetHidden;
        return new HomeViewModel
        {
            Actions = actions,
            Tabs =
            [
                new NavItem(ThisPage, "Recently played"),
                new NavItem("all", "My games"),
                new NavItem("attn", "Needs you", Count: needsYouCount > 0 ? needsYouCount.ToString(CultureInfo.InvariantCulture) : null),
            ],
            HeroId = hero?.Id,
            NoGames = games.Count == 0,
            HeroArt = ArtImages.Load(hero?.HeroPath, 1920),
            HeroLogo = ArtImages.Load(hero?.LogoPath, 760),
            HeroTitle = hero?.Title,
            HeroEyebrow = hero is null ? null : LastPlayedPhrase(hero, nowLocal),
            HeroChip = hero is null || hero.Playtime <= TimeSpan.Zero ? null : HoursPlayed(hero.Playtime),
            HeroStatus = hero?.Syncs == true ? hero.Status ?? GameStatus.Synced : null,
            HeroStatusLabel = hero is { Syncs: true, Status: null or GameStatus.Synced } ? "Save synced" : StatusLabel(hero),
            HeroBlurb = hero is null ? null : Blurb(hero),
            HeroPlayLabel = hero?.LastPlayedUtc is null ? "Play" : "Continue playing",
            NeedsYou = home.NeedsYou.Select(g => new NeedsYouItem(g.Id, g.Title, ArtImages.Load(g.CoverPath, 96), GsGameTile.InitialOf(g.Title), g.Status, ActionFor(g.Status))).ToList(),
            AnySyncing = home.Syncing > 0,
            HasActivity = home.MonthDays.Any(d => d > 0),
            SyncedLabel = home.Syncing == 0 ? $"{games.Count} games found, none syncing yet" : $"{home.Synced} of {home.Syncing} games synced",
            SyncedRight = home.Syncing == 0 ? "" : $"{Math.Round(100.0 * home.Synced / home.Syncing).ToString(CultureInfo.InvariantCulture)}%",
            SyncedPercent = home.Syncing == 0 ? 0 : 100.0 * home.Synced / home.Syncing,
            JumpBackIn = home.JumpBackIn.Select(g => Tile(g, nowLocal, width: 300, withMeta: false, setHidden)).ToList(),
            ActivityTitle = $"Activity in {home.MonthName}",
            Days = home.MonthDays,
            StartWeekday = home.MonthStartWeekday,
        };
    }

    /// <summary>My games and Needs you open the library on that view; Recently played stays chosen here for when you come back.</summary>
    partial void OnSelectedTabChanged(string value)
    {
        if (value == ThisPage || Actions is null)
        {
            return;
        }

        Actions.Show("library", value);
        SelectedTab = ThisPage;
    }

    public static TileItem Tile(LauncherGame game, DateTime nowLocal, int width = 320, bool withMeta = true, Action<GameId, bool>? setHidden = null) =>
        new(game.Id, game.Title, ArtImages.Load(game.CoverPath, width), game.Status, withMeta ? Launcher.Meta(game, nowLocal) : null)
        {
            StatusLabel = StatusLabel(game),
            IsHidden = game.IsHidden,
            ToggleHidden = setHidden is null ? null : new CommunityToolkit.Mvvm.Input.RelayCommand(() => setHidden(game.Id, !game.IsHidden)),
        };

    /// <summary>LIB-10: a game its store's cloud syncs names the store ("Synced by Steam"); other statuses say their own word.</summary>
    public static string? StatusLabel(LauncherGame? game) => game?.Status == GameStatus.BackupOnly ? StoreNames.SyncedBy(game.Store) : null;

    /// <summary>"Last played today, 21:04", "Last played yesterday", "Last played on 20 Sep".</summary>
    public static string LastPlayedPhrase(LauncherGame game, DateTime nowLocal)
    {
        if (game.Status == GameStatus.Playing)
        {
            return "Playing now";
        }

        if (game.LastPlayedUtc is not { } utc)
        {
            return game.Installed ? "Not played yet" : "Not installed on this PC";
        }

        var local = utc.ToLocalTime();
        return local.Date == nowLocal.Date ? $"Last played today, {local:HH:mm}"
            : local.Date == nowLocal.Date.AddDays(-1) ? "Last played yesterday"
            : $"Last played on {Launcher.WhenText(utc, nowLocal)}";
    }

    private static string HoursPlayed(TimeSpan playtime) =>
        playtime.TotalHours >= 1 ? $"{Math.Round(playtime.TotalHours).ToString(CultureInfo.InvariantCulture)} hrs played" : $"{Math.Max(1, (int)Math.Round(playtime.TotalMinutes))} min played";

    private static string Blurb(LauncherGame game) => game switch
    {
        { Syncs: false } => "GameSync found its saves. Confirm them in the library to back them up and keep them in sync.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { Status: GameStatus.Playing } => "Its save syncs a few seconds after you quit.",
        _ => "Its saves are backed up on this PC and in your Google Drive.",
    };

    private static string ActionFor(GameStatus? status) => status switch
    {
        GameStatus.Conflict => "Resolve",
        GameStatus.HeldForReview => "Review",
        GameStatus.SavesMissing => "Add a place",
        GameStatus.FilesInUse => "Retry",
        _ => "Details",
    };
}

/// <summary>
/// Every game with its art (LIB-11): a cover from Steam or a title cover, never an empty tile. Its pill tabs switch the
/// view in place: all games, those that need you, Steam's software, and games hidden from the launcher.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IPageSurface
{
    private IReadOnlyDictionary<string, IReadOnlyList<TileItem>> _views = new Dictionary<string, IReadOnlyList<TileItem>>();

    /// <summary>The launcher's other page, at Home's strength.</summary>
    public GlassStrength Strength => GlassStrength.Home;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tiles))]
    private string _selectedTab = "all";

    public string Title { get; init; } = "My games";

    public string Subtitle { get; init; } = "";

    public IReadOnlyList<NavItem> Tabs { get; init; } = [];

    /// <summary>The chosen view's tiles.</summary>
    public IReadOnlyList<TileItem> Tiles => _views.TryGetValue(SelectedTab, out var tiles) ? tiles : [];

    /// <param name="tab"><c>all</c> for the games, <c>attn</c> for those that need you, <c>software</c> for Steam's software, <c>hidden</c> for games hidden from the launcher.</param>
    /// <param name="actions">What the page asks of the app; null where it only shows, as in the snapshot tool.</param>
    public static LibraryViewModel From(IReadOnlyList<LauncherGame> all, DateTime nowLocal, string tab = "all", LauncherActions? actions = null)
    {
        // Each game's picture is decoded once, whichever views show it.
        var tiles = new Dictionary<GameId, TileItem>();
        foreach (var game in all)
        {
            tiles.TryAdd(game.Id, HomeViewModel.Tile(game, nowLocal, setHidden: actions?.SetHidden));
        }

        IReadOnlyList<TileItem> Of(IEnumerable<LauncherGame> games) => games.Select(g => tiles[g.Id]).Distinct().ToList();
        var games = all.Where(g => g.Shown).ToList();
        var views = new Dictionary<string, IReadOnlyList<TileItem>>
        {
            ["all"] = Of(games),
            ["attn"] = Of(games.Where(g => g.NeedsYou)),
            ["software"] = Of(all.Where(g => g.IsSoftware && !g.IsHidden)),
            ["hidden"] = Of(all.Where(g => g.IsHidden)),
        };
        var needsYou = views["attn"].Count;
        var tabs = new List<NavItem>
        {
            new("all", "All games"),
            new("attn", "Needs you", Count: needsYou > 0 ? needsYou.ToString(CultureInfo.InvariantCulture) : null),
        };
        if (views["software"].Count > 0)
        {
            tabs.Add(new NavItem("software", "Software"));
        }

        if (views["hidden"].Count > 0)
        {
            tabs.Add(new NavItem("hidden", "Hidden"));
        }

        return new LibraryViewModel
        {
            Subtitle = $"{games.Count} games · {games.Count(g => g.CoverPath is not null)} with Steam’s art",
            Tabs = tabs,
            _views = views,
            SelectedTab = tabs.Any(t => t.Id == tab) ? tab : "all",
        };
    }
}
