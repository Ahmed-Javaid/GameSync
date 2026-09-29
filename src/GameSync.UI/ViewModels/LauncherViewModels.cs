using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
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

/// <summary>What the launcher's pages ask of the app. Pages made without it (the snapshot tool) only show.</summary>
/// <param name="Play">Starts a game, with the check before playing when it syncs (PLAY-02, PLAY-03).</param>
/// <param name="SyncNow">Every game syncs at the agent's next round with nothing playing.</param>
/// <param name="Show">Opens a page by its rail id, on a tab when given: <c>("library", "attn")</c>.</param>
/// <param name="SetHidden">Hides a game from the launcher on this PC, or shows it again.</param>
public sealed record LauncherActions(Action<GameId> Play, Action SyncNow, Action<string, string?> Show, Action<GameId, bool> SetHidden)
{
    /// <summary>Makes a game a favourite on this PC, or an ordinary game again (LIB-17).</summary>
    public Action<GameId, bool>? SetFavourite { get; init; }

    /// <summary>Keeps the library's order on this PC (LIB-16).</summary>
    public Action<LibrarySort>? SetSort { get; init; }

    /// <summary>Keeps the library's view on this PC: all games, installed or local (LIB-22).</summary>
    public Action<string>? SetView { get; init; }

    /// <summary>Scan a folder for games…: the folder joins the ones every scan looks in, and this PC is scanned now (LIB-23).</summary>
    public Func<string, CancellationToken, Task<FolderScan>>? ScanFolder { get; init; }

    /// <summary>Locate the game…: the program picked marks a game installed in its folder, and Play starts it (LIB-24).</summary>
    public Action<GameId, string>? Locate { get; init; }

    /// <summary>The space the backups take on this PC's drive, and the drive's free space, off the UI thread (MGR-03).</summary>
    public Func<CancellationToken, Task<BackupSpace?>>? LoadSpace { get; init; }

    /// <summary>Opens a game's page in the library, as a click on its cover anywhere does (LIB-18).</summary>
    public Action<GameId>? OpenGame { get; init; }

    /// <summary>Reads a game's page: its places, named saves, versions and log, off the UI thread.</summary>
    public Func<GameId, CancellationToken, Task<GameDetail?>>? LoadGame { get; init; }

    /// <summary>Opens a folder in Explorer: where a game keeps its saves.</summary>
    public Action<string>? OpenFolder { get; init; }

    /// <summary>Sync these saves: confirms what the scan found for a game, so it syncs from the agent's next round (FIND-06).</summary>
    public Action<GameId>? SyncGame { get; init; }

    /// <summary>Backs a game up now (BAK-16).</summary>
    public Action<GameId>? BackUpNow { get; init; }

    /// <summary>Save as…: keeps the game's save as it is now under a name (BAK-18).</summary>
    public Action<GameId, string>? SaveAs { get; init; }

    /// <summary>Brings a version back, a named save's (with its name) or any other; this PC's files are kept first.</summary>
    public Action<GameId, VersionId, string?>? Restore { get; init; }

    /// <summary>A game's saves in the save manager: its named saves, where they are, every version and its log (MGR-07).</summary>
    public Action<GameId>? OpenSaves { get; init; }

    /// <summary>A game's Properties, on a section (<c>general</c>, <c>launch</c>, <c>files</c>, <c>saves</c>, <c>sync</c>) or the first (LIB-20).</summary>
    public Action<GameId, string?>? OpenProperties { get; init; }

    /// <summary>Opens a link GameSync made in the app Windows keeps for it: a game's store page, Install through Steam.</summary>
    public Action<string>? OpenLink { get; init; }

    /// <summary>Keeps a save held for review as the game's current one, as <c>gamesync approve</c> does.</summary>
    public Action<GameId>? Approve { get; init; }

    /// <summary>A game's conflict in the save manager: the one waiting for the person, or the last one settled (SYNC-10).</summary>
    public Action<GameId>? OpenConflict { get; init; }

    /// <summary>Reads a game's conflict, off the UI thread.</summary>
    public Func<GameId, CancellationToken, Task<ConflictDetail?>>? LoadConflict { get; init; }

    /// <summary>Settles a waiting conflict: this PC's save (true), or the cloud's version given (SYNC-11).</summary>
    public Action<GameId, bool, VersionId?>? Resolve { get; init; }

    /// <summary>Switches to the save that lost the game's last conflict (SYNC-04).</summary>
    public Action<GameId>? Swap { get; init; }

    /// <summary>Syncs one game again, when its files were in use (SYNC-02).</summary>
    public Action<GameId>? Retry { get; init; }

    /// <summary>Gives a named save another name: the game, the name, the new name (BAK-18).</summary>
    public Action<GameId, string, string>? RenameSave { get; init; }

    /// <summary>Takes a save's name away; the save stays in the history (BAK-18).</summary>
    public Action<GameId, string>? ForgetSave { get; init; }

    /// <summary>Add a place, over the page (FOLD-01).</summary>
    public Action<GameId>? OpenAddPlace { get; init; }

    /// <summary>Looks at a folder or file picked in Add a place, off the UI thread.</summary>
    public Func<GameId, string, CancellationToken, Task<NewPlaceLook>>? LookPlace { get; init; }

    /// <summary>Adds a place to a game: the folder or file, and what it holds.</summary>
    public Action<GameId, string, SaveCategory>? AddPlace { get; init; }

    /// <summary>Import kept saves, over the page (BAK-19).</summary>
    public Action<GameId>? OpenImportKept { get; init; }

    /// <summary>A game's places, for which one kept copies copy.</summary>
    public Func<GameId, CancellationToken, Task<IReadOnlyList<SaveRoot>>>? SaveRoots { get; init; }

    /// <summary>Reads a folder of kept saves (false) or imports them as named saves (true): the game, the folder, the place they copy.</summary>
    public Func<GameId, string, string?, bool, CancellationToken, Task<ImportReport>>? KeptSaves { get; init; }

    /// <summary>Every game's saves at a glance, for the save manager's table, off the UI thread.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<GameSaveSummary>>>? LoadSaves { get; init; }

    /// <summary>What a game's Properties show, with the files in its save places, off the UI thread (LIB-20, FIND-12).</summary>
    public Func<GameId, CancellationToken, Task<GameProperties?>>? LoadProperties { get; init; }

    /// <summary>Saves what the person changed in a game's Properties; the pages show it once it's done.</summary>
    public Action<GameId, GamePropertiesChange>? SaveProperties { get; init; }

    /// <summary>Closes the dialog over the page.</summary>
    public Action? CloseDialog { get; init; }
}

/// <summary>
/// A game as the launcher lists it, on a cover tile and as a row of the library's list: its cover (or none, for a
/// title cover), status and meta line, and what a right-click offers (Play, favourite, hide).
/// </summary>
public sealed record TileItem(GameId Id, string Title, IImage? Art, GameStatus? Status, string? Meta)
{
    /// <summary>The badge's words when the status's own aren't enough: which store syncs it (LIB-10).</summary>
    public string? StatusLabel { get; init; }

    /// <summary>The small cover for the library's list (Steam's 300×450 capsule, decoded small).</summary>
    public IImage? SmallArt { get; init; }

    public string Initial => GsGameTile.InitialOf(Title);

    /// <summary>Installed on this PC; the list dims a game that isn't, and it can't be played from here.</summary>
    public bool Installed { get; init; } = true;

    public bool IsHidden { get; init; }

    public bool IsFavourite { get; init; }

    /// <summary>The status shows under the game's name in the list only when it needs the person or the game is running.</summary>
    public bool ShowsListStatus => Status is GameStatus.Playing || SyncCounts.NeedsYou(Status);

    /// <summary>Starts the game; null where the page can't, or the game isn't installed here.</summary>
    public ICommand? PlayCommand { get; init; }

    /// <summary>Hides the game from the launcher on this PC, or shows it again; null where the page can't.</summary>
    public ICommand? ToggleHidden { get; init; }

    /// <summary>Makes it a favourite on this PC, or not; null where the page can't.</summary>
    public ICommand? ToggleFavourite { get; init; }

    public string HideLabel => IsHidden ? "Show in the launcher" : "Hide from the launcher";

    public string FavouriteLabel => IsFavourite ? "Remove from favourites" : "Add to favourites";

    /// <summary>Anything to offer on a right-click.</summary>
    public bool HasMenu => ToggleHidden is not null || ToggleFavourite is not null;

    /// <summary>What a screen reader says for the game's row or tile: its name, what needs doing, and whether it's here (A11Y-03).</summary>
    public string SpokenName => string.Join(", ", new[]
    {
        Title,
        Status is { } status && status != GameStatus.Synced ? StatusLabel ?? GsStatusBadge.Describe(status).Word : null,
        Installed ? null : "not installed on this PC",
    }.OfType<string>());

    public override string ToString() => SpokenName;
}

/// <summary>A row of the Needs you card: a small cover, the game, its status and the button that deals with it.</summary>
public sealed record NeedsYouItem(GameId Id, string Title, IImage? Art, string Initial, GameStatus? Status, string Action);

/// <summary>
/// What Home's top bar says about the cloud and the PCs (Nielsen's visibility of system status): where the saves go and
/// whether it's reachable, this PC's name, and the other PCs with when each was last seen.
/// </summary>
public sealed record HomeStatus(string Cloud, string CloudLine, string ThisPc, IReadOnlyList<string> OtherPcs)
{
    public bool HasOtherPcs => OtherPcs.Count > 0;

    public bool NoOtherPcs => OtherPcs.Count == 0;
}

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
        OpenGameCommand = new RelayCommand<GameId>(game => Actions?.OpenGame?.Invoke(game));
        OpenGameSavesCommand = new RelayCommand<GameId>(game =>
        {
            // Resolve opens the game's conflict; the other actions its saves, where the details are.
            if (NeedsYou.FirstOrDefault(n => n.Id == game) is { Status: GameStatus.Conflict } && Actions?.OpenConflict is { } conflict)
            {
                conflict(game);
            }
            else
            {
                Actions?.OpenSaves?.Invoke(game);
            }
        });
        OpenHeroCommand = new RelayCommand(() =>
        {
            if (HeroId is { } game)
            {
                Actions?.OpenGame?.Invoke(game);
            }
        });
        OpenHeroSavesCommand = new RelayCommand(() =>
        {
            if (HeroId is { } game)
            {
                Actions?.OpenSaves?.Invoke(game);
            }
        });
        OpenHeroPropertiesCommand = new RelayCommand(() =>
        {
            if (HeroId is { } game)
            {
                Actions?.OpenProperties?.Invoke(game, "general");
            }
        });
    }

    /// <summary>A Needs you row's button: Resolve opens the game's conflict; Review and the others its saves in the save manager.</summary>
    public ICommand OpenGameSavesCommand { get; }

    /// <summary>The hero's Manage saves: the hero game's saves in the save manager.</summary>
    public ICommand OpenHeroSavesCommand { get; }

    /// <summary>The hero's gear: the hero game's Properties, as the gear is everywhere.</summary>
    public ICommand OpenHeroPropertiesCommand { get; }

    /// <summary>What the page asks of the app; without it, the buttons do nothing.</summary>
    public LauncherActions? Actions { get; init; }

    /// <summary>Continue playing: the hero game, the way its store starts it.</summary>
    public ICommand PlayCommand { get; }

    /// <summary>A cover or a Needs you row: that game's page in the library (LIB-18).</summary>
    public ICommand OpenGameCommand { get; }

    /// <summary>The hero's Manage saves and Game settings: the hero game's page, which holds both.</summary>
    public ICommand OpenHeroCommand { get; }

    public ICommand SyncNowCommand { get; }

    /// <summary>All games, from Jump back in; and Choose games to sync, until first run's own list exists.</summary>
    public ICommand OpenLibraryCommand { get; }

    /// <summary>The save manager, from the Needs you card.</summary>
    public ICommand OpenSavesCommand { get; }

    /// <summary>The cloud and the PCs, for the top bar's Google Drive and This PC buttons; null leaves them out.</summary>
    public HomeStatus? Status { get; init; }

    public bool HasStatus => Status is not null;

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

    /// <summary>The hero's Play, except while the hero game runs (PLAY-12).</summary>
    public bool HeroShowsPlay { get; init; } = true;

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
    /// <param name="status">The cloud and the PCs, for the top bar.</param>
    public static HomeViewModel From(LauncherHome home, IReadOnlyList<LauncherGame> all, DateTime nowLocal, LauncherActions? actions = null, HomeStatus? status = null)
    {
        var games = all.Where(g => g.Shown).ToList();
        var hero = home.Hero;
        var needsYouCount = games.Count(g => g.NeedsYou);
        return new HomeViewModel
        {
            Actions = actions,
            Status = status,
            Tabs =
            [
                new NavItem(ThisPage, "Recently played"),
                new NavItem("all", "My games"),
                new NavItem("attn", "Needs you", Count: needsYouCount > 0 ? needsYouCount.ToString(CultureInfo.InvariantCulture) : null),
            ],
            HeroId = hero?.Id,
            NoGames = games.Count == 0,
            HeroShowsPlay = hero?.IsRunning != true,
            HeroArt = ArtImages.Load(hero?.HeroPath, 1920),
            HeroLogo = ArtImages.Load(hero?.LogoPath, 760),
            HeroTitle = hero?.Title,
            HeroEyebrow = hero is null ? null : HeroPhrase(hero, nowLocal),
            HeroChip = hero is null || hero.Playtime <= TimeSpan.Zero ? null : HoursPlayed(hero.Playtime),
            // PLAY-12: the game playing now reads Playing now, whether its saves sync or not.
            HeroStatus = hero?.IsRunning == true ? GameStatus.Playing : hero?.Syncs == true ? hero.Status ?? GameStatus.Synced : null,
            HeroStatusLabel = hero?.IsRunning == true ? "Playing now" : hero is { Syncs: true, Status: null or GameStatus.Synced } ? "Save synced" : StatusLabel(hero),
            HeroBlurb = hero is null ? null : Blurb(hero),
            HeroPlayLabel = hero?.LastPlayedUtc is null ? "Play" : "Continue playing",
            NeedsYou = home.NeedsYou.Select(g => new NeedsYouItem(g.Id, g.Title, ArtImages.Load(g.CoverPath, 96), GsGameTile.InitialOf(g.Title), g.Status, ActionFor(g.Status))).ToList(),
            AnySyncing = home.Syncing > 0,
            HasActivity = home.MonthDays.Any(d => d > 0),
            SyncedLabel = home.Syncing == 0 ? $"{games.Count} games found, none syncing yet" : $"{home.Synced} of {home.Syncing} games synced",
            SyncedRight = home.Syncing == 0 ? "" : $"{Math.Round(100.0 * home.Synced / home.Syncing).ToString(CultureInfo.InvariantCulture)}%",
            SyncedPercent = home.Syncing == 0 ? 0 : 100.0 * home.Synced / home.Syncing,
            JumpBackIn = home.JumpBackIn.Select(g => Tile(g, nowLocal, width: 300, withMeta: false, actions)).ToList(),
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

    /// <param name="smallWidth">Also decode the cover this wide for the library's list; 0 leaves it out.</param>
    public static TileItem Tile(LauncherGame game, DateTime nowLocal, int width = 320, bool withMeta = true, LauncherActions? actions = null, int smallWidth = 0) =>
        new(game.Id, game.Title, ArtImages.Load(game.CoverPath, width), game.IsRunning ? GameStatus.Playing : game.Status, withMeta ? Launcher.Meta(game, nowLocal) : null)
        {
            StatusLabel = StatusLabel(game),
            SmallArt = smallWidth > 0 ? ArtImages.Load(game.CoverPath, smallWidth) : null,
            Installed = game.Installed,
            IsHidden = game.IsHidden,
            IsFavourite = game.IsFavourite,
            PlayCommand = actions is null || !game.Installed ? null : new RelayCommand(() => actions.Play(game.Id)),
            ToggleHidden = actions is null ? null : new RelayCommand(() => actions.SetHidden(game.Id, !game.IsHidden)),
            ToggleFavourite = actions?.SetFavourite is { } setFavourite ? new RelayCommand(() => setFavourite(game.Id, !game.IsFavourite)) : null,
        };

    /// <summary>LIB-10: a game its store's cloud syncs names the store ("Synced by Steam"); other statuses say their own word.</summary>
    public static string? StatusLabel(LauncherGame? game) => game?.Status == GameStatus.BackupOnly ? StoreNames.SyncedBy(game.Store) : null;

    /// <summary>
    /// The hero's eyebrow: for the game playing now, since when and how it's installed ("Playing now · since 20:41 · In its
    /// own folder"); otherwise when it was last played.
    /// </summary>
    public static string HeroPhrase(LauncherGame game, DateTime nowLocal)
    {
        if (!game.IsRunning)
        {
            return LastPlayedPhrase(game, nowLocal);
        }

        var where = StoreNames.Name(game.Store) ?? (game.Store == StoreKind.Loose ? "In its own folder" : null);
        return string.Join(" · ", new[] { "Playing now", game.RunningSinceUtc is { } since ? $"since {since.ToLocalTime():HH:mm}" : null, where }.OfType<string>());
    }

    /// <summary>"Last played today, 21:04", "Last played yesterday", "Last played on 20 Sep".</summary>
    public static string LastPlayedPhrase(LauncherGame game, DateTime nowLocal)
    {
        if (game.IsRunning)
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
        { IsRunning: true, Syncs: false } => "GameSync saw it start, so its play counts. Its saves aren't synced yet: sync them from its page when you're done.",
        { Syncs: false } => "GameSync found its saves. Sync them from its page to back them up and keep them in step.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { IsRunning: true } => "Its save syncs a few seconds after you quit.",
        _ => "Its saves are backed up on this PC and in your Google Drive.",
    };

    /// <summary>The same words as the game's page and its saves use for what needs doing.</summary>
    private static string ActionFor(GameStatus? status) => GameViewModel.ActionFor(status).Label;
}
