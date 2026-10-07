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

/// <summary>
/// A game's page (design system → GameDetailScreen, LIB-18, LIB-19), opened beside the library's list: about the game,
/// with a little about its saves; the save work itself is in the save manager. Back and a breadcrumb; the hero of just
/// the art and the name; the play bar (Play or the status's own action, Last played, Play time, Saves, the favourite
/// star, Properties, More); About from its Steam store page; its Saves, a little, with Open in Saves; On this PC. Full
/// glass over the game's own art (LOOK-17).
/// </summary>
public sealed partial class GameViewModel : ObservableObject, IPageSurface
{
    /// <summary>An About longer than this shows three lines and Show more.</summary>
    private const int ShortAbout = 180;

    private readonly LauncherActions? _actions;
    private LauncherGame _game;
    private GameDetail? _detail;
    private int _loads;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private IImage? _heroArt;

    [ObservableProperty]
    private IImage? _logo;

    [ObservableProperty]
    private string? _eyebrow;

    /// <summary>KAN-55: where it's installed as a mark on the hero (a store's, or a folder), with <see cref="StoreTip"/> under the pointer.</summary>
    [ObservableProperty]
    private string? _storeIcon;

    [ObservableProperty]
    private string? _storeTip;

    [ObservableProperty]
    private IReadOnlyList<PlayStat> _stats = [];

    [ObservableProperty]
    private string? _primaryLabel;

    [ObservableProperty]
    private string _primaryIcon = "play";

    /// <summary>The primary is the one recommended action (Play, or the status's own), rather than Install through Steam.</summary>
    [ObservableProperty]
    private bool _primaryIsMain = true;

    [ObservableProperty]
    private bool _primaryEnabled = true;

    [ObservableProperty]
    private string? _primaryTip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryBusy), nameof(PrimaryBusyText))]
    private ICommand? _primaryCommand;

    /// <summary>KAN-80: Play was pressed and the game isn't running yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryBusy))]
    private bool _starting;

    /// <summary>KAN-80: the primary's job is under way: Play starting the game, or Back up now keeping the save.</summary>
    public bool PrimaryBusy => (PrimaryCommand == PlayCommand && Starting) || (PrimaryCommand == BackUpNowCommand && BackUpNowCommand.IsRunning);

    public string PrimaryBusyText => PrimaryCommand == PlayCommand ? "Starting" : "Backing up";

    /// <summary>Play as a round button beside the status's own action, when the game needs you.</summary>
    [ObservableProperty]
    private bool _showsPlayBeside;

    /// <summary>
    /// LIB-24: Locate the game… in place of Play, for a game not installed here that no store installs (found only by its
    /// saves, or one whose folder moved): its program, picked in Windows' picker, marks it installed.
    /// </summary>
    [ObservableProperty]
    private bool _canLocate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavouriteLabel))]
    private bool _isFavourite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideLabel))]
    private bool _isHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotInstalled))]
    private bool _installed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAbout), nameof(NoAbout), nameof(CanExpandAbout))]
    private string? _about;

    [ObservableProperty]
    private string? _aboutSource;

    [ObservableProperty]
    private string _aboutNote = "";

    [ObservableProperty]
    private IReadOnlyList<Fact> _aboutFacts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutLines), nameof(ExpandLabel))]
    private bool _aboutExpanded;

    [ObservableProperty]
    private bool _canOpenStorePage;

    [ObservableProperty]
    private GameStatus? _savesStatus;

    [ObservableProperty]
    private string? _savesStatusLabel;

    [ObservableProperty]
    private string? _savesSentence;

    [ObservableProperty]
    private IReadOnlyList<Fact> _savesFacts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveFolder))]
    private string? _saveFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesSyncing), nameof(SavesNotSyncing), nameof(SavesNeedYou))]
    private bool _syncs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesSyncing), nameof(SavesNeedYou))]
    private string? _needAction;

    [ObservableProperty]
    private string _needIcon = "alert";

    [ObservableProperty]
    private IReadOnlyList<Fact> _pcFacts = [];

    [ObservableProperty]
    private string? _gameFolder;

    public GameViewModel(LauncherGame game, TileItem tile, LauncherActions? actions)
    {
        _actions = actions;
        _game = game;
        PlayCommand = new AsyncRelayCommand(async () =>
        {
            // KAN-80: Play says Starting… from the press until the game runs, or the check says why it can't.
            Starting = true;
            if (_actions?.Start is { } start)
            {
                await start(Id);
            }
            else
            {
                _actions?.Play(Id);
            }

            Starting = _actions?.IsStarting?.Invoke(Id) == true;
        }, () => _actions is not null);
        ToggleFavouriteCommand = new RelayCommand(() => _actions?.SetFavourite?.Invoke(Id, !IsFavourite), () => _actions?.SetFavourite is not null);
        ToggleHiddenCommand = new RelayCommand(() => _actions?.SetHidden(Id, !IsHidden), () => _actions is not null);
        OpenPropertiesCommand = new RelayCommand<string>(section => _actions?.OpenProperties?.Invoke(Id, section), _ => _actions?.OpenProperties is not null);
        OpenSavesCommand = new RelayCommand(() => _actions?.OpenSaves?.Invoke(Id), () => _actions?.OpenSaves is not null);
        OpenConflictCommand = new RelayCommand(() => _actions?.OpenConflict?.Invoke(Id), () => _actions?.OpenConflict is not null);
        OpenStorePageCommand = new RelayCommand(() =>
        {
            if (_game.SteamAppId is { } app)
            {
                _actions?.OpenLink?.Invoke($"https://store.steampowered.com/app/{app.ToString(CultureInfo.InvariantCulture)}/");
            }
        }, () => _actions?.OpenLink is not null);
        OpenGameFolderCommand = new RelayCommand(() =>
        {
            if (GameFolder is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
        OpenSaveFolderCommand = new RelayCommand(() =>
        {
            if (SaveFolder is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
        BackUpNowCommand = new AsyncRelayCommand(async () =>
        {
            if (_actions?.BackUpNow is { } backUp)
            {
                await backUp(Id);
            }
        }, () => _actions?.BackUpNow is not null);
        BackUpNowCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
            {
                OnPropertyChanged(nameof(PrimaryBusy));
            }
        };
        OpenNamedSaveCommand = new RelayCommand(() =>
        {
            if (!Syncs && _detail?.Kept is not null)
            {
                _actions?.OpenKeptCopies?.Invoke(new KeptCopiesStart(Id, Title, Backup: true));
                return;
            }

            _actions?.OpenNamedSave?.Invoke(NamedSaveStart.For(Id, Title,
                _detail?.Places.Select(p => (p.Folder, p.Tag, p.Evidence)) ?? [], _detail?.NamedSaves.Select(n => n.Name) ?? [], Syncs ? null : KeepLine));
        }, () => _actions?.OpenNamedSave is not null);
        SyncGameCommand = new AsyncRelayCommand(async () =>
        {
            // KAN-61: only the live save, the copies beside it as named saves, in its dialog.
            if (!Syncs && _detail?.Kept is not null)
            {
                _actions?.OpenKeptCopies?.Invoke(new KeptCopiesStart(Id, Title, Backup: false));
            }
            else if (_actions?.SyncGame is { } sync)
            {
                await sync(Id);
            }
        }, () => _actions?.SyncGame is not null);
        ToggleAboutCommand = new RelayCommand(() => AboutExpanded = !AboutExpanded);
        Update(game, tile);
    }

    public GameId Id => _game.Id;

    public GlassStrength Strength => GlassStrength.Glass;

    /// <summary>Glossy's backdrop: the game's own hero picture, or its cover when there's none.</summary>
    public string? BackdropArt => _game.HeroPath ?? _game.CoverPath;

    public string FavouriteLabel => IsFavourite ? "Remove from favourites" : "Add to favourites";

    public string HideLabel => IsHidden ? "Show in the library" : "Hide from the library";

    public bool NotInstalled => !Installed;

    public bool HasAbout => About is not null;

    public bool NoAbout => About is null;

    public bool CanExpandAbout => About is { Length: > ShortAbout };

    public int AboutLines => AboutExpanded ? 0 : 3;

    public string ExpandLabel => AboutExpanded ? "Show less" : "Show more";

    public bool HasSaveFolder => SaveFolder is not null;

    public bool SavesNeedYou => Syncs && NeedAction is not null;

    public bool SavesSyncing => Syncs && NeedAction is null;

    public bool SavesNotSyncing => !Syncs;

    /// <summary>Not syncing yet, and its saves were found: Sync these saves, New named save… and Choose files… have something to work on.</summary>
    [ObservableProperty]
    private bool _savesHavePlaces;

    /// <summary>FIND-04: learn mode found where it saves, so the Saves card offers See what it found.</summary>
    [ObservableProperty]
    private bool _savesLearnFound;

    [RelayCommand]
    private void SeeLearnFinds() => _actions?.OpenLearnFinds?.Invoke(Id);

    public bool CanOpenGameFolder => GameFolder is not null;

    public ICommand PlayCommand { get; }

    public ICommand ToggleFavouriteCommand { get; }

    public ICommand ToggleHiddenCommand { get; }

    /// <summary>The game's Properties, on the section given (LIB-20).</summary>
    public ICommand OpenPropertiesCommand { get; }

    /// <summary>The game's saves in the save manager, where the save work is (MGR-07).</summary>
    public ICommand OpenSavesCommand { get; }

    /// <summary>The game's conflict, both saves side by side (SYNC-10).</summary>
    public ICommand OpenConflictCommand { get; }

    /// <summary>
    /// The status's own action when the game needs you: Resolve opens its conflict; the others its saves, where the
    /// sentence says what happened and the buttons deal with it.
    /// </summary>
    public ICommand NeedCommand => _game.Status == GameStatus.Conflict && _actions?.OpenConflict is not null ? OpenConflictCommand : OpenSavesCommand;

    public ICommand OpenStorePageCommand { get; }

    public ICommand OpenGameFolderCommand { get; }

    public ICommand OpenSaveFolderCommand { get; }

    /// <summary>Back up now (BAK-16); not while the game runs, since quitting backs it up.</summary>
    public IAsyncRelayCommand BackUpNowCommand { get; }

    /// <summary>New named save… (BAK-18): this PC's save kept under a name, in its dialog (KAN-77).</summary>
    public ICommand OpenNamedSaveCommand { get; }

    /// <summary>Sync these saves: confirms what the scan found (FIND-06).</summary>
    public IAsyncRelayCommand SyncGameCommand { get; }

    public ICommand ToggleAboutCommand { get; }

    /// <summary>It's being played now, however it was started (PLAY-12).</summary>
    public bool IsPlaying => _game.IsRunning;

    /// <summary>Locate the game…: the program picked in Windows' picker; the app marks the game installed in its folder (LIB-24).</summary>
    public void Locate(string program) => _actions?.Locate?.Invoke(Id, program);

    /// <summary>What the page reads after it opens, and again whenever the games refresh: About, the saves at a glance, this PC.</summary>
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
                SavesSentence = $"GameSync couldn't read this game's saves on this PC: {e.Message}";
            }
        }
    }

    /// <summary>What Save as… says first on a game not syncing yet, with what keeping it starts with (KAN-63).</summary>
    [ObservableProperty]
    private string _keepLine = GameSavesViewModel.KeepNote;

    /// <summary>KAN-61: a game not syncing yet whose live save sits beside copies kept by hand says so on its Saves card.</summary>
    private string? KeptSentence() => !Syncs && _detail?.Kept is { } kept
        ? $"GameSync found its live save and {(kept.Copies == 1 ? "1 copy" : $"{kept.Copies.ToString(CultureInfo.InvariantCulture)} copies")} of it you kept by hand beside it. Nothing is backed up yet."
        : null;

    /// <summary>The page's content, from what this PC knows of the game.</summary>
    public void Show(GameDetail detail, DateTime nowLocal)
    {
        _detail = detail;
        SavesSentence = KeptSentence() ?? (detail.Syncs ? null : GameSavesViewModel.LearnLine(Title, detail.Learn, detail.Places.Count == 0)) ?? SavesSentence;
        SavesHavePlaces = !detail.Syncs && detail.Places.Count > 0;
        SavesLearnFound = !detail.Syncs && detail.Learn.State == LearnState.Found;
        Stats = StatsOf(_game, nowLocal, detail.Current?.NewestUtc, detail.AchievementsLeftOut ? null : detail.Achievements);
        ShowAchievements(detail.Achievements, detail.AchievementsLeftOut, nowLocal, detail.ZenithSeen);
        KeepLine = GameSavesViewModel.KeepNoteFor(detail.FoundFiles, detail.FoundBytes);
        var about = detail.About;
        About = about.Description;
        AboutSource = about.Description is null ? null : "From its Steam store page";
        AboutNote = _game.ByHand && _game.SteamAppId is null ? "Added by hand, so there's no store page to show."
            : _game.SteamAppId is null ? "No Steam store page matches this game, so there's nothing more to show about it."
            : "Its Steam store page comes with its art, the next time GameSync asks Steam.";
        AboutFacts = Fact.Known(
            new Fact("Developer", Join(about.Developers)),
            new Fact("Publisher", Join(about.Publishers)),
            new Fact("Released", about.ReleasedUtc?.ToString("d MMM yyyy", CultureInfo.InvariantCulture)),
            about.Genres.Count > 0 ? new Fact("Genres", null, Tags: about.Genres) : null,
            new Fact("Engine", about.Engine));
        CanOpenStorePage = _game.SteamAppId is not null;

        SavesFacts = detail.Syncs
            ? Fact.Known(
                new Fact("Backed up", detail.LastBackupUtc is { } at ? $"{GameSavesViewModel.When(at, nowLocal)} · {detail.LastBackupPc?.ToUpperInvariant()}" : "Not yet"),
                new Fact("History", detail.Versions.Count == 0 ? "No versions yet"
                    : $"{(detail.Versions.Count == 1 ? "1 version" : $"{detail.Versions.Count.ToString(CultureInfo.InvariantCulture)} versions")} · {Cli.FormatSize(detail.HistoryBytes)}"),
                new Fact("Named", detail.NamedSaves.Count switch { 0 => "None yet", 1 => "1 save", var n => $"{n.ToString(CultureInfo.InvariantCulture)} saves" }))
            : Fact.Known(
                new Fact("Found", detail.Places.Count == 0 ? detail.Learn.State switch
                    {
                        LearnState.Waiting => "Not yet: learn mode watches next time",
                        LearnState.Watching => "Learn mode is watching",
                        LearnState.Found => $"Learn mode found {(detail.Learn.Finds?.Places.Count == 1 ? "1 place" : $"{(detail.Learn.Finds?.Places.Count ?? 0).ToString(CultureInfo.InvariantCulture)} places")}",
                        LearnState.NothingFound => "Not yet: learn mode watches again",
                        _ => "No saves yet",
                    }
                    : detail.Places.Count == 1 ? detail.Places[0].Evidence
                    : $"{detail.Places.Count.ToString(CultureInfo.InvariantCulture)} places · {detail.Places[0].Evidence}"));
        SaveFolder = detail.Places.Select(p => p.Folder).FirstOrDefault(f => f is not null);

        GameFolder = detail.InstallDir is { } dir && Directory.Exists(dir) ? dir : null;
        OnPropertyChanged(nameof(CanOpenGameFolder));
        var options = about.StoreLink is not null ? about.StoreLaunchOptions : about.LaunchOptions;
        PcFacts = Installed
            ? Fact.Known(
                new Fact("Installed in", detail.InstallDir, Mono: true),
                new Fact("Size", about.InstallBytes is { } bytes ? Cli.FormatSize(bytes) : null),
                new Fact("Starts", about.StoreLink is not null ? $"Through {StoreNames.Name(about.Store) ?? "its store"}" : "Its own program"),
                new Fact("Options", options ?? "None", Mono: options is not null))
            : [];
        (Eyebrow, StoreIcon, StoreTip) = EyebrowOf(_game, detail);
    }

    /// <summary>The game as the library knows it now: its status, play and marks may have changed since the page opened.</summary>
    public void Update(LauncherGame game, TileItem tile)
    {
        var artChanged = game.HeroPath != _game.HeroPath || game.LogoPath != _game.LogoPath || HeroArt is null && Logo is null;
        _game = game;
        Title = game.Title;
        if (artChanged)
        {
            HeroArt = ArtImages.Load(game.HeroPath, 1920) ?? tile.Art;
            Logo = ArtImages.Load(game.LogoPath, 760);
        }

        (Eyebrow, StoreIcon, StoreTip) = EyebrowOf(game, _detail);
        IsFavourite = game.IsFavourite;
        IsHidden = game.IsHidden;
        Installed = game.Installed;
        Syncs = game.Syncs;
        if (_actions?.IsStarting is { } starting)
        {
            // KAN-80: Starting… lasts until the game runs, or the app gives up on it.
            Starting = !game.IsRunning && starting(game.Id);
        }

        SavesStatus = game.Syncs ? game.Status ?? GameStatus.Synced : null;
        SavesStatusLabel = StatusWord(game);
        SavesSentence = KeptSentence() ?? SentenceOf(game);
        (NeedAction, NeedIcon) = game.NeedsYou ? ActionFor(game.Status) : (null, "alert");
        Stats = StatsOf(game, DateTime.Now, _detail?.Current?.NewestUtc, _detail is { AchievementsLeftOut: false } ? _detail.Achievements : null);

        // The one recommended action: Play; the status's own when the game needs you; when it isn't here, Install through
        // Steam for a Steam game, and Locate the game… for one no store installs.
        ShowsPlayBeside = false;
        PrimaryIsMain = true;
        PrimaryEnabled = true;
        PrimaryTip = null;
        CanLocate = !game.Installed && (game.Store is null or StoreKind.Loose) && !game.ByHand && _actions?.Locate is not null;
        if (!game.Installed)
        {
            PrimaryLabel = game.Store == StoreKind.Steam && game.SteamAppId is not null ? "Install through Steam" : null;
            PrimaryIcon = "download";
            PrimaryIsMain = false;
            PrimaryCommand = new RelayCommand(() => _actions?.OpenLink?.Invoke($"steam://install/{game.SteamAppId?.ToString(CultureInfo.InvariantCulture)}"));
        }
        else if (game.IsRunning)
        {
            PrimaryLabel = "Playing";
            PrimaryIcon = "play";
            PrimaryEnabled = false;
            PrimaryTip = "It's running now";
            PrimaryCommand = null;
            ShowsPlayBeside = false;
        }
        else if (game.Syncs && NeedAction is { } need)
        {
            PrimaryLabel = need;
            PrimaryIcon = NeedIcon;
            PrimaryCommand = NeedCommand;
            ShowsPlayBeside = !game.IsFolder;
        }
        else if (game.IsFolder)
        {
            // LIB-13: a folder of the person's own has nothing to play; backing it up now is what it offers.
            PrimaryLabel = game.Syncs ? "Back up now" : null;
            PrimaryIcon = "upload";
            PrimaryCommand = BackUpNowCommand;
        }
        else
        {
            PrimaryLabel = "Play";
            PrimaryIcon = "play";
            PrimaryCommand = PlayCommand;
        }

        OnPropertyChanged(nameof(BackdropArt));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(NeedCommand));
    }

    /// <summary>
    /// ACH-01 to ACH-03, the Achievements card under About (design system version 33 → GameDetailScreen): the game's
    /// overview (its ring, the tiers unlocked and its Zenith), the latest five unlocked, and the rarest in its own light;
    /// View all opens every one. Every game's page has it since version 35 (KAN-111, the owner: "it should still have
    /// achievements on its game page. Let it say 0"): Steam's list at 0 for a copy that isn't Steam's or a game not run
    /// here, a line when there's none to read, and a game left out of the achievements says so (KAN-110).
    /// </summary>
    private void ShowAchievements(GameAchievementsView? view, bool leftOut, DateTime nowLocal, bool zenithSeen = true)
    {
        // Version 49: the first time this game's 100% is seen on this PC, its moment plays, once.
        CelebratesZenith = !leftOut && view is { IsComplete: true } && !zenithSeen && !_zenithSeenHere;
        AchievementsView = leftOut ? null : view;
        HasAchievements = !_game.IsFolder;
        AchievementsLeftOut = leftOut;
        ShowsAchievementsOverview = !leftOut && view is { Total: > 0 };
        string Count(int n) => n.ToString(CultureInfo.InvariantCulture);
        AchievementsSubtitle = leftOut ? "Left out of your achievements" : view switch
        {
            null => null,
            { From: AchievementsFrom.ListNotSteamCopy } => $"0 of {Count(view.Total)} · Steam's list",
            { From: AchievementsFrom.ListNotPlayedHere } => $"0 of {Count(view.Total)} · none unlocked on this PC",
            { From: AchievementsFrom.NoneOnSteam } => "0 achievements on Steam",
            { From: AchievementsFrom.CopyRecord, IsComplete: true } => "Every achievement unlocked · this copy's record",
            { From: AchievementsFrom.CopyRecord } => $"{Count(view.Unlocked)} of {Count(view.Total)} unlocked · this copy's record",
            { IsComplete: true } => "Every achievement unlocked on Steam",
            _ => $"{Count(view.Unlocked)} of {Count(view.Total)} unlocked on Steam",
        };
        AchievementsNote = leftOut ? null : view?.From switch
        {
            AchievementsFrom.ListNotSteamCopy => "This copy isn't Steam's, so what you unlock in it doesn't show here: if it keeps its own record, add the folder in Settings, Achievements. The list is Steam's, with how rare each one is.",
            AchievementsFrom.CopyRecord => "This copy isn't Steam's, so what you've unlocked comes from its own record, in a folder added in Settings, Achievements. Each one's name, icon and how rare it is are Steam's.",
            AchievementsFrom.ListNotPlayedHere => "Steam on this PC hasn't seen you play it, so nothing shows as unlocked yet; once you play it here, what you unlock does.",
            AchievementsFrom.NoneOnSteam => _game.Store == StoreKind.Steam
                ? "Steam lists no achievements for this game."
                : "Steam lists no achievements for this game. A game with its own launcher, such as Ubisoft Connect, may keep them there; GameSync reads Steam's for now.",
            AchievementsFrom.Steam => null,
            _ => _game.SteamAppId is not null
                ? "Its list of achievements comes from Steam the next time GameSync can ask."
                : "GameSync can't read this game's achievements yet: it reads Steam's for now.",
        };
        AchievementsProgress = view is null || leftOut ? AchievementsProgress.None : AchievementsProgress.Of(view, nowLocal);
        LatestAchievements = leftOut ? [] : view?.Latest(5).Select(a => AchievementItem.Of(a, nowLocal)).ToList() ?? [];
        RarestAchievement = !leftOut && view?.Rarest is { } rarest ? AchievementItem.Of(rarest, nowLocal) : null;
        NoneUnlocked = !leftOut && view is { IsTracked: true, Unlocked: 0 };
        OnPropertyChanged(nameof(CanViewAllAchievements));
    }

    /// <summary>
    /// Version 49 (the owner, 5 Oct 2026: "I really want players to feel awarded when they hit 100%"): this game's 100% is
    /// seen here for the first time, so its card's ring plays its moment and the Zenith's banner unfurls; once.
    /// </summary>
    [ObservableProperty]
    private bool _celebratesZenith;

    private bool _zenithSeenHere;

    /// <summary>The moment played: remembered on this PC, so it doesn't again.</summary>
    public ICommand SeeZenithCommand => new RelayCommand(() =>
    {
        _zenithSeenHere = true;
        CelebratesZenith = false;
        _actions?.SeeZenith?.Invoke(Id);
    });

    /// <summary>The game's achievements as last read, for View all.</summary>
    public GameAchievementsView? AchievementsView { get; private set; }

    /// <summary>Every game's page has the card but a folder of the person's own (LIB-13), which isn't played.</summary>
    [ObservableProperty]
    private bool _hasAchievements;

    /// <summary>Left out of the achievements in Settings (KAN-110): the card says so, with Count it again.</summary>
    [ObservableProperty]
    private bool _achievementsLeftOut;

    /// <summary>Its ring, tiers and Zenith: a game with a list to show, not left out.</summary>
    [ObservableProperty]
    private bool _showsAchievementsOverview;

    /// <summary>The card's line on where its achievements come from, or why there are none to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievementsNote), nameof(AchievementsNoteIcon))]
    private string? _achievementsNote;

    public bool HasAchievementsNote => AchievementsNote is not null;

    /// <summary>Where the list comes from is information; none to read is the trophy's own line.</summary>
    public string AchievementsNoteIcon => AchievementsView is null or { Total: 0 } ? "trophy" : "info";

    /// <summary>Counts a game left out back in (KAN-110): its popups and its place on the Achievements page and Home.</summary>
    public ICommand CountAchievementsAgainCommand => new RelayCommand(() => _actions?.SetAchievementsLeftOut?.Invoke(Id, false));

    [ObservableProperty]
    private string? _achievementsSubtitle;

    [ObservableProperty]
    private AchievementsProgress _achievementsProgress = AchievementsProgress.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLatestAchievements))]
    private IReadOnlyList<AchievementItem> _latestAchievements = [];

    public bool HasLatestAchievements => LatestAchievements.Count > 0;

    /// <summary>The rarest unlocked, in the spotlight; null until Steam's percentages are kept.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRarest))]
    private AchievementItem? _rarestAchievement;

    public bool HasRarest => RarestAchievement is not null;

    /// <summary>None unlocked yet: the card says so instead of an empty row.</summary>
    [ObservableProperty]
    private bool _noneUnlocked;

    /// <summary>View all: every one of its achievements, beside the library's list.</summary>
    public ICommand ViewAllAchievementsCommand => new RelayCommand(() => _actions?.OpenAchievements?.Invoke(Id, null));

    public bool CanViewAllAchievements => _actions?.OpenAchievements is not null && AchievementsView is { Total: > 0 };

    /// <summary>Last played, Play time, Achievements (a game whose store keeps them, ACH-01) and Saves, as the play bar shows them.</summary>
    /// <param name="newestFile">
    /// KAN-43: when its newest save file was written, for a folder of the person's own GameSync hasn't seen change yet.
    /// </param>
    /// <param name="achievements">Its achievements on this PC, once its page has read them.</param>
    public static IReadOnlyList<PlayStat> StatsOf(LauncherGame game, DateTime nowLocal, DateTime? newestFile = null, GameAchievementsView? achievements = null)
    {
        var saves = new PlayStat("Saves", StatusWord(game), IsStatus: true, Status: game.Syncs ? game.Status ?? GameStatus.Synced : null);
        if (game.IsFolder)
        {
            // LIB-13: a folder of the person's own isn't played: when it last changed, and its saves.
            var changed = game.LastPlayedUtc ?? newestFile;
            return [new PlayStat("Last changed", changed is { } when ? Launcher.WhenText(when, nowLocal) ?? "Not yet" : "Not yet"), saves];
        }

        return
        [
            new PlayStat("Last played", game.IsRunning ? "Playing now" : game.LastPlayedUtc is { } at ? Launcher.WhenText(at, nowLocal) ?? "Never" : "Never"),
            new PlayStat("Play time", game.Playtime <= TimeSpan.Zero ? "None yet"
                : game.Playtime.TotalHours >= 1 ? Math.Round(game.Playtime.TotalHours) is var hours && hours == 1 ? "1 hour" : $"{hours.ToString(CultureInfo.InvariantCulture)} hours"
                : Math.Max(1, (int)Math.Round(game.Playtime.TotalMinutes)) is var minutes && minutes == 1 ? "1 minute" : $"{minutes} minutes"),
            .. achievements is null ? Array.Empty<PlayStat>()
                : [new PlayStat("Achievements", $"{achievements.Unlocked.ToString(CultureInfo.InvariantCulture)} of {achievements.Total.ToString(CultureInfo.InvariantCulture)}")],
            saves,
        ];
    }

    /// <summary>The status in a word or two: "Synced", "Synced by Steam", "Held for review", "Not syncing yet".</summary>
    public static string StatusWord(LauncherGame game) =>
        !game.Syncs ? "Not syncing yet" : HomeViewModel.StatusLabel(game) ?? GsStatusBadge.Describe(game.Status ?? GameStatus.Synced).Word;

    /// <summary>The Saves card's sentence: what's true of the game's saves, shortly.</summary>
    public static string SentenceOf(LauncherGame game) => game switch
    {
        { Syncs: false, Installed: false } => "Its saves are still on this PC. Sync them to keep them safe for when you play again.",
        { Syncs: false } => "GameSync found its saves on this PC. Nothing is backed up yet.",
        { Status: GameStatus.HeldForReview } => "Its save changed while the game wasn't running, so GameSync held it for you to look at.",
        { NeedsYou: true, StatusDetail: { Length: > 0 } detail } => detail,
        { Status: GameStatus.Playing } => "Running now. Its save syncs a few seconds after you quit.",
        { FirstBackupPending: true } => "Not backed up yet: its first backup happens when GameSync next syncs, or now with Back up now.",
        { IsFolder: true, Syncs: true, NeedsYou: false } => "It syncs once its folder has been quiet for 5 minutes, so a server's world is kept between its autosaves; every version is kept.",
        { Status: GameStatus.BackupOnly, StoreSyncs: false } => "Backed up, every version kept; not synced between your PCs.",
        { Status: GameStatus.BackupOnly } => $"{StoreNames.SyncingStore(game.Store)} syncs its saves between your PCs; GameSync keeps a backup of every version.",
        { Cloud: { } cloud } => $"Backed up on this PC and in {cloud}, every version kept.",
        _ => "Backed up on this PC, every version kept; it goes up once you connect a cloud.",
    };

    /// <summary>The status's own action and its icon, for a game that needs you: Resolve leads to its conflict, the others to its saves.</summary>
    public static (string Label, string Icon) ActionFor(GameStatus? status) => status switch
    {
        GameStatus.Conflict => ("Resolve", "alert"),
        GameStatus.HeldForReview => ("Review", "pause"),
        GameStatus.SavesMissing or GameStatus.NoSaves => ("See where", "search"),
        GameStatus.FilesInUse => ("See why", "lock"),
        _ => ("See why", "alert"),
    };

    /// <summary>
    /// The hero's eyebrow (KAN-55): where the game is installed as a mark, with the words under the pointer, and only what
    /// the mark doesn't say as text. A store's game: its mark ("Installed through Steam"), no text. In its own folder: a
    /// folder, then "G:\Black Myth Wukong · Unreal Engine". Not installed: "Not installed on this PC", with the store's
    /// mark when a store has it. With no mark: "Not installed on this PC · Found by its saves", "Added by hand".
    /// </summary>
    public static (string Text, string? Icon, string? Tip) EyebrowOf(LauncherGame game, GameDetail? detail)
    {
        var icon = MarkOf(game.Store);
        if (StoreNames.Name(game.Store) is { } store)
        {
            return game.Installed
                ? ("", icon, $"Installed through {StoreTipName(game.Store)}")
                : ("Not installed on this PC", icon, $"On {store}; not installed on this PC");
        }

        if (game.Store == StoreKind.Loose)
        {
            return game.Installed
                ? (string.Join(" · ", new[] { detail?.InstallDir, detail?.About.Engine }.OfType<string>()), "folder",
                    "In its own folder, started without a store's launcher")
                : ("Not installed on this PC", "folder", "In its own folder when it's installed");
        }

        var where = game.IsOwn ? "Added by you" : game.ByHand ? "Added by hand" : "Found by its saves";
        return (game.Installed ? where : $"Not installed on this PC · {where}", null, null);
    }

    /// <summary>A store's mark for <see cref="GsIcon"/>: <c>steam</c>, <c>epic</c>, <c>ea</c>; a folder for a game in its own folder.</summary>
    public static string? MarkOf(StoreKind? store) => store switch
    {
        StoreKind.Steam => "steam",
        StoreKind.Epic => "epic",
        StoreKind.Ea => "ea",
        StoreKind.Loose => "folder",
        _ => null,
    };

    private static string StoreTipName(StoreKind? store) => store switch
    {
        StoreKind.Epic => "the Epic Games Launcher",
        StoreKind.Ea => "the EA app",
        _ => StoreNames.Name(store) ?? "its store",
    };

    private static string? Join(IReadOnlyList<string> names) => names.Count == 0 ? null : string.Join(", ", names);
}
