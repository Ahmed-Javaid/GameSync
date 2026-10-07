using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>
/// An achievement as the screens show it (design system version 33): its badge (the icon, locked or hidden, and its
/// metal), its name and what it asks, when it was unlocked, and how many of Steam's players have it. A hidden one still
/// locked keeps its secret (the owner, 3 Oct 2026): it reads "Hidden achievement", and only how many players have it shows.
/// </summary>
/// <param name="When">"Today", "26 Sep", or "Unlocked" when Steam kept no time; empty while it's locked.</param>
/// <param name="WhenFull">"26 Sep, 22:41"; "Not yet" while it's locked.</param>
/// <param name="State"><c>unlocked</c>, <c>locked</c> or <c>hidden</c> (hidden and still locked).</param>
/// <param name="Tier"><c>gold</c>, <c>silver</c> or <c>bronze</c>, once its rarity is known.</param>
public sealed record AchievementItem(string Id, string Name, string? Description, string When, string WhenFull, IImage? Icon, string State, string? Tier,
    double? Percent, DateTime? UnlockedUtc, string Tip)
{
    /// <summary>Steam's icons are 64px pictures: decoded as they are, whatever size a badge shows them.</summary>
    public const int IconSize = 64;

    public const string HiddenName = "Hidden achievement";

    public bool IsUnlocked => State == "unlocked";

    public bool IsSecret => State == "hidden";

    public bool HasWhen => When.Length > 0;

    public bool HasPercent => Percent is not null;

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>"2.7%", "41%".</summary>
    public string? PercentText => Percent is { } p ? PercentOf(p) : null;

    /// <summary>"Very rare".</summary>
    public string? Rarity => AchievementTiers.Rarity(Percent);

    /// <summary>"Very rare · 2.7% of players".</summary>
    public string? RarityLine => Percent is { } p ? $"{AchievementTiers.Rarity(p)} · {PercentOf(p)} of players" : null;

    /// <summary>An unlocked one's metal beside its percent in a row.</summary>
    public bool HasDot => IsUnlocked && Tier is not null;

    public bool IsGoldDot => IsUnlocked && Tier == "gold";

    public bool IsSilverDot => IsUnlocked && Tier == "silver";

    public bool IsBronzeDot => IsUnlocked && Tier == "bronze";

    /// <summary>What a screen reader says for it, as its tooltip does.</summary>
    public override string ToString() => Tip;

    /// <summary>"9.8%", "0.4%", "23%".</summary>
    public static string PercentOf(double value) =>
        (value < 10 ? value.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(value).ToString(CultureInfo.InvariantCulture)) + "%";

    public static AchievementItem Of(AchievementShown a, DateTime nowLocal)
    {
        var state = a.Unlocked ? "unlocked" : a.Secret ? "hidden" : "locked";
        var when = a.KnownUnlockUtc is { } at ? Day(at, nowLocal) : a.Unlocked ? "Unlocked" : "";
        var full = a.KnownUnlockUtc is { } time ? DayAndTime(time, nowLocal) : a.Unlocked ? "Unlocked, on a day Steam didn't keep" : "Not yet";
        var tier = a.Tier switch
        {
            AchievementTier.Gold => "gold",
            AchievementTier.Silver => "silver",
            AchievementTier.Bronze => "bronze",
            _ => null,
        };
        var players = a.Percent is { } p ? $", {PercentOf(p)} of players" : "";
        if (a.Secret)
        {
            // Nothing of what it is: not its name, what it asks, or its picture.
            return new AchievementItem(a.Id, HiddenName, "Keep playing to reveal it.", "", "Not yet", null, state, tier, a.Percent, null,
                $"{HiddenName}, locked{players}");
        }

        var what = a.Description is { Length: > 0 } description ? $"{a.Name}: {description}" : a.Name;
        var tip = $"{what} ({(a.Unlocked ? $"unlocked {(a.KnownUnlockUtc is null ? "" : when)}".TrimEnd() : "locked")}{players})";
        return new AchievementItem(a.Id, a.Name, a.Description, when, full, ArtImages.Load(a.Icon, IconSize), state, tier, a.Percent, a.UnlockedUtc, tip);
    }

    /// <summary>"Today", "Yesterday", "26 Sep", or "26 Sep 2025" in another year.</summary>
    public static string Day(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime().Date;
        return local == nowLocal.Date ? "Today"
            : local == nowLocal.Date.AddDays(-1) ? "Yesterday"
            : local.Year == nowLocal.Year ? local.ToString("d MMM", CultureInfo.InvariantCulture)
            : local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>"Today, 21:14", "26 Sep, 22:41".</summary>
    public static string DayAndTime(DateTime utc, DateTime nowLocal) =>
        $"{Day(utc, nowLocal)}, {utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}";
}

/// <summary>How far a game is, as its overview shows it (design system → AchievementsOverview): its ring, its tiers and its Zenith.</summary>
/// <param name="CompletedOn">The day Zenith was earned ("2 Oct"), once every one is unlocked and Steam kept the time.</param>
public sealed record AchievementsProgress(int Done, int Total, int Gold, int Silver, int Bronze, string? CompletedOn)
{
    public static readonly AchievementsProgress None = new(0, 0, 0, 0, 0, null);

    public double Percent => Total == 0 ? 0 : 100.0 * Done / Total;

    /// <summary>"72%", rounded down so a game is never at 100% before its Zenith.</summary>
    public string PercentText => Math.Floor(Percent).ToString(CultureInfo.InvariantCulture) + "%";

    public bool IsZenith => Total > 0 && Done >= Total;

    public int ToGo => Math.Max(0, Total - Done);

    public static AchievementsProgress Of(GameAchievementsView view, DateTime nowLocal) => new(
        view.Unlocked,
        view.Total,
        view.Count(AchievementTier.Gold),
        view.Count(AchievementTier.Silver),
        view.Count(AchievementTier.Bronze),
        view.CompletedUtc is { } at ? AchievementItem.Day(at, nowLocal) : null);
}

/// <summary>A step back in a breadcrumb: its name and where it goes.</summary>
public sealed record Crumb(string Label, ICommand Command, string? Spoken = null)
{
    public override string ToString() => Spoken ?? Label;
}

/// <summary>What the Achievements page reads: what's kept, what's kept after asking Steam for what isn't, or rarity asked again now.</summary>
public enum AchievementsLoad
{
    Read,
    Fetch,
    Refresh,
}

/// <summary>
/// Every achievement of a game (design system → AchievementsScreen; the owner, 3 Oct 2026: "a way to see all unlockable
/// achievements; hidden achievements stay hidden until they're unlocked"): the header with the game's overview; the list
/// with All, Unlocked, Locked and Hidden, a search that never finds a hidden one, and a sort; and what the one picked is.
/// Opened from a game's page (View all), Home's card or the Achievements page; Back returns there.
/// </summary>
public sealed partial class AchievementsViewModel : ObservableObject
{
    public const string AllTab = "all";
    public const string UnlockedTab = "unlocked";
    public const string LockedTab = "locked";
    public const string HiddenTab = "hidden";

    /// <summary>The orders on offer, as the sort menu names them.</summary>
    public static readonly IReadOnlyList<(string Id, string Label)> Sorts =
    [
        ("recent", "Recently unlocked"),
        ("rarest", "Rarest first"),
        ("players", "Most players first"),
        ("name", "Name"),
    ];

    private readonly LauncherActions? _actions;
    private IReadOnlyList<AchievementItem> _all = [];
    private IReadOnlyDictionary<string, int> _ranks = new Dictionary<string, int>();
    private int _loads;

    [ObservableProperty]
    private string _subtitle = "From Steam on this PC";

    [ObservableProperty]
    private AchievementsProgress _progress = AchievementsProgress.None;

    /// <summary>The game's art, faint behind the header.</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _headArt;

    [ObservableProperty]
    private IReadOnlyList<NavItem> _tabs = [];

    [ObservableProperty]
    private string _tab = AllTab;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(SortName), nameof(SortsRecent), nameof(SortsRarest), nameof(SortsPlayers), nameof(SortsName))]
    private string _sort = "recent";

    [ObservableProperty]
    private IReadOnlyList<AchievementItem> _shown = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicked), nameof(PickedText), nameof(PickedWhen), nameof(PickedPlayers), nameof(PickedBar), nameof(PickedRarity),
        nameof(PickedRank), nameof(HasPickedRank), nameof(PickedTier), nameof(HasPickedTier), nameof(PickedGold), nameof(PickedSilver), nameof(PickedBronze))]
    private AchievementItem? _picked;

    /// <summary>What the list says when nothing is in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNothing))]
    private string? _nothing;

    /// <summary>Steam's percentages are kept, so the rarity sorts and the tiers mean something.</summary>
    [ObservableProperty]
    private bool _hasRarity;

    /// <param name="here">The breadcrumb's last step: "Achievements" in the library, the game's name on the Achievements page.</param>
    /// <param name="crumbs">The steps before it.</param>
    public AchievementsViewModel(GameId game, string title, LauncherActions? actions, Action back, IReadOnlyList<Crumb> crumbs, string here = "Achievements")
    {
        Game = game;
        GameTitle = title;
        _actions = actions;
        Crumbs = crumbs;
        Here = here;
        BackCommand = new RelayCommand(back);
        SortByCommand = new RelayCommand<string>(id =>
        {
            if (id is not null && Sorts.Any(s => s.Id == id))
            {
                Sort = id;
            }
        });
    }

    public GameId Game { get; }

    public string GameTitle { get; }

    public string Title => $"Achievements in {GameTitle}";

    public IReadOnlyList<Crumb> Crumbs { get; }

    public string Here { get; }

    /// <summary>Back: to the page that opened it. Esc, Alt+Left and the mouse's back button do the same.</summary>
    public ICommand BackCommand { get; }

    public ICommand SortByCommand { get; }

    /// <summary>Every achievement as read, for the tests.</summary>
    public IReadOnlyList<AchievementItem> All => _all;

    public bool HasNothing => Nothing is not null;

    public string SortLabel => Sorts.First(s => s.Id == Sort).Label;

    public string SortName => $"Sort by: {SortLabel}";

    public bool SortsRecent => Sort == "recent";

    public bool SortsRarest => Sort == "rarest";

    public bool SortsPlayers => Sort == "players";

    public bool SortsName => Sort == "name";

    public bool HasPicked => Picked is not null;

    /// <summary>What it asks; a hidden one's stays secret.</summary>
    public string PickedText => Picked switch
    {
        null => "",
        { IsSecret: true } => "Its name and what it asks stay secret until you unlock it.",
        { Description: { Length: > 0 } description } => description,
        _ => "Steam gives no description for it.",
    };

    public string PickedWhen => Picked?.WhenFull ?? "";

    public string PickedPlayers => Picked?.PercentText ?? "Not known yet";

    /// <summary>The bar under how many players have it, never quite empty.</summary>
    public double PickedBar => Math.Max(1, Picked?.Percent ?? 0);

    public string PickedRarity => Picked?.Rarity ?? "Not known yet";

    /// <summary>Where an unlocked one stands among the person's own: "Your rarest", "Your 3rd rarest".</summary>
    public string? PickedRank => Picked is { } a && _ranks.TryGetValue(a.Id, out var rank)
        ? rank == 1 ? "Your rarest" : $"Your {Ordinal(rank)} rarest"
        : null;

    public bool HasPickedRank => PickedRank is not null;

    /// <summary>An unlocked one's tier, for the chip under its name.</summary>
    public string? PickedTier => Picked is { IsUnlocked: true } a ? a.Tier : null;

    public bool HasPickedTier => PickedTier is not null;

    public bool PickedGold => PickedTier == "gold";

    public bool PickedSilver => PickedTier == "silver";

    public bool PickedBronze => PickedTier == "bronze";

    /// <summary>
    /// Shows what's known now, if anything, then reads again once Steam was asked for every icon the list shows (never a
    /// hidden one's) and for rarity a week old.
    /// </summary>
    public async void Load(GameAchievementsView? known = null)
    {
        var now = DateTime.Now;
        if (known is not null)
        {
            Show(known, now);
        }

        if (_actions?.LoadAchievements is not { } load)
        {
            return;
        }

        var mine = ++_loads;
        try
        {
            if (known is null && await load(Game, false, CancellationToken.None) is { } read && mine == _loads)
            {
                Show(read, DateTime.Now);
            }

            if (await load(Game, true, CancellationToken.None) is { } fetched && mine == _loads)
            {
                Show(fetched, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or HttpRequestException)
        {
            // What's shown stays; the icons come another time.
        }
    }

    /// <summary>The game's achievements as read now; the tab, the search, the sort and the one picked stay.</summary>
    public void Show(GameAchievementsView view, DateTime nowLocal)
    {
        _all = view.All.Select(a => AchievementItem.Of(a, nowLocal)).ToList();
        _ranks = _all.Where(a => a is { IsUnlocked: true, Percent: not null })
            .OrderBy(a => a.Percent)
            .Select((a, i) => (a.Id, Rank: i + 1))
            .ToDictionary(r => r.Id, r => r.Rank);
        Progress = AchievementsProgress.Of(view, nowLocal);
        HasRarity = _all.Any(a => a.Percent is not null);
        Subtitle = view.From switch
        {
            // KAN-111: a game Steam here holds none for shows Steam's list, every one locked.
            AchievementsFrom.ListNotSteamCopy => "Steam's list · this copy isn't Steam's, so nothing shows as unlocked here",
            AchievementsFrom.ListNotPlayedHere => "Steam's list · Steam on this PC hasn't seen you play it yet",
            // KAN-123: a copy Steam doesn't run, from its own record; the names, icons and rarity are Steam's.
            AchievementsFrom.CopyRecord when HasRarity => "From this copy's own record · how rare each is, from Steam's players this week",
            AchievementsFrom.CopyRecord => "From this copy's own record · how rare each is comes once Steam has been asked",
            _ when HasRarity => "From Steam on this PC · how rare each is, from Steam's players this week",
            _ => "From Steam on this PC · how rare each is comes once Steam has been asked",
        };
        var counts = (
            All: _all.Count,
            Unlocked: _all.Count(a => a.IsUnlocked),
            Locked: _all.Count(a => a.State == "locked"),
            Hidden: _all.Count(a => a.IsSecret));
        var tabs = new List<NavItem>
        {
            new(AllTab, "All", Count: counts.All.ToString(CultureInfo.InvariantCulture)),
            new(UnlockedTab, "Unlocked", Count: counts.Unlocked.ToString(CultureInfo.InvariantCulture)),
        };
        if (counts.Locked > 0)
        {
            tabs.Add(new NavItem(LockedTab, "Locked", Count: counts.Locked.ToString(CultureInfo.InvariantCulture)));
        }

        if (counts.Hidden > 0)
        {
            tabs.Add(new NavItem(HiddenTab, "Hidden", Count: counts.Hidden.ToString(CultureInfo.InvariantCulture)));
        }

        Tabs = tabs;
        if (tabs.All(t => t.Id != Tab))
        {
            Tab = AllTab;
        }

        Rebuild();
    }

    /// <summary>Picks an achievement by its id, as a click on it in the Achievements page's recent unlocks does.</summary>
    public void Pick(string id)
    {
        if (_all.FirstOrDefault(a => a.Id == id) is { } item)
        {
            if (!Shown.Contains(item))
            {
                (Tab, Search) = (AllTab, "");
            }

            Picked = Shown.FirstOrDefault(a => a.Id == id);
        }
    }

    partial void OnTabChanged(string value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    partial void OnSortChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var inTab = _all.Where(a => Tab switch
        {
            UnlockedTab => a.IsUnlocked,
            LockedTab => a.State == "locked",
            HiddenTab => a.IsSecret,
            _ => true,
        });

        // A hidden one is never searched: what it is stays secret, and so does whether a word would find it.
        var needle = Search.Trim();
        var matching = needle.Length == 0 ? inTab
            : inTab.Where(a => !a.IsSecret && (a.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                                               a.Description?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) == true));
        var picked = Picked?.Id;
        Shown = Order(matching).ToList();
        Nothing = Shown.Count > 0 ? null
            : needle.Length > 0 ? $"No achievement matches “{needle}”. Hidden ones aren't searched: they stay secret."
            : Tab switch
            {
                UnlockedTab => "None unlocked yet.",
                LockedTab => "Every one is unlocked.",
                HiddenTab => "No hidden ones left to find.",
                _ => "Nothing here yet.",
            };
        Picked = Shown.FirstOrDefault(a => a.Id == picked) ?? Shown.FirstOrDefault();
    }

    /// <summary>
    /// Recently unlocked: the unlocked ones newest first, then the rest with what most players have first (what's within
    /// reach). Rarest first and Most players first by Steam's percentages; Name, hidden ones last. Ties go by name.
    /// </summary>
    private IEnumerable<AchievementItem> Order(IEnumerable<AchievementItem> items)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        return Sort switch
        {
            "rarest" => items.OrderBy(a => a.Percent ?? double.MaxValue).ThenBy(a => a.Name, byName),
            "players" => items.OrderByDescending(a => a.Percent ?? -1).ThenBy(a => a.Name, byName),
            "name" => items.OrderBy(a => a.IsSecret).ThenBy(a => a.Name, byName),
            _ => items.OrderByDescending(a => a.IsUnlocked)
                .ThenByDescending(a => a.UnlockedUtc ?? DateTime.MinValue)
                .ThenByDescending(a => a.Percent ?? -1)
                .ThenBy(a => a.Name, byName),
        };
    }

    private static string Ordinal(int n) => n.ToString(CultureInfo.InvariantCulture) + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th",
    });
}

/// <summary>A game on the Achievements page's shelves and table: its cover, name and how far it is.</summary>
public sealed record TrophyGame(GameId Id, string Title, IImage? Cover, string Initial, AchievementsProgress Progress, string Last, DateTime? LastUtc,
    DateTime? CompletedUtc)
{
    public bool IsZenith => Progress.IsZenith;

    /// <summary>"123 / 171".</summary>
    public string CountText => $"{Progress.Done.ToString(CultureInfo.InvariantCulture)} / {Progress.Total.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Closest to Zenith's line: "72% · 48 to go".</summary>
    public string ToGoText => $"{Progress.PercentText} · {Progress.ToGo.ToString(CultureInfo.InvariantCulture)} to go";

    /// <summary>The shelf's line under a Zenith: "Zenith · 2 Oct".</summary>
    public string ZenithText => Progress.CompletedOn is { } on ? $"Zenith · {on}" : "Zenith";

    public string GoldText => Progress.Gold.ToString(CultureInfo.InvariantCulture);

    public string SilverText => Progress.Silver.ToString(CultureInfo.InvariantCulture);

    public string BronzeText => Progress.Bronze.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => IsZenith
        ? $"{Title}: Zenith, every achievement unlocked{(Progress.CompletedOn is { } on ? $", {on}" : "")}"
        : $"{Title}: {Progress.Done} of {Progress.Total} achievements, {Progress.PercentText}, last unlocked {Last}";
}

/// <summary>A recent unlock on the Achievements page: the achievement, and its game's name with how many players have it.</summary>
public sealed record TrophyUnlock(GameId Game, string GameTitle, AchievementItem Achievement)
{
    /// <summary>"Sekiro: Shadows Die Twice · 9.1% of players".</summary>
    public string Sub => Achievement.PercentText is { } percent ? $"{GameTitle} · {percent} of players" : GameTitle;

    public override string ToString() => $"{Achievement.Name}, {GameTitle}, unlocked {Achievement.When}";

    /// <summary>What a screen reader says for one within reach: "The Lumberjack, Valheim, locked, 68% of players have it".</summary>
    public string ReachSpoken => $"{Achievement.Name}, {GameTitle}, locked{(Achievement.PercentText is { } p ? $", {p} of players have it" : "")}";
}

/// <summary>The Every game table's order: by a heading, and a second click reverses it.</summary>
public enum TrophySort
{
    Progress,
    Game,
    Unlocked,
    Last,
}

/// <summary>
/// The Achievements page, from the rail's trophy (design system → TrophyRoomScreen; the owner, 3 Oct 2026: "a way to
/// track platinum / 100% achievements"): every game's achievements together. The band at the top (the average, the
/// unlocks, the Zeniths, the tiers and the rarest you have), the Zeniths shelf, Closest to Zenith, Recent unlocks
/// and Every game; a game opens its achievements here, with Back to this page. It lives as long as the window.
/// </summary>
public sealed partial class TrophyRoomViewModel : ObservableObject, IPageSurface, IRailPage
{
    private readonly LauncherActions? _actions;
    private IReadOnlyList<GameAchievementsView> _views = [];
    private IReadOnlyDictionary<GameId, LauncherGame> _games = new Dictionary<GameId, LauncherGame>();
    private IReadOnlyList<TrophyGame> _all = [];
    private int _loads;

    /// <summary>A game's achievements open on this page; null shows the page itself.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRoom), nameof(Strength), nameof(BackdropArt))]
    private AchievementsViewModel? _game;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoGames))]
    private bool _hasGames;

    /// <summary>Read once at least, so an empty page says there are none rather than nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoGames))]
    private bool _read;

    [ObservableProperty]
    private double _average;

    [ObservableProperty]
    private string _averageLabel = "0%";

    [ObservableProperty]
    private string _averageSpoken = "";

    [ObservableProperty]
    private string _unlockedText = "0";

    [ObservableProperty]
    private string _unlockedSub = "";

    [ObservableProperty]
    private string _zenithCount = "0";

    [ObservableProperty]
    private bool _hasZenith;

    [ObservableProperty]
    private string _zenithSub = "None yet";

    /// <summary>The monument's line under its count: "Latest:" over the latest game's name, or "None yet".</summary>
    [ObservableProperty]
    private string _zenithSubLines = "None yet";

    /// <summary>The monument for a screen reader: how many Zeniths, and the latest (KAN-128).</summary>
    [ObservableProperty]
    private string _zenithSpoken = "Zeniths: none yet";

    [ObservableProperty]
    private string _gold = "0";

    [ObservableProperty]
    private string _silver = "0";

    [ObservableProperty]
    private string _bronze = "0";

    /// <summary>The rarest achievement the person has, of every game's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRarest))]
    private TrophyUnlock? _rarest;

    [ObservableProperty]
    private IReadOnlyList<TrophyGame> _zeniths = [];

    /// <summary>The shelf with none on it: "None yet. Risk of Rain 2 is closest: 48 to go."</summary>
    [ObservableProperty]
    private string? _zenithNote;

    [ObservableProperty]
    private IReadOnlyList<TrophyGame> _closest = [];

    [ObservableProperty]
    private IReadOnlyList<TrophyUnlock> _recent = [];

    /// <summary>Within reach (KAN-105): the locked achievements most players have, in the games started, never a hidden one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReach))]
    private IReadOnlyList<TrophyUnlock> _reach = [];

    [ObservableProperty]
    private IReadOnlyList<TrophyGame> _rows = [];

    [ObservableProperty]
    private string _gamesSubtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ByGame), nameof(ByProgress), nameof(ByUnlocked), nameof(ByLast), nameof(GameIdle), nameof(ProgressIdle), nameof(UnlockedIdle),
        nameof(LastIdle))]
    private TrophySort _by = TrophySort.Progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Arrow))]
    private bool _reversed;

    /// <summary>Refresh rarity is asking Steam.</summary>
    [ObservableProperty]
    private bool _refreshing;

    public TrophyRoomViewModel(LauncherActions? actions = null)
    {
        _actions = actions;
        OpenGameCommand = new RelayCommand<GameId>(game => OpenGame(game));
        OpenUnlockCommand = new RelayCommand<TrophyUnlock>(unlock =>
        {
            if (unlock is not null)
            {
                OpenGame(unlock.Game, unlock.Achievement.Id);
            }
        });
        SortCommand = new RelayCommand<string>(heading =>
        {
            if (!Enum.TryParse<TrophySort>(heading, out var by))
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

            Rows = Order(_all);
        });
        RefreshRarityCommand = new AsyncRelayCommand(async () =>
        {
            Refreshing = true;
            try
            {
                await LoadAsync(AchievementsLoad.Refresh);
            }
            finally
            {
                Refreshing = false;
            }
        });
    }

    /// <summary>
    /// Full glass, as Home and the library (KAN-108, the owner, 3 Oct 2026: "for glossy the trophy room cards need to have
    /// a bit more transparency"); a game's achievements open here show its art, as its page does.
    /// </summary>
    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => Game is { } open && _games.TryGetValue(open.Game, out var game) ? game.HeroPath ?? game.CoverPath : null;

    public string Title => "Achievements";

    public bool ShowsRoom => Game is null;

    public bool NoGames => Read && !HasGames;

    public bool HasRarest => Rarest is not null;

    public bool HasZeniths => Zeniths.Count > 0;

    public bool HasClosest => Closest.Count > 0;

    public bool HasRecent => Recent.Count > 0;

    public bool HasReach => Reach.Count > 0;

    public bool CanRefresh => _actions?.LoadAllAchievements is not null;

    /// <summary>A game's achievements, on this page; from Closest to Zenith, the Zeniths and Every game.</summary>
    public ICommand OpenGameCommand { get; }

    /// <summary>A recent unlock: its game's achievements, with it picked.</summary>
    public ICommand OpenUnlockCommand { get; }

    /// <summary>A heading's click: sorts Every game by it; a second click reverses it.</summary>
    public ICommand SortCommand { get; }

    /// <summary>Asks Steam again how rare each achievement is, for every game.</summary>
    public IAsyncRelayCommand RefreshRarityCommand { get; }

    public bool ByGame => By == TrophySort.Game;

    public bool ByProgress => By == TrophySort.Progress;

    public bool ByUnlocked => By == TrophySort.Unlocked;

    public bool ByLast => By == TrophySort.Last;

    public bool GameIdle => !ByGame;

    public bool ProgressIdle => !ByProgress;

    public bool UnlockedIdle => !ByUnlocked;

    public bool LastIdle => !ByLast;

    /// <summary>The heading's arrow: up for A to Z or the most first, down when reversed.</summary>
    public double Arrow => Reversed ? 0 : 180;

    /// <summary>Every game's achievements as read, for the tests.</summary>
    public IReadOnlyList<GameAchievementsView> Views => _views;

    /// <summary>The games as they are now, for their covers and names; the page shows them again.</summary>
    public void Update(IReadOnlyList<LauncherGame> games)
    {
        _games = games.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        if (Read)
        {
            Show(_views, DateTime.Now);
        }
    }

    /// <summary>Reads what's kept and shows it, then reads again once Steam was asked for what isn't kept yet.</summary>
    public async void Load()
    {
        try
        {
            await LoadAsync(AchievementsLoad.Read);
            await LoadAsync(AchievementsLoad.Fetch);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or HttpRequestException)
        {
            // What's shown stays; asked again next time the page opens.
        }
    }

    public void ReachedFromRail() => Game = null;

    /// <summary>Every game's achievements as read now, with what the page says about them.</summary>
    public void Show(IReadOnlyList<GameAchievementsView> views, DateTime nowLocal)
    {
        _views = views;
        Read = true;
        var games = views.Where(v => v.Total > 0).Select(v =>
        {
            _games.TryGetValue(v.Game, out var game);
            var last = v.LastUnlockUtc;
            return new TrophyGame(v.Game, game?.Title ?? v.Title, ArtImages.LoadCover(game?.CoverPath, 240), GsGameTile.InitialOf(game?.Title ?? v.Title),
                AchievementsProgress.Of(v, nowLocal), last is { } at ? AchievementItem.Day(at, nowLocal) : v.Unlocked > 0 ? "Unlocked" : "Not yet", last, v.CompletedUtc);
        }).ToList();
        _all = games;
        HasGames = games.Count > 0;

        // The average, as Steam reckons it: of the games with at least one unlocked.
        var started = games.Where(g => g.Progress.Done > 0).ToList();
        Average = started.Count == 0 ? 0 : started.Average(g => g.Progress.Percent);
        AverageLabel = Math.Round(Average).ToString(CultureInfo.InvariantCulture) + "%";
        AverageSpoken = $"Average completion of the games you've started: {AverageLabel}";
        var unlocked = games.Sum(g => g.Progress.Done);
        UnlockedText = unlocked.ToString("N0", CultureInfo.InvariantCulture);
        UnlockedSub = $"across {Count(started.Count)}";
        Gold = games.Sum(g => g.Progress.Gold).ToString("N0", CultureInfo.InvariantCulture);
        Silver = games.Sum(g => g.Progress.Silver).ToString("N0", CultureInfo.InvariantCulture);
        Bronze = games.Sum(g => g.Progress.Bronze).ToString("N0", CultureInfo.InvariantCulture);

        Zeniths = games.Where(g => g.IsZenith).OrderByDescending(g => g.CompletedUtc ?? DateTime.MinValue).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        HasZenith = Zeniths.Count > 0;
        ZenithCount = Zeniths.Count.ToString(CultureInfo.InvariantCulture);
        ZenithSub = Zeniths.FirstOrDefault() is { } latest ? $"Latest: {latest.Title}" : "None yet";
        // The monument's line: "Latest:" over the game's name, so a long name isn't broken at its hyphen (KAN-128).
        ZenithSubLines = Zeniths.FirstOrDefault() is { } shown ? $"Latest:\n{shown.Title}" : "None yet";
        ZenithSpoken = Zeniths.FirstOrDefault() is { } newest
            ? $"Zeniths: {ZenithCount}, the latest {newest.Title}"
            : "Zeniths: none yet";
        Closest = started.Where(g => !g.IsZenith).OrderByDescending(g => g.Progress.Percent).ThenBy(g => g.Progress.ToGo).Take(5).ToList();
        ZenithNote = HasZenith ? null
            : Closest.FirstOrDefault() is { } closest ? $"None yet. {closest.Title} is closest: {closest.Progress.ToGo.ToString(CultureInfo.InvariantCulture)} to go."
            : "None yet. Every achievement of a game unlocked earns its Zenith.";

        var unlocks = views.SelectMany(v => v.All.Where(a => a.Unlocked).Select(a => (View: v, Achievement: a))).ToList();
        Recent = unlocks.Where(u => u.Achievement.KnownUnlockUtc is not null)
            .OrderByDescending(u => u.Achievement.KnownUnlockUtc)
            .Take(5)
            .Select(u => new TrophyUnlock(u.View.Game, TitleOf(u.View), AchievementItem.Of(u.Achievement, nowLocal)))
            .ToList();
        // Within reach: what most players have that you don't yet, in the games you've started and not finished. A hidden
        // one stays out: what it is stays secret, and so does how close it is.
        var startedIds = started.Where(g => !g.IsZenith).Select(g => g.Id).ToHashSet();
        Reach = views.Where(v => startedIds.Contains(v.Game))
            .SelectMany(v => v.All.Where(a => !a.Unlocked && !a.Hidden && a.Percent is not null).Select(a => (View: v, Achievement: a)))
            .OrderByDescending(u => u.Achievement.Percent)
            .ThenBy(u => u.Achievement.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(5)
            .Select(u => new TrophyUnlock(u.View.Game, TitleOf(u.View), AchievementItem.Of(u.Achievement, nowLocal)))
            .ToList();

        // None yet while Steam's percentages aren't kept, or before any game has been read.
        Rarest = unlocks.Where(u => u.Achievement.Percent is not null).OrderBy(u => u.Achievement.Percent).Take(1).ToList() is [var rarest]
            ? new TrophyUnlock(rarest.View.Game, TitleOf(rarest.View), AchievementItem.Of(rarest.Achievement, nowLocal))
            : null;
        GamesSubtitle = $"{Count(games.Count)} with achievements on this PC";
        Rows = Order(games);
        OnPropertyChanged(nameof(HasZeniths));
        OnPropertyChanged(nameof(HasClosest));
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>Opens a game's achievements on this page, with one of them picked when given.</summary>
    public void OpenGame(GameId? game, string? pick = null)
    {
        if (game is not { } id || _views.FirstOrDefault(v => v.Game == id) is not { } view)
        {
            return;
        }

        var achievements = new AchievementsViewModel(id, TitleOf(view), _actions, () => Game = null,
            [new Crumb("Achievements", new RelayCommand(() => Game = null), "Achievements: every game's")], here: TitleOf(view))
        {
            // Decoded small and drawn large: soft, as the design blurs it, for nothing.
            HeadArt = _games.TryGetValue(id, out var shown) ? ArtImages.Load(shown.HeroPath ?? shown.CoverPath, 160) : null,
        };
        achievements.Load(view);
        if (pick is not null)
        {
            achievements.Pick(pick);
        }

        Game = achievements;
    }

    private async Task LoadAsync(AchievementsLoad how)
    {
        if (_actions?.LoadAllAchievements is not { } load)
        {
            return;
        }

        var mine = ++_loads;
        var read = await load(how, CancellationToken.None);
        if (mine == _loads)
        {
            Show(read, DateTime.Now);
            if (Game is { } open && read.FirstOrDefault(v => v.Game == open.Game) is { } view)
            {
                open.Show(view, DateTime.Now);
            }
        }
    }

    private string TitleOf(GameAchievementsView view) => _games.TryGetValue(view.Game, out var game) ? game.Title : view.Title;

    private IReadOnlyList<TrophyGame> Order(IReadOnlyList<TrophyGame> games)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        IEnumerable<TrophyGame> ordered = By switch
        {
            TrophySort.Game => games.OrderBy(g => g.Title, byName),
            TrophySort.Unlocked => games.OrderByDescending(g => g.Progress.Done).ThenBy(g => g.Title, byName),
            TrophySort.Last => games.OrderByDescending(g => g.LastUtc ?? DateTime.MinValue).ThenBy(g => g.Title, byName),
            _ => games.OrderByDescending(g => g.Progress.Percent).ThenByDescending(g => g.Progress.Done).ThenBy(g => g.Title, byName),
        };
        return (Reversed ? ordered.Reverse() : ordered).ToList();
    }

    private static string Count(int games) => games == 1 ? "1 game" : $"{games.ToString(CultureInfo.InvariantCulture)} games";
}
