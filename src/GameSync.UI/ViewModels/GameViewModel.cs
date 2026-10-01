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
    private ICommand? _primaryCommand;

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

    /// <summary>The name Save as… is typing.</summary>
    [ObservableProperty]
    private string _saveName = "";

    public GameViewModel(LauncherGame game, TileItem tile, LauncherActions? actions)
    {
        _actions = actions;
        _game = game;
        PlayCommand = new RelayCommand(() => _actions?.Play(Id), () => _actions is not null);
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
        BackUpNowCommand = new RelayCommand(() => _actions?.BackUpNow?.Invoke(Id), () => _actions?.BackUpNow is not null);
        SaveAsCommand = new RelayCommand(() =>
        {
            var name = SaveName.Trim();
            if (name.Length > 0)
            {
                _actions?.SaveAs?.Invoke(Id, name);
                SaveName = "";
            }
        }, () => _actions?.SaveAs is not null);
        SyncGameCommand = new RelayCommand(() => _actions?.SyncGame?.Invoke(Id), () => _actions?.SyncGame is not null);
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
    public ICommand BackUpNowCommand { get; }

    /// <summary>Save as…: keeps this PC's save under the name typed (BAK-18).</summary>
    public ICommand SaveAsCommand { get; }

    /// <summary>Sync these saves: confirms what the scan found (FIND-06).</summary>
    public ICommand SyncGameCommand { get; }

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

    /// <summary>The page's content, from what this PC knows of the game.</summary>
    public void Show(GameDetail detail, DateTime nowLocal)
    {
        _detail = detail;
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
                new Fact("Found", detail.Places.Count == 0 ? "No saves yet"
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
        SavesStatus = game.Syncs ? game.Status ?? GameStatus.Synced : null;
        SavesStatusLabel = StatusWord(game);
        SavesSentence = SentenceOf(game);
        (NeedAction, NeedIcon) = game.NeedsYou ? ActionFor(game.Status) : (null, "alert");
        Stats = StatsOf(game, DateTime.Now);

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

    /// <summary>Last played, Play time and Saves, as the play bar shows them; Achievements join once they're read (after v1).</summary>
    public static IReadOnlyList<PlayStat> StatsOf(LauncherGame game, DateTime nowLocal)
    {
        var saves = new PlayStat("Saves", StatusWord(game), IsStatus: true, Status: game.Syncs ? game.Status ?? GameStatus.Synced : null);
        if (game.IsFolder)
        {
            // LIB-13: a folder of the person's own isn't played: when it last changed, and its saves.
            return [new PlayStat("Last changed", game.LastPlayedUtc is { } changed ? Launcher.WhenText(changed, nowLocal) ?? "Not yet" : "Not yet"), saves];
        }

        return
        [
            new PlayStat("Last played", game.IsRunning ? "Playing now" : game.LastPlayedUtc is { } at ? Launcher.WhenText(at, nowLocal) ?? "Never" : "Never"),
            new PlayStat("Play time", game.Playtime <= TimeSpan.Zero ? "None yet"
                : game.Playtime.TotalHours >= 1 ? Math.Round(game.Playtime.TotalHours) is var hours && hours == 1 ? "1 hour" : $"{hours.ToString(CultureInfo.InvariantCulture)} hours"
                : Math.Max(1, (int)Math.Round(game.Playtime.TotalMinutes)) is var minutes && minutes == 1 ? "1 minute" : $"{minutes} minutes"),
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
