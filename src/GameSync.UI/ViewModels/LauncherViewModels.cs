using System.Globalization;
using Avalonia.Media;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>A game tile: its cover (or none, for a title cover), status and meta line, and hiding it from the launcher.</summary>
public sealed record TileItem(GameId Id, string Title, IImage? Art, GameStatus? Status, string? Meta)
{
    public bool IsHidden { get; init; }

    /// <summary>Hides the game from the launcher on this PC, or shows it again; null where the page can't.</summary>
    public System.Windows.Input.ICommand? ToggleHidden { get; init; }

    public string HideLabel => IsHidden ? "Show in the launcher" : "Hide from the launcher";
}

/// <summary>A row of the Needs you card: a small cover, the game, its status and the button that deals with it.</summary>
public sealed record NeedsYouItem(GameId Id, string Title, IImage? Art, string Initial, GameStatus? Status, string Action);

/// <summary>The launcher home (PLAY-01): the hero, Needs you, Jump back in and this month's activity.</summary>
public sealed class HomeViewModel
{
    public string Greeting { get; init; } = "Welcome back";

    public string Subtitle { get; init; } = "Here’s where you left off";

    public IReadOnlyList<NavItem> Tabs { get; init; } = [];

    public GameId? HeroId { get; init; }

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

    /// <param name="setHidden">Hides a game from the launcher or shows it again; null where the page can't.</param>
    public static HomeViewModel From(LauncherHome home, IReadOnlyList<LauncherGame> all, DateTime nowLocal, Action<GameId, bool>? setHidden = null)
    {
        var games = all.Where(g => g.Shown).ToList();
        var hero = home.Hero;
        var needsYouCount = games.Count(g => g.NeedsYou);
        return new HomeViewModel
        {
            Tabs =
            [
                new NavItem("recent", "Recently played"),
                new NavItem("all", "My games"),
                new NavItem("attn", "Needs you", Count: needsYouCount > 0 ? needsYouCount.ToString(CultureInfo.InvariantCulture) : null),
            ],
            HeroId = hero?.Id,
            HeroArt = ArtImages.Load(hero?.HeroPath, 1920),
            HeroLogo = ArtImages.Load(hero?.LogoPath, 760),
            HeroTitle = hero?.Title,
            HeroEyebrow = hero is null ? null : LastPlayedPhrase(hero, nowLocal),
            HeroChip = hero is null || hero.Playtime <= TimeSpan.Zero ? null : HoursPlayed(hero.Playtime),
            HeroStatus = hero?.Syncs == true ? hero.Status ?? GameStatus.Synced : null,
            HeroStatusLabel = hero is { Syncs: true, Status: null or GameStatus.Synced } ? "Save synced" : null,
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

    public static TileItem Tile(LauncherGame game, DateTime nowLocal, int width = 320, bool withMeta = true, Action<GameId, bool>? setHidden = null) =>
        new(game.Id, game.Title, ArtImages.Load(game.CoverPath, width), game.Status, withMeta ? Launcher.Meta(game, nowLocal) : null)
        {
            IsHidden = game.IsHidden,
            ToggleHidden = setHidden is null ? null : new CommunityToolkit.Mvvm.Input.RelayCommand(() => setHidden(game.Id, !game.IsHidden)),
        };

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

/// <summary>Every game with its art (LIB-11): a cover from Steam or a title cover, never an empty tile.</summary>
public sealed class LibraryViewModel
{
    public string Title { get; init; } = "My games";

    public string Subtitle { get; init; } = "";

    public IReadOnlyList<NavItem> Tabs { get; init; } = [];

    public IReadOnlyList<TileItem> Tiles { get; init; } = [];

    /// <param name="tab"><c>all</c> for the games, <c>attn</c> for those that need you, <c>software</c> for Steam's software, <c>hidden</c> for games hidden from the launcher.</param>
    /// <param name="setHidden">Hides a game from the launcher or shows it again; null where the page can't.</param>
    public static LibraryViewModel From(IReadOnlyList<LauncherGame> all, DateTime nowLocal, string tab = "all", Action<GameId, bool>? setHidden = null)
    {
        var games = all.Where(g => g.Shown).ToList();
        var software = all.Count(g => g.IsSoftware && !g.IsHidden);
        var hidden = all.Count(g => g.IsHidden);
        var withArt = games.Count(g => g.CoverPath is not null);
        var needsYou = games.Count(g => g.NeedsYou);
        var shown = tab switch
        {
            "attn" => games.Where(g => g.NeedsYou),
            "software" => all.Where(g => g.IsSoftware && !g.IsHidden),
            "hidden" => all.Where(g => g.IsHidden),
            _ => games,
        };
        var tabs = new List<NavItem>
        {
            new("all", "All games"),
            new("attn", "Needs you", Count: needsYou > 0 ? needsYou.ToString(CultureInfo.InvariantCulture) : null),
        };
        if (software > 0)
        {
            tabs.Add(new NavItem("software", "Software"));
        }

        if (hidden > 0)
        {
            tabs.Add(new NavItem("hidden", "Hidden"));
        }

        return new LibraryViewModel
        {
            Subtitle = $"{games.Count} games · {withArt} with Steam’s art",
            Tabs = tabs,
            SelectedTab = tab,
            Tiles = shown.Select(g => HomeViewModel.Tile(g, nowLocal, setHidden: setHidden)).ToList(),
        };
    }

    public string SelectedTab { get; init; } = "all";
}
