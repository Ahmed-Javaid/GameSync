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
/// <param name="Show">Opens a page by its rail id, on a tab when given: <c>("saves", "needs")</c>.</param>
/// <param name="SetHidden">Hides a game from the launcher on this PC, or shows it again.</param>
public sealed record LauncherActions(Action<GameId> Play, Action SyncNow, Action<string, string?> Show, Action<GameId, bool> SetHidden)
{
    /// <summary>KAN-80: Play, waited for: done once the game is on its way, or the check before playing said why it can't start.</summary>
    public Func<GameId, Task>? Start { get; init; }

    /// <summary>KAN-80: Play was pressed and the game isn't running yet, so its Play says Starting….</summary>
    public Func<GameId, bool>? IsStarting { get; init; }

    /// <summary>KAN-80: GameSync is syncing or uploading now, so Home's Sync now turns its arrows.</summary>
    public Func<bool>? IsWorking { get; init; }

    /// <summary>KAN-80: a game's upload or download, which its saves show as it goes.</summary>
    public Func<GameId, TransferView>? TransferOf { get; init; }

    /// <summary>A cloud is connected, so what's kept here uploads next.</summary>
    public Func<bool>? HasCloud { get; init; }

    /// <summary>The share window (SHARE-01, SHARE-04): picking saves or sharing all, with games ticked, or one version from Export (KAN-23).</summary>
    public Action<ShareStart>? OpenShare { get; init; }

    /// <summary>Import saves (SHARE-10), for a shared zip picked in Windows' own picker.</summary>
    public Action<string>? OpenImport { get; init; }

    /// <summary>Makes a game a favourite on this PC, or an ordinary game again (LIB-17).</summary>
    public Action<GameId, bool>? SetFavourite { get; init; }

    /// <summary>Keeps the library's order on this PC (LIB-16).</summary>
    public Action<LibrarySort>? SetSort { get; init; }

    /// <summary>Keeps the library's Installed only on this PC (KAN-47).</summary>
    public Action<bool>? SetInstalledOnly { get; init; }

    /// <summary>Scan a folder for games…: the folder joins the ones every scan looks in, and this PC is scanned now (LIB-23).</summary>
    public Func<string, CancellationToken, Task<FolderScan>>? ScanFolder { get; init; }

    /// <summary>Locate the game…: the program picked marks a game installed in its folder, and Play starts it (LIB-24).</summary>
    public Action<GameId, string>? Locate { get; init; }

    /// <summary>Add game: Add a game or folder over the library, for one of the person's own (LIB-13).</summary>
    public Action? OpenAddGame { get; init; }

    /// <summary>SYNC-14: what the next sync would do for every game, and why, off the UI thread (the save manager's Plan).</summary>
    public Func<CancellationToken, Task<SyncPlanView>>? CheckPlan { get; init; }

    /// <summary>Runs the plan's ticked changes, exactly as they were planned.</summary>
    public Func<IReadOnlyList<PlannedChange>, CancellationToken, Task<PlanRun>>? RunPlan { get; init; }

    /// <summary>The space the backups take on this PC's drive, and the drive's free space, off the UI thread (MGR-03).</summary>
    public Func<CancellationToken, Task<BackupSpace?>>? LoadSpace { get; init; }

    /// <summary>Connect the cloud, after first run skipped it: its dialog over the page.</summary>
    public Action? ConnectCloud { get; init; }

    /// <summary>Opens a game's page in the library, as a click on its cover anywhere does (LIB-18).</summary>
    public Action<GameId>? OpenGame { get; init; }

    /// <summary>
    /// A game's achievements, every one (design system → AchievementsScreen), beside the library's list: from its page's
    /// View all, or Home's card with the page Back returns to (<c>home</c>); null returns to the game's page.
    /// </summary>
    public Action<GameId, string?>? OpenAchievements { get; init; }

    /// <summary>KAN-110: leaves a game out of the achievements on this PC, or counts it again; the pages read again after.</summary>
    public Action<GameId, bool>? SetAchievementsLeftOut { get; init; }

    /// <summary>Version 49: a game's 100% seen, its moment played; remembered on this PC.</summary>
    public Action<GameId>? SeeZenith { get; init; }

    /// <summary>PKG-03 (design system version 53): Restart to update, from Home's top bar.</summary>
    public Action? RestartToUpdate { get; init; }

    /// <summary>
    /// ACH-01 to ACH-03: a game's achievements read off the UI thread, after asking Steam, when told to, for each icon the
    /// list of every one shows (never a hidden one's) and rarity a week old; null for a game with none GameSync can read.
    /// </summary>
    public Func<GameId, bool, CancellationToken, Task<GameAchievementsView?>>? LoadAchievements { get; init; }

    /// <summary>The Achievements page: every game's achievements here, read off the UI thread as it says (design system → TrophyRoomScreen).</summary>
    public Func<AchievementsLoad, CancellationToken, Task<IReadOnlyList<GameAchievementsView>>>? LoadAllAchievements { get; init; }

    /// <summary>Reads a game's page: its places, named saves, versions and log, off the UI thread.</summary>
    public Func<GameId, CancellationToken, Task<GameDetail?>>? LoadGame { get; init; }

    /// <summary>Opens a folder in Explorer: where a game keeps its saves.</summary>
    public Action<string>? OpenFolder { get; init; }

    /// <summary>Sync these saves: confirms what the scan found for a game and syncs it, done once its saves are kept here (FIND-06).</summary>
    public Func<GameId, Task>? SyncGame { get; init; }

    /// <summary>Backs a game up now (BAK-16), done once the save is kept here; its upload follows beside whatever comes next.</summary>
    public Func<GameId, Task>? BackUpNow { get; init; }

    /// <summary>New named save…, over the page (BAK-18, KAN-77).</summary>
    public Action<NamedSaveStart>? OpenNamedSave { get; init; }

    /// <summary>KAN-61: a game's live save beside copies kept by hand, over the page: only the live save, the copies as named saves.</summary>
    public Action<KeptCopiesStart>? OpenKeptCopies { get; init; }

    /// <summary>KAN-61: the live save and each copy beside it, read off the UI thread without changing anything, saying how far it is (KAN-80).</summary>
    public Func<GameId, IProgress<WorkProgress>?, CancellationToken, Task<KeptLook>>? LookKept { get; init; }

    /// <summary>
    /// KAN-61: keeps only the live save, backed up only when told (KAN-63), and the copies as named saves when told,
    /// saying how far it is (KAN-80); what goes wrong is thrown, for the dialog to say.
    /// </summary>
    public Func<GameId, bool, bool, IProgress<WorkProgress>?, CancellationToken, Task<KeptDone>>? ApplyKept { get; init; }

    /// <summary>
    /// Keeps the game's save as it is now under a name (BAK-18); a game not syncing yet is kept first (KAN-63). Null once
    /// it's kept, or why it couldn't be.
    /// </summary>
    public Func<GameId, string, Task<string?>>? KeepNamed { get; init; }

    /// <summary>KAN-63: starts keeping a game not syncing yet (backed up only, not synced between PCs); true once it's kept.</summary>
    public Func<GameId, Task<bool>>? Keep { get; init; }

    /// <summary>
    /// Brings a version back, a named save's (with its name) or any other; this PC's files are kept first. Null once it's
    /// in place, or why it couldn't be (KAN-51).
    /// </summary>
    public Func<GameId, VersionId, string?, Task<string?>>? Restore { get; init; }

    /// <summary>A game's saves in the save manager: its named saves, where they are, every version and its log (MGR-07).</summary>
    public Action<GameId>? OpenSaves { get; init; }

    /// <summary>
    /// KAN-87: a named save or any version as a plain folder in the folder picked, named after it: the game, the version,
    /// its name (null: its date), the folder picked. The folder made, or why it couldn't be.
    /// </summary>
    public Func<GameId, VersionId, string?, string, Task<(string? Folder, string? Problem)>>? ExportFolder { get; init; }

    /// <summary>KAN-82: keeps how named saves are listed on this PC: <c>newest</c> or <c>name</c>.</summary>
    public Action<string>? SetNamedSort { get; init; }

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

    /// <summary>FIND-04: what learn mode found for a game, over its saves (LearnModeDialog).</summary>
    public Action<GameId>? OpenLearnFinds { get; init; }

    /// <summary>FIND-04: learn mode on for the game's next session, or off (forgetting what it found).</summary>
    public Action<GameId, bool>? SetLearn { get; init; }

    /// <summary>FIND-04: the places picked from learn mode's finds are added and the game syncs; why not, when it can't.</summary>
    public Func<GameId, IReadOnlyList<string>, Task<string?>>? AddLearned { get; init; }

    /// <summary>Import kept saves, over the page (BAK-19).</summary>
    public Action<GameId>? OpenImportKept { get; init; }

    /// <summary>A game's places, for which one kept copies copy.</summary>
    public Func<GameId, CancellationToken, Task<IReadOnlyList<SaveRoot>>>? SaveRoots { get; init; }

    /// <summary>Reads a folder of kept saves (false) or imports them as named saves (true): the game, the folder, the place they copy.</summary>
    public Func<GameId, string, string?, bool, CancellationToken, Task<ImportReport>>? KeptSaves { get; init; }

    /// <summary>Every game's saves at a glance, for the save manager's table, off the UI thread.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<GameSaveSummary>>>? LoadSaves { get; init; }

    /// <summary>MGR-08: every version of every game from every PC, for the save manager's Versions tab.</summary>
    public Func<CancellationToken, Task<VersionsView>>? LoadVersions { get; init; }

    /// <summary>MGR-09: everything in the activity log, newest first, for the save manager's Log tab.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<LogEntry>>>? LoadLog { get; init; }

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

    /// <summary>The words of the small mark shown for a game whose saves are fine (KAN-48); null shows the badge instead.</summary>
    public string? MarkLabel { get; init; }

    /// <summary>The small cover for the library's list (Steam's 300×450 capsule, decoded small).</summary>
    public IImage? SmallArt { get; init; }

    /// <summary>ACH-02: how many of its achievements are unlocked, as Steam on this PC keeps them; 0 of 0 for a game with none it counts.</summary>
    public int AchievementsDone { get; init; }

    public int AchievementsTotal { get; init; }

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

    /// <summary>Its Properties (LIB-20), from the right-click menu too (KAN-56); null where the page can't.</summary>
    public ICommand? PropertiesCommand { get; init; }

    public bool HasProperties => PropertiesCommand is not null;

    public string HideLabel => IsHidden ? "Show in the launcher" : "Hide from the launcher";

    public string FavouriteLabel => IsFavourite ? "Remove from favourites" : "Add to favourites";

    /// <summary>Anything to offer on a right-click.</summary>
    public bool HasMenu => ToggleHidden is not null || ToggleFavourite is not null || PropertiesCommand is not null;

    /// <summary>What a screen reader says for the game's row or tile: its name, what needs doing, and whether it's here (A11Y-03).</summary>
    public string SpokenName => string.Join(", ", new[]
    {
        Title,
        MarkLabel ?? (Status is { } status && status != GameStatus.Synced ? StatusLabel ?? GsStatusBadge.Describe(status).Word : null),
        Installed ? null : "not installed on this PC",
    }.OfType<string>());

    public override string ToString() => SpokenName;
}

/// <summary>
/// Home's Achievements card (the owner, 3 Oct 2026; design system version 33, the simpler one again since 46): the hero
/// game's overview (its ring, tiers and Zenith) and the last two unlocked, or the last game played that has some, saying so;
/// its arrow opens that game's achievements, every one.
/// </summary>
public sealed record HomeAchievements(GameId Game, string Title, string Subtitle, AchievementsProgress Progress, IReadOnlyList<AchievementItem> Latest, string OpenLabel)
{
    /// <summary>Version 49: this game's 100% hasn't been seen on this PC yet, so its moment plays here.</summary>
    public bool CelebratesZenith { get; init; }

    public bool HasLatest => Latest.Count > 0;

    public bool NoneUnlocked => Latest.Count == 0;

    /// <param name="heroHasNone">The card is about another game than the hero, which keeps none.</param>
    /// <param name="heroLeftOut">The hero has some, but is left out of the achievements in Settings (KAN-110).</param>
    /// <param name="zenithSeen">The game's Zenith has been seen on this PC, so its moment doesn't play.</param>
    public static HomeAchievements Of(GameAchievementsView view, LauncherGame? hero, bool heroHasNone, IReadOnlyList<LauncherGame> games, DateTime nowLocal,
        bool heroLeftOut = false, bool zenithSeen = true)
    {
        var game = games.FirstOrDefault(g => g.Id == view.Game);
        var title = game?.Title ?? view.Title;
        // The hero has none to show: said plainly (the owner, 3 Oct 2026, found "Bloodborne GOTY's copy keeps none" odd).
        // A copy Steam knows by its app ID but doesn't run here (the owner's Sons of the Forest, in its own folder): Steam keeps
        // no record of what's unlocked in it, which is why, rather than GameSync not finding them.
        var why = heroLeftOut ? "is left out of your achievements"
            : hero?.Store == StoreKind.Steam ? "has none on Steam"
            : hero?.SteamAppId is not null ? "isn't run by Steam here, so Steam keeps none of its achievements"
            : "has none GameSync can read";
        var subtitle = heroHasNone && hero is not null
            ? $"{title}, played {Played(game?.LastPlayedUtc, nowLocal)}. {hero.Title} {why}."
            : $"{title} · {view.Unlocked.ToString(CultureInfo.InvariantCulture)} of {view.Total.ToString(CultureInfo.InvariantCulture)}{(view.From == AchievementsFrom.CopyRecord ? " in its own record" : " on Steam")}";
        return new HomeAchievements(
            view.Game,
            title,
            subtitle,
            AchievementsProgress.Of(view, nowLocal),
            view.Latest(2).Select(a => AchievementItem.Of(a, nowLocal)).ToList(),
            $"All of {title}'s achievements")
        {
            CelebratesZenith = view.IsComplete && !zenithSeen,
        };
    }

    private static string Played(DateTime? utc, DateTime nowLocal) => utc is null ? "before" : AchievementItem.Day(utc.Value, nowLocal) switch
    {
        "Today" => "today",
        "Yesterday" => "yesterday",
        var day => $"on {day}",
    };
}

/// <summary>
/// One game Home's banner can show (KAN-125): its art, logo and words, its status, its Play, the art Glossy follows while
/// it's shown, and its Achievements card.
/// </summary>
public sealed record HeroSlide(GameId Id, string Title, IImage? Art, IImage? Logo, string? Eyebrow, string? Chip, GameStatus? Status, string? StatusLabel,
    string? Blurb, string PlayLabel, bool ShowsPlay, string? BackdropArt, HomeAchievements? Achievements)
{
    public static HeroSlide Of(LauncherGame game, DateTime nowLocal, LauncherActions? actions, HomeAchievements? achievements) => new(
        game.Id,
        game.Title,
        ArtImages.Load(game.HeroPath, 1920),
        ArtImages.Load(game.LogoPath, 760),
        HomeViewModel.HeroPhrase(game, nowLocal),
        game.Playtime <= TimeSpan.Zero ? null : HomeViewModel.HoursPlayed(game.Playtime),
        // PLAY-12: the game playing now reads Playing now, whether its saves sync or not.
        game.IsRunning ? GameStatus.Playing : game.Syncs ? game.Status ?? GameStatus.Synced : null,
        game.IsRunning ? "Playing now" : game is { Syncs: true, Status: null or GameStatus.Synced } ? "Save synced" : HomeViewModel.StatusLabel(game),
        HomeViewModel.Blurb(game),
        game.LastPlayedUtc is null ? "Play" : "Continue playing",
        !game.IsRunning,
        game.HeroPath ?? game.CoverPath,
        achievements);
}

/// <summary>A dot of Home's pager (KAN-125): which game it is, whether it's the one shown, and the button that shows it.</summary>
public sealed partial class HeroDot(int index, string title, int count, bool isCurrent, ICommand show) : ObservableObject
{
    public int Index { get; } = index;

    public ICommand Show { get; } = show;

    /// <summary>"Valheim, 2 of 5", for screen readers and the tooltip.</summary>
    public string Name { get; } = $"{title}, {(index + 1).ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}";

    [ObservableProperty]
    private bool _isCurrent = isCurrent;

    public override string ToString() => Name;
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

    /// <summary>First run skipped the cloud: the top bar offers Connect the cloud in place of the cloud button.</summary>
    public bool NoCloud { get; init; }

    public bool HasCloud => !NoCloud;
}

/// <summary>A month of Home's Activity (KAN-66): its name, each day's level and games, and what each day says when pointed at.</summary>
public sealed record ActivityMonthItem(string Name, IReadOnlyList<int> Levels, int StartWeekday, IReadOnlyList<ActivityDay> Days)
{
    public IReadOnlyList<string> Tips { get; } = Days.Select(d => d.Tip).ToList();
}

/// <summary>
/// A day in Home's Activity (KAN-66): the games played that day and for how long, longest first, for the tooltip
/// pointing at it shows and the list a click on it opens.
/// </summary>
public sealed record ActivityDay(DateTime Date, TimeSpan Played, IReadOnlyList<ActivityGame> Games, string Tip)
{
    /// <summary>"Tuesday 29 September".</summary>
    public string Label => Date.ToString("dddd d MMMM", CultureInfo.InvariantCulture);

    /// <summary>"3 h 10 min played".</summary>
    public string Total => $"{Launcher.DurationText(Played)} played";

    /// <summary>
    /// A day from what was played on it: pointing at it says the day and its play, then each game and how long (five at
    /// most, then how many more); a day without play says so, and a day still to come only its date.
    /// </summary>
    public static ActivityDay Of(DateTime date, IReadOnlyList<DayPlay> played, DateTime nowLocal, IReadOnlyDictionary<GameId, string?> covers, Action<GameId> open)
    {
        var total = played.Aggregate(TimeSpan.Zero, (sum, game) => sum + game.Played);
        var games = played.Select(g => new ActivityGame(g.Id, g.Title, Launcher.DurationText(g.Played), covers.GetValueOrDefault(g.Id), open)).ToList();
        var label = date.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        var tip = date.Date > nowLocal.Date ? label
            : games.Count == 0 ? $"{label} · nothing played"
            : string.Join('\n', games.Take(5).Select(g => $"{g.Title} · {g.Played}")
                .Prepend($"{label} · {Launcher.DurationText(total)}")
                .Concat(games.Count > 5 ? [$"and {games.Count - 5} more"] : []));
        return new ActivityDay(date, total, games, tip);
    }
}

/// <summary>A game in a day's list: its cover, its title and how long it was played; a click opens its page.</summary>
public sealed class ActivityGame(GameId id, string title, string played, string? coverPath, Action<GameId> open)
{
    private IImage? _art;
    private bool _loaded;

    public GameId Id => id;

    public string Title => title;

    /// <summary>"2 h 40 min".</summary>
    public string Played => played;

    public string Initial => GsGameTile.InitialOf(title);

    /// <summary>The cover, read only once the list shows.</summary>
    public IImage? Art
    {
        get
        {
            if (!_loaded)
            {
                _art = ArtImages.Load(coverPath, 96);
                _loaded = true;
            }

            return _art;
        }
    }

    /// <summary>"Terraria, 30 min played", for screen readers.</summary>
    public string Spoken => $"{title}, {played} played";

    public ICommand Open { get; } = new RelayCommand(() => open(id));
}

/// <summary>
/// The launcher home (PLAY-01): the hero, Needs you, Jump back in and this month's activity. Its pill tabs: Recently played
/// is this page, My games opens the library, and Needs you the save manager's Needs you (KAN-46).
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IPageSurface
{
    public const string ThisPage = "recent";

    /// <summary>Home's Needs you tab, which opens the save manager's Needs you (KAN-46).</summary>
    public const string NeedsYouTab = "needs";

    /// <summary>Full glass, as a game's page: Home's cards take the art's colour (the owner, 1 Oct 2026; LOOK-17).</summary>
    public GlassStrength Strength => GlassStrength.Glass;

    [ObservableProperty]
    private string _selectedTab = ThisPage;

    /// <summary>KAN-80: the hero's Play was pressed and the game isn't running yet: Play says Starting….</summary>
    [ObservableProperty]
    private bool _heroStarting;

    /// <summary>KAN-80: GameSync is syncing or uploading: Sync now turns its arrows.</summary>
    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>
    /// PKG-03 (design system version 53 → LauncherScreen #update): a newer GameSync is downloaded and checked, so the top
    /// bar offers Restart to update; this is its tooltip, saying which and what happens, or why it waits. Null otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsUpdate))]
    private string? _updateReady;

    /// <summary>Restart to update waits while a game is being played.</summary>
    [ObservableProperty]
    private bool _canUpdate = true;

    public bool ShowsUpdate => UpdateReady is not null;

    public IRelayCommand RestartToUpdateCommand => _restartToUpdate ??= new RelayCommand(() => Actions?.RestartToUpdate?.Invoke());

    private IRelayCommand? _restartToUpdate;

    public HomeViewModel()
    {
        PlayCommand = new AsyncRelayCommand(async () =>
        {
            if (HeroId is not { } game)
            {
                return;
            }

            // KAN-80: Play says Starting… from the press until the game runs, or the check says why it can't.
            HeroStarting = true;
            if (Actions?.Start is { } start)
            {
                await start(game);
            }
            else
            {
                Actions?.Play(game);
            }

            HeroStarting = Actions?.IsStarting?.Invoke(game) == true;
        });
        SyncNowCommand = new RelayCommand(() => Actions?.SyncNow());
        OpenLibraryCommand = new RelayCommand(() => Actions?.Show("library", "all"));
        OpenSavesCommand = new RelayCommand(() => Actions?.Show("saves", SaveManagerViewModel.NeedsTab));
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
        ConnectCloudCommand = new RelayCommand(() => Actions?.ConnectCloud?.Invoke());
        OpenSettingsCommand = new RelayCommand<string>(section => Actions?.Show("settings", section));
        ChooseDayCommand = new RelayCommand<int>(day => ChosenDay = day >= 0 && day < ActivityDays.Count ? ActivityDays[day] : null);
        EarlierMonthCommand = new RelayCommand(() => MonthIndex--, () => MonthIndex > 0);
        LaterMonthCommand = new RelayCommand(() => MonthIndex++, () => MonthIndex < Months.Count - 1);
    }

    /// <summary>The top bar's Connect the cloud, while no cloud is connected.</summary>
    public ICommand ConnectCloudCommand { get; }

    /// <summary>A section of Settings, from the top bar's cloud and PC buttons: "cloud" or "devices".</summary>
    public ICommand OpenSettingsCommand { get; }

    /// <summary>No cloud is connected yet: the top bar offers Connect the cloud.</summary>
    public bool NoCloud => Status?.NoCloud == true;

    /// <summary>The cloud button: where the saves go, once a cloud is connected.</summary>
    public bool ShowsCloud => HasStatus && !NoCloud;

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

    /// <summary>
    /// KAN-125 (the owner: "I want the main screens hero card to be scrollable ... Recently played games?"): the games the
    /// banner can show, the one playing now or played last first, then the next ones played, five at most.
    /// </summary>
    public IReadOnlyList<HeroSlide> HeroSlides { get; init; } = [];

    /// <summary>How many games the banner can show.</summary>
    public const int HeroSlideCount = 5;

    /// <summary>Which of <see cref="HeroSlides"/> the banner shows.</summary>
    [ObservableProperty]
    private int _heroIndex;

    /// <summary>The game the banner shows.</summary>
    public HeroSlide? Hero => HeroIndex >= 0 && HeroIndex < HeroSlides.Count ? HeroSlides[HeroIndex] : null;

    public GameId? HeroId => Hero?.Id;

    /// <summary>There's a game to put in the banner: the last one played, or else one that's installed.</summary>
    public bool HasHero => HeroId is not null;

    /// <summary>More than one game to show: the pager, the wheel and the arrow keys move between them.</summary>
    public bool HasHeroPages => HeroSlides.Count > 1;

    /// <summary>The pager's dots, one a game.</summary>
    public IReadOnlyList<HeroDot> HeroDots => _heroDots ??= HeroSlides.Select((slide, i) => new HeroDot(i, slide.Title, HeroSlides.Count, i == HeroIndex,
        new RelayCommand(() => HeroIndex = i))).ToList();

    private List<HeroDot>? _heroDots;

    /// <summary>The next game the banner can show, round to the first after the last.</summary>
    public ICommand NextHeroCommand => new RelayCommand(() => HeroIndex = (HeroIndex + 1) % HeroSlides.Count, () => HeroSlides.Count > 1);

    /// <summary>The game before, round to the last before the first.</summary>
    public ICommand PreviousHeroCommand => new RelayCommand(() => HeroIndex = (HeroIndex + HeroSlides.Count - 1) % HeroSlides.Count, () => HeroSlides.Count > 1);

    /// <summary>What a screen reader says of the banner: the game, where it is among them, and what Enter does.</summary>
    public string? HeroSpoken => Hero is { } hero
        ? HasHeroPages
            ? $"{hero.Title}, {(HeroIndex + 1).ToString(CultureInfo.InvariantCulture)} of {HeroSlides.Count.ToString(CultureInfo.InvariantCulture)}. Enter opens its page; Left and Right show the other games you played last."
            : $"{hero.Title}. Enter opens its page."
        : null;

    /// <summary>Glossy's art while the banner shows this game (its wide art, else its cover), so the page's colours follow it.</summary>
    public string? HeroBackdropArt => Hero?.BackdropArt;

    /// <summary>Raised when the banner shows another game, for the window's colours to follow it.</summary>
    public event Action? HeroShown;

    partial void OnHeroIndexChanged(int value)
    {
        foreach (var dot in HeroDots)
        {
            dot.IsCurrent = dot.Index == value;
        }

        HeroStarting = Hero is { ShowsPlay: true } hero && Actions?.IsStarting?.Invoke(hero.Id) == true;
        foreach (var name in (string[])[nameof(Hero), nameof(HeroId), nameof(HasHero), nameof(HeroShowsPlay), nameof(HeroIsPlaying), nameof(HeroArt), nameof(HeroLogo),
                     nameof(HeroTitle), nameof(HeroEyebrow), nameof(HeroChip), nameof(HeroStatus), nameof(HeroStatusLabel), nameof(HeroBlurb), nameof(HeroPlayLabel),
                     nameof(HeroSpoken), nameof(HeroBackdropArt), nameof(Achievements), nameof(HasAchievements), nameof(NoAchievements), nameof(CelebratesZenith)])
        {
            OnPropertyChanged(name);
        }

        HeroShown?.Invoke();
    }

    /// <summary>
    /// After the pages are read again: keeps showing the game <paramref name="before"/> showed, so a refresh doesn't take the
    /// banner back to the first; unless another game is first now (one that started playing), which is then shown.
    /// </summary>
    public void KeepShowing(HomeViewModel? before)
    {
        if (before?.HeroId is { } shown && before.HeroSlides.FirstOrDefault()?.Id == HeroSlides.FirstOrDefault()?.Id &&
            HeroSlides.ToList().FindIndex(s => s.Id == shown) is var index and > 0)
        {
            HeroIndex = index;
        }
    }

    /// <summary>GameSync hasn't found any games on this PC yet (it hasn't scanned): Home says so instead of an empty banner.</summary>
    public bool NoGames { get; init; }

    /// <summary>There are games to choose from, so the Needs you card can offer to choose the ones to sync.</summary>
    public bool CanChoose => NoneSyncing && !NoGames;

    /// <summary>The hero's Play, except while the hero game runs (PLAY-12).</summary>
    public bool HeroShowsPlay => Hero?.ShowsPlay ?? true;

    /// <summary>The hero game is running now: the banner glows in the play colour (KAN-50).</summary>
    public bool HeroIsPlaying => HeroId is not null && !HeroShowsPlay;

    public IImage? HeroArt => Hero?.Art;

    public IImage? HeroLogo => Hero?.Logo;

    public string? HeroTitle => Hero?.Title;

    public string? HeroEyebrow => Hero?.Eyebrow;

    public string? HeroChip => Hero?.Chip;

    public GameStatus? HeroStatus => Hero?.Status;

    public string? HeroStatusLabel => Hero?.StatusLabel;

    public string? HeroBlurb => Hero?.Blurb;

    public string HeroPlayLabel => Hero?.PlayLabel ?? "Continue playing";

    public IReadOnlyList<NeedsYouItem> NeedsYou { get; init; } = [];

    /// <summary>
    /// Home's Achievements card (design system version 33): the shown game's, following the banner (KAN-125); null when no
    /// game here has any GameSync can read.
    /// </summary>
    public HomeAchievements? Achievements => Hero?.Achievements;

    public bool HasAchievements => Achievements is not null;

    public bool NoAchievements => Achievements is null;

    private readonly HashSet<GameId> _zenithsSeenHere = [];

    /// <summary>Version 49: the shown game's 100% is seen for the first time, so the card's moment plays; once.</summary>
    public bool CelebratesZenith => Achievements is { CelebratesZenith: true } card && !_zenithsSeenHere.Contains(card.Game);

    /// <summary>The card's moment played: remembered on this PC, so it doesn't again.</summary>
    public ICommand SeeZenithCommand => new RelayCommand(() =>
    {
        if (Achievements is not { } card || !_zenithsSeenHere.Add(card.Game))
        {
            return;
        }

        OnPropertyChanged(nameof(CelebratesZenith));
        Actions?.SeeZenith?.Invoke(card.Game);
    });

    /// <summary>The Achievements card's arrow: every one of the game's achievements, with Back to Home.</summary>
    public ICommand OpenAchievementsCommand => new RelayCommand(() =>
    {
        if (Achievements is { } card)
        {
            if (Actions?.OpenAchievements is { } open)
            {
                open(card.Game, "home");
            }
            else
            {
                Actions?.OpenGame?.Invoke(card.Game);
            }
        }
    });

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

    /// <summary>KAN-66: Activity month by month, this month last; the card shows one, and its arrows go back and forth.</summary>
    public IReadOnlyList<ActivityMonthItem> Months { get; init; } = [];

    /// <summary>The month the Activity card shows, from 0 for the earliest.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityTitle), nameof(MonthName), nameof(Days), nameof(StartWeekday), nameof(ActivityDays), nameof(DayTips))]
    [NotifyCanExecuteChangedFor(nameof(EarlierMonthCommand), nameof(LaterMonthCommand))]
    private int _monthIndex;

    private ActivityMonthItem? ShownMonth => MonthIndex >= 0 && MonthIndex < Months.Count ? Months[MonthIndex] : null;

    /// <summary>"Activity in September": the card's name for screen readers; the card shows the month between its arrows.</summary>
    public string ActivityTitle => ShownMonth is { } month ? $"Activity in {month.Name}" : "Activity";

    /// <summary>"September", or "December 2025" in another year.</summary>
    public string MonthName => ShownMonth?.Name ?? "";

    public IReadOnlyList<int> Days => ShownMonth?.Levels ?? [];

    public int StartWeekday => ShownMonth?.StartWeekday ?? 0;

    /// <summary>Each day of the month shown, with the games played that day and for how long.</summary>
    public IReadOnlyList<ActivityDay> ActivityDays => ShownMonth?.Days ?? [];

    /// <summary>What each day of the month shown says when pointed at.</summary>
    public IReadOnlyList<string> DayTips => ShownMonth?.Tips ?? [];

    /// <summary>The month before, back to the first with play (a year at most).</summary>
    public IRelayCommand EarlierMonthCommand { get; }

    /// <summary>The month after, up to this one.</summary>
    public IRelayCommand LaterMonthCommand { get; }

    /// <summary>The day whose games the Activity card's flyout lists.</summary>
    [ObservableProperty]
    private ActivityDay? _chosenDay;

    /// <summary>A click on a day with play, or Enter on it: its games, in the flyout.</summary>
    public ICommand ChooseDayCommand { get; }

    /// <param name="actions">What the page asks of the app; null where it only shows, as in the snapshot tool.</param>
    /// <param name="status">The cloud and the PCs, for the top bar.</param>
    /// <param name="achievements">
    /// Home's Achievements card: the game's, whether it's another game than the hero, which keeps none, and whether the hero
    /// is left out of the achievements instead (KAN-110).
    /// </param>
    /// <param name="achievementsOf">The same for each other game the banner can show (KAN-125); without it they show the hero's.</param>
    /// <param name="progress">ACH-02: how far each game's achievements are, for the ring on Jump back in's covers.</param>
    public static HomeViewModel From(LauncherHome home, IReadOnlyList<LauncherGame> all, DateTime nowLocal, LauncherActions? actions = null, HomeStatus? status = null,
        (GameAchievementsView? View, bool HeroHasNone, bool HeroLeftOut) achievements = default,
        Func<LauncherGame, (GameAchievementsView? View, bool HeroHasNone, bool HeroLeftOut)>? achievementsOf = null,
        IReadOnlyDictionary<GameId, (int Done, int Total)>? progress = null, IReadOnlySet<GameId>? zenithsSeen = null)
    {
        var games = all.Where(g => g.Shown).ToList();
        var hero = home.Hero;
        var needsYouCount = games.Count(g => g.NeedsYou);

        // KAN-125: the hero first, then the games played last, each with its own Achievements card.
        var shown = hero is null ? [] : games.Where(g => g.Id != hero.Id && g.LastPlayedUtc is not null && !g.IsFolder)
            .OrderByDescending(g => g.LastPlayedUtc).Take(HeroSlideCount - 1).Prepend(hero).ToList();
        HomeAchievements? CardOf(LauncherGame game, (GameAchievementsView? View, bool HeroHasNone, bool HeroLeftOut) card) =>
            card.View is { } view ? HomeAchievements.Of(view, game, card.HeroHasNone, games, nowLocal, card.HeroLeftOut, zenithsSeen?.Contains(view.Game) ?? true) : null;
        var slides = shown.Select((game, i) => HeroSlide.Of(game, nowLocal, actions,
            CardOf(game, i == 0 ? achievements : achievementsOf?.Invoke(game) ?? achievements))).ToList();
        var covers = all.ToDictionary(g => g.Id, g => g.CoverPath);
        var months = home.Months.Select(month => new ActivityMonthItem(
            month.Name,
            month.Levels,
            month.StartWeekday,
            month.Play.Select((played, i) => ActivityDay.Of(month.First.AddDays(i), played, nowLocal, covers, id => actions?.OpenGame?.Invoke(id))).ToList())).ToList();
        return new HomeViewModel
        {
            Actions = actions,
            Status = status,
            Tabs =
            [
                new NavItem(ThisPage, "Recently played"),
                new NavItem("all", "My games"),
                // Something needs the person: the pill in warn, with a caution mark and the count (design system version 32).
                new NavItem(NeedsYouTab, "Conflicts", Count: needsYouCount > 0 ? needsYouCount.ToString(CultureInfo.InvariantCulture) : null,
                    Tone: needsYouCount > 0 ? "warn" : null),
            ],
            HeroSlides = slides,
            NoGames = games.Count == 0,
            HeroStarting = hero is { IsRunning: false } && actions?.IsStarting?.Invoke(hero.Id) == true,
            IsSyncing = actions?.IsWorking?.Invoke() == true,
            NeedsYou = home.NeedsYou.Select(g => new NeedsYouItem(g.Id, g.Title, ArtImages.Load(g.CoverPath, 96), GsGameTile.InitialOf(g.Title), g.Status, ActionFor(g.Status))).ToList(),
            AnySyncing = home.Syncing > 0,
            HasActivity = home.Months.Any(m => m.Levels.Any(d => d > 0)),
            SyncedLabel = home.Syncing == 0 ? $"{games.Count} games found, none syncing yet" : $"{home.Synced} of {home.Syncing} games synced",
            SyncedRight = home.Syncing == 0 ? "" : $"{Math.Round(100.0 * home.Synced / home.Syncing).ToString(CultureInfo.InvariantCulture)}%",
            SyncedPercent = home.Syncing == 0 ? 0 : 100.0 * home.Synced / home.Syncing,
            JumpBackIn = home.JumpBackIn.Select(g => Tile(g, nowLocal, width: 300, withMeta: false, actions,
                achievements: progress is not null && progress.TryGetValue(g.Id, out var done) ? done : null)).ToList(),
            Months = months,
            MonthIndex = months.Count - 1,
        };
    }

    /// <summary>My games opens the library, and Needs you the save manager's Needs you (KAN-46); Recently played stays chosen here for when you come back.</summary>
    partial void OnSelectedTabChanged(string value)
    {
        if (value == ThisPage || Actions is null)
        {
            return;
        }

        // KAN-46: Needs you is the save manager's tab, where the save work is; My games is the library.
        if (value == NeedsYouTab)
        {
            Actions.Show("saves", SaveManagerViewModel.NeedsTab);
        }
        else
        {
            Actions.Show("library", value);
        }

        SelectedTab = ThisPage;
    }

    /// <param name="smallWidth">Also decode the cover this wide for the library's list; 0 leaves it out.</param>
    /// <param name="achievements">ACH-02: how many of its achievements are unlocked, and of how many, when they count (design system version 39).</param>
    public static TileItem Tile(LauncherGame game, DateTime nowLocal, int width = 320, bool withMeta = true, LauncherActions? actions = null, int smallWidth = 0,
        (int Done, int Total)? achievements = null) =>
        new(game.Id, game.Title, ArtImages.LoadCover(game.CoverPath, width), game.IsRunning ? GameStatus.Playing : game.Status, withMeta ? Launcher.Meta(game, nowLocal) : null)
        {
            AchievementsDone = achievements?.Done ?? 0,
            AchievementsTotal = achievements?.Total ?? 0,
            StatusLabel = StatusLabel(game),
            MarkLabel = game.IsRunning ? null : MarkLabel(game.Status, game.Store, game.StoreSyncs),
            SmallArt = smallWidth > 0 ? ArtImages.LoadCover(game.CoverPath, smallWidth) : null,
            Installed = game.Installed,
            IsHidden = game.IsHidden,
            IsFavourite = game.IsFavourite,
            PlayCommand = actions is null || !game.Installed ? null : new RelayCommand(() => actions.Play(game.Id)),
            ToggleHidden = actions is null ? null : new RelayCommand(() => actions.SetHidden(game.Id, !game.IsHidden)),
            ToggleFavourite = actions?.SetFavourite is { } setFavourite ? new RelayCommand(() => setFavourite(game.Id, !game.IsFavourite)) : null,
            PropertiesCommand = actions?.OpenProperties is { } properties ? new RelayCommand(() => properties(game.Id, null)) : null,
        };

    /// <summary>LIB-10: a game its store's cloud syncs names the store ("Synced by Steam"); other statuses say their own word.</summary>
    public static string? StatusLabel(LauncherGame? game) => game switch
    {
        { Status: GameStatus.BackupOnly, StoreSyncs: false } => "Backed up",
        { Status: GameStatus.BackupOnly } => StoreNames.SyncedBy(game.Store),
        { FirstBackupPending: true } => "Not backed up yet",
        _ => null,
    };

    /// <summary>
    /// KAN-48: a game whose saves are fine gets a small mark in the ok colour in place of a badge, with these words in its
    /// tooltip and for screen readers; null for any other status, so the badge shows.
    /// </summary>
    /// <param name="storeSyncs">Its store's cloud syncs it; otherwise it's backed up only by the person's choice (KAN-63).</param>
    public static string? MarkLabel(GameStatus? status, StoreKind? store, bool storeSyncs = true) => status switch
    {
        GameStatus.Synced => "Synced between your PCs",
        GameStatus.BackupOnly when !storeSyncs => "Backed up; not synced between your PCs",
        GameStatus.BackupOnly => StoreNames.BackedUpSyncedBy(store),
        _ => null,
    };

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

    internal static string HoursPlayed(TimeSpan playtime) =>
        playtime.TotalHours >= 1 ? Math.Round(playtime.TotalHours) is var hours && hours == 1 ? "1 hr played" : $"{hours.ToString(CultureInfo.InvariantCulture)} hrs played"
        : $"{Math.Max(1, (int)Math.Round(playtime.TotalMinutes))} min played";

    internal static string Blurb(LauncherGame game) => game switch
    {
        // FIND-04 (design system version 43, LauncherScreen #learning): a game nothing was found for, watched by learn mode.
        { IsRunning: true, Syncs: false, Learning: true } =>
            "GameSync hasn't found where it saves, so learn mode is watching. When you quit, it shows you the places it wrote to; nothing syncs until you choose.",
        { IsRunning: true, Syncs: false } => "GameSync saw it start, so its play counts. Its saves aren't synced yet: sync them from its page when you're done.",
        { Syncs: false } => "GameSync found its saves. Sync them from its page to back them up and keep them in step.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { IsRunning: true } => "Its save syncs a few seconds after you quit.",
        { Cloud: { } cloud } => $"Its saves are backed up on this PC and in {cloud}.",
        _ => "Its saves are backed up on this PC; they go up once you connect a cloud.",
    };

    /// <summary>The same words as the game's page and its saves use for what needs doing.</summary>
    private static string ActionFor(GameStatus? status) => GameViewModel.ActionFor(status).Label;
}
