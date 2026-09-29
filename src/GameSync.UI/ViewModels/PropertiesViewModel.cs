using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>
/// A row of a game's files in its Properties (design system → FileTree, FIND-12): a place, a file in it, or the files past
/// the ones listed; ticked when it's backed up. A place's box shows whether all, some or none of it is in.
/// </summary>
public sealed partial class FileRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(IsOut))]
    private bool? _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    public FileRow(Action changed, string root, string path, string name, string kind, int depth, bool? isChecked, long bytes = 0, int count = 1,
        string? why = null, bool locked = false, string? tag = null, string? meta = null, bool readOnly = false)
    {
        _changed = changed;
        (Root, Path, Name, Kind, Depth, Bytes, Count, Why, Locked, Tag, Meta, ReadOnly) = (root, path, name, kind, depth, bytes, count, why, locked, tag, meta, readOnly);
        _isChecked = isChecked;
        Original = isChecked;
        ToggleCommand = new RelayCommand(Toggle, () => CanTick);
        ExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    /// <summary>The place's rule root key, or <c>registry:</c> and the key.</summary>
    public string Root { get; }

    /// <summary>The file's path inside its place; empty for the place itself.</summary>
    public string Path { get; }

    public string Name { get; }

    /// <summary><c>folder</c> (a place), <c>registry</c>, <c>file</c> or <c>more</c> (the files past the ones listed).</summary>
    public string Kind { get; }

    public int Depth { get; }

    public long Bytes { get; }

    /// <summary>How many files the row stands for: one, or the files past the ones listed.</summary>
    public int Count { get; }

    public string? Why { get; }

    /// <summary>Never backed up whatever the person picks: a program file (R1).</summary>
    public bool Locked { get; }

    public string? Tag { get; }

    public string? Meta { get; }

    /// <summary>What it was when the dialog opened, to tell what the person changed.</summary>
    public bool? Original { get; private set; }

    /// <summary>Takes what it shows now as what it was, once the dialog has built it.</summary>
    public void Settle() => Original = IsChecked;

    public FileRow? Place { get; private set; }

    public List<FileRow> Children { get; } = [];

    public bool IsPlace => Depth == 0;

    public bool HasChildren => Children.Count > 0;

    /// <summary>Shown but not changeable: the game isn't syncing yet, so this is what syncing would take.</summary>
    public bool ReadOnly { get; }

    /// <summary>It has a box: every file and place but a program file and the files past the ones listed.</summary>
    public bool ShowsBox => !Locked && Kind != "more";

    public bool CanTick => ShowsBox && !ReadOnly;

    public bool IsOut => IsChecked == false;

    public bool HasTag => Tag is not null;

    public string Icon => Kind switch
    {
        "folder" => "folder",
        "registry" => "key",
        "more" => "more",
        _ => "file",
    };

    public string Chevron => IsExpanded ? "chevronDown" : "chevronRight";

    public Avalonia.Thickness Indent => new(4 + Depth * 22, 0, 12, 0);

    /// <summary>Why it's out, while it is: "You left it out", "Skipped: a log, dump or cache", a program file's lock.</summary>
    public string? Note => Locked ? Why : IsChecked == false ? Why ?? (Kind == "more" ? null : "You left it out") : null;

    public ICommand ToggleCommand { get; }

    public ICommand ExpandCommand { get; }

    public void Add(FileRow child)
    {
        child.Place = this;
        Children.Add(child);
    }

    /// <summary>Ticks or unticks it: a place, everything in it that can be; a file, itself, and its place's box follows.</summary>
    public void Toggle()
    {
        if (!CanTick)
        {
            return;
        }

        if (IsPlace)
        {
            var on = IsChecked != true;
            IsChecked = on;
            foreach (var child in Children.Where(c => !c.Locked))
            {
                child.IsChecked = on;
            }
        }
        else
        {
            IsChecked = IsChecked != true;
            Place?.Recount();
        }

        _changed();
    }

    /// <summary>A place's box from its files: all in, none, or some.</summary>
    public void Recount()
    {
        var files = Children.Where(c => c.CanTick).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var on = files.Count(c => c.IsChecked == true);
        IsChecked = on == files.Count ? true : on == 0 ? false : null;
        foreach (var more in Children.Where(c => c.Kind == "more"))
        {
            // The files past the ones listed go with the place: in while any of it is.
            more.IsChecked = IsChecked != false;
        }
    }

    public override string ToString() => string.Join(", ", new[] { Name, Tag, Meta, IsChecked == true ? "backed up" : IsChecked == false ? "left out" : "partly backed up", Note }.OfType<string>());
}

/// <summary>A choice of a few, as a Select shows it: its id and words.</summary>
public sealed record Choice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One of a game's pictures in its Properties' Art (design system → GamePropertiesDialog, ART-06): Steam's, the person's
/// own image, or none, with Choose an image… and Use Steam's. A picked image is checked at once, like Steam's art, and
/// kept only when the person saves.
/// </summary>
public sealed partial class ArtSlot : ObservableObject
{
    private readonly Action _changed;

    /// <summary>What the person did: an image file picked, "" to take their own away, or null for as it was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwn), nameof(From), nameof(ChooseLabel), nameof(ChooseName), nameof(Shown))]
    private string? _picked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumb), nameof(NoThumb))]
    private IImage? _thumb;

    public ArtSlot(Action changed, GameArt art, string gameTitle)
    {
        _changed = changed;
        (Kind, Steam, Own, GameTitle) = (art.Kind, art.Steam, art.Own, gameTitle);
        UndoCommand = new RelayCommand(() => Picked = Own is null ? null : "");
        LoadThumb();
    }

    public ArtKind Kind { get; }

    public string? Steam { get; }

    /// <summary>The person's own image, as it was when the dialog opened.</summary>
    public string? Own { get; }

    public string GameTitle { get; }

    public string Title => Kind switch
    {
        ArtKind.Cover => "Cover",
        ArtKind.Hero => "Banner",
        _ => "Logo",
    };

    public string Description => Kind switch
    {
        ArtKind.Cover => "The tile in the library and on Home: tall, like Steam's 600 × 900.",
        ArtKind.Hero => "The wide picture at the top of its page, and on Home when it's the last played: like 1920 × 620.",
        _ => "Shown over the banner in place of its name: a PNG with a see-through background.",
    };

    /// <summary>Its own image shows: one picked now, or the one it had.</summary>
    public bool IsOwn => Picked is { Length: > 0 } || (Picked is null && Own is not null);

    /// <summary>The picture the slot shows now.</summary>
    public string? Shown => Picked is { Length: > 0 } picked ? picked : IsOwn ? Own : Steam;

    public string From => IsOwn ? "Your image" : Steam is not null ? "Steam's" : Kind == ArtKind.Logo ? "None: its name shows" : "None: a title cover shows";

    public string ChooseLabel => IsOwn ? "Choose another…" : "Choose an image…";

    /// <summary>What a screen reader says for the button (A11Y-03): "Choose an image for the banner".</summary>
    public string ChooseName => $"{(IsOwn ? "Choose another image" : "Choose an image")} for the {Title.ToLowerInvariant()}";

    public string UndoLabel => Steam is not null ? "Use Steam's" : "Remove";

    /// <summary>What a screen reader says for it: "Use Steam's banner", "Remove your logo".</summary>
    public string UndoName => Steam is not null ? $"Use Steam's {Title.ToLowerInvariant()}" : $"Remove your {Title.ToLowerInvariant()}";

    public bool HasError => Error is not null;

    public bool HasThumb => Thumb is not null;

    public bool NoThumb => Thumb is null;

    /// <summary>What the empty picture says: its name for a logo, the game's initial for the others.</summary>
    public string Placeholder => Kind == ArtKind.Logo ? "Its name" : GsGameTile.InitialOf(GameTitle);

    public double ThumbWidth => Kind switch
    {
        ArtKind.Cover => 48,
        ArtKind.Hero => 124,
        _ => 96,
    };

    public double ThumbHeight => Kind == ArtKind.Cover ? 72 : 40;

    /// <summary>Use Steam's, or Remove when Steam has none: the person's own image goes once they save.</summary>
    public ICommand UndoCommand { get; }

    /// <summary>What changed, as the host saves it; null when nothing did.</summary>
    public ArtChoice? Change => Picked switch
    {
        null => null,
        "" => Own is null ? null : new ArtChoice(Kind, null),
        var file => new ArtChoice(Kind, file),
    };

    /// <summary>An image picked in Windows' picker: checked now, like Steam's art (ART-08), and shown; kept when the person saves.</summary>
    public void Choose(string file)
    {
        try
        {
            GameSettings.Picture(file, Kind);
            Error = null;
            Picked = file;
        }
        catch (Exception e) when (e is UsageException or IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    partial void OnPickedChanged(string? value)
    {
        LoadThumb();
        OnPropertyChanged(nameof(Change));
        _changed();
    }

    private void LoadThumb() => Thumb = ArtImages.Load(Shown, (int)ThumbWidth * 2);

    public override string ToString() => $"{Title}: {From}";
}

/// <summary>
/// A game's Properties, like Steam's (design system → GamePropertiesDialog; LIB-20, FIND-12, ART-06): General, Art (its
/// own cover, banner and logo), Launch, Installed files, Saves (which files are backed up) and Sync. Changes wait for
/// Save changes; Cancel, Esc and the close button drop them, asking first when there are some.
/// </summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    public static readonly IReadOnlyList<Choice> SettingsChoices =
        [new(GameSettings.SyncBetween, "Sync between PCs"), new(GameSettings.ThisPc, "Back up on this PC"), new(GameSettings.Off, "Don't back up")];

    public static readonly IReadOnlyList<Choice> ScreenshotChoices = [new("on", "Back up on this PC"), new("off", "Don't back up")];

    public static readonly IReadOnlyList<Choice> ConflictChoices = [new("newest", "Newest wins"), new("ask", "Always ask"), new("this-pc", "This PC wins")];

    public static readonly IReadOnlyList<NavItem> ModeChoices = [new("sync", "Between your PCs"), new("backup", "Back up only")];

    private readonly LauncherActions? _actions;
    private GameProperties? _loaded;

    [ObservableProperty]
    private string _section;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _favourite;

    [ObservableProperty]
    private bool _shown = true;

    [ObservableProperty]
    private string _launchOptions = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteText))]
    private string _program = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsLabel))]
    private string _settingsFiles = GameSettings.ThisPc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotsLabel))]
    private string _screenshots = "off";

    [ObservableProperty]
    private bool _skipDefaults = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private string _mode = "sync";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConflictLabel))]
    private string _conflict = "newest";

    [ObservableProperty]
    private IReadOnlyList<FileRow> _rows = [];

    /// <summary>Its cover, banner and logo (ART-06).</summary>
    [ObservableProperty]
    private IReadOnlyList<ArtSlot> _art = [];

    [ObservableProperty]
    private string _countText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesText), nameof(HasChanges))]
    private int _changes;

    /// <summary>Closing with changes not saved asks first, in the dialog's foot.</summary>
    [ObservableProperty]
    private bool _confirmingDiscard;

    [ObservableProperty]
    private string? _copied;

    public PropertiesViewModel(GameId id, string title, LauncherActions? actions, string? section = null)
    {
        Id = id;
        Title = title;
        _name = title;
        _actions = actions;
        _section = Sections.Any(s => s.Id == section) ? section! : "general";
        SaveCommand = new RelayCommand(Save, () => _actions?.SaveProperties is not null);
        CancelCommand = new RelayCommand(() => Close(force: false));
        DiscardCommand = new RelayCommand(() => Close(force: true));
        KeepEditingCommand = new RelayCommand(() => ConfirmingDiscard = false);
        SelectCommand = new RelayCommand<string>(Select);
        OpenFolderCommand = new RelayCommand(() =>
        {
            if (_loaded?.InstallDir is { } folder)
            {
                _actions?.OpenFolder?.Invoke(folder);
            }
        }, () => _actions?.OpenFolder is not null);
        StorePageCommand = new RelayCommand(() =>
        {
            if (_loaded?.About.SteamAppId is { } app)
            {
                _actions?.OpenLink?.Invoke($"https://store.steampowered.com/app/{app.ToString(CultureInfo.InvariantCulture)}/");
            }
        }, () => _actions?.OpenLink is not null);
        SteamPageCommand = new RelayCommand(() =>
        {
            if (_loaded?.About.SteamAppId is { } app)
            {
                _actions?.OpenLink?.Invoke($"steam://nav/games/details/{app.ToString(CultureInfo.InvariantCulture)}");
            }
        }, () => _actions?.OpenLink is not null);
        StopSyncingCommand = new RelayCommand(StopSyncing, () => _actions?.SaveProperties is not null);
        UseFoundProgramCommand = new RelayCommand(() => Program = "");
    }

    public GameId Id { get; }

    public string Title { get; }

    public IReadOnlyList<NavItem> Sections { get; } =
    [
        new("general", "General", "info"),
        new("art", "Art", "palette"),
        new("launch", "Launch", "play"),
        new("files", "Installed files", "drive"),
        new("saves", "Saves", "saves"),
        new("sync", "Sync", "sync"),
    ];

    public bool IsLoading => !IsLoaded;

    public string ChangesText => Changes switch
    {
        0 => "No changes",
        1 => "1 change, not saved yet",
        _ => $"{Changes.ToString(CultureInfo.InvariantCulture)} changes, not saved yet",
    };

    public bool HasChanges => Changes > 0;

    public bool IsGeneral => Section == "general";

    public bool IsArt => Section == "art";

    public bool IsLaunch => Section == "launch";

    public bool IsFiles => Section == "files";

    public bool IsSaves => Section == "saves";

    public bool IsSync => Section == "sync";

    public string CommandLine => $"gamesync sync {Id}";

    public string? AntiCheatNote => _loaded?.About.AntiCheat is { } antiCheat
        ? $"Ships {antiCheat}: it starts only through {StoreNames.Name(_loaded.About.Store) ?? "its launcher"}, learn mode stays off, and its saves are never shared."
        : null;

    public bool HasAntiCheat => AntiCheatNote is not null;

    /// <summary>A store starts it: its own launch options apply, read only here.</summary>
    public bool ThroughStore => _loaded?.About.StoreLink is not null;

    public bool OwnProgram => IsLoaded && !ThroughStore;

    public bool IsSteam => _loaded?.About.Store == StoreKind.Steam && _loaded.About.SteamAppId is not null;

    public string RouteTitle => ThroughStore ? $"Starts through {StoreNames.Name(_loaded!.About.Store) ?? "its store"}" : "Program";

    public string RouteDescription => ThroughStore
        ? $"{StoreNames.Name(_loaded!.About.Store) ?? "Its store"} starts it, so its overlay, its cloud and its own launch options work as usual."
        : "GameSync starts it in its own folder, never as admin.";

    public string RouteText => ThroughStore ? _loaded!.About.StoreLink! : Program.Length > 0 ? Program : _loaded?.About.Program ?? "No program found in its folder yet";

    public bool ProgramPicked => Program.Length > 0;

    public string StoreOptions => _loaded?.About.StoreLaunchOptions ?? "None";

    /// <summary>What to put in Steam's launch options so a start from Steam gets GameSync's check too (PLAY-08).</summary>
    public string SteamLine => $"\"{Environment.ProcessPath ?? "GameSync.Tray.exe"}\" launch {Id} -- %command%";

    public IReadOnlyList<Fact> InstalledFacts => _loaded is not { } p ? [] : Fact.Known(
        new Fact("Folder", p.InstallDir, Mono: true),
        new Fact("Size", p.About.InstallBytes is { } bytes ? Cli.FormatSize(bytes) : null),
        new Fact("Store", StoreNames.Name(p.About.Store) is { } store ? p.About.StoreId is { } storeId ? $"{store} · app {storeId}" : store : "None: a game in its own folder"),
        new Fact("Build", p.About.Build, Mono: true),
        new Fact("Engine", p.About.Engine),
        new Fact("Found by", p.About.FoundBy));

    public bool Installed => _loaded?.Installed ?? true;

    public bool NotInstalled => !Installed;

    public bool CanOpenFolder => _loaded?.InstallDir is not null;

    public bool CanOpenStorePage => _loaded?.About.SteamAppId is not null;

    /// <summary>Its files can be chosen: it syncs. A game not syncing yet shows what was found.</summary>
    public bool CanChooseFiles => _loaded?.Syncs == true;

    public bool NotSyncing => IsLoaded && _loaded?.Syncs != true;

    public bool HasSettingsFiles => _loaded?.HasSettingsFiles == true;

    public string SettingsLabel => SettingsChoices.First(c => c.Id == SettingsFiles).Label;

    public string ScreenshotsLabel => ScreenshotChoices.First(c => c.Id == Screenshots).Label;

    public string ConflictLabel => ConflictChoices.First(c => c.Id == Conflict).Label;

    public string ModeDescription => Mode == "sync"
        ? "Each PC's save goes to the cloud after you play, and the newest comes down before you play."
        : "Its store syncs it between PCs; GameSync keeps every version as a backup and never brings one down by itself.";

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand DiscardCommand { get; }

    public ICommand KeepEditingCommand { get; }

    /// <summary>A Select's choice: <c>settings:off</c>, <c>shots:on</c>, <c>conflict:ask</c>.</summary>
    public ICommand SelectCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand StorePageCommand { get; }

    /// <summary>The game in Steam's library, where its own launch options are changed.</summary>
    public ICommand SteamPageCommand { get; }

    public ICommand StopSyncingCommand { get; }

    /// <summary>Back to the program GameSync found in the game's folder.</summary>
    public ICommand UseFoundProgramCommand { get; }

    /// <summary>Reads what the dialog shows, after it opens.</summary>
    public async void Load()
    {
        if (_actions?.LoadProperties is not { } load)
        {
            return;
        }

        try
        {
            if (await load(Id, CancellationToken.None) is { } properties)
            {
                Show(properties);
            }
            else
            {
                Error = $"{Title} isn't on this PC any more.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            Error = $"GameSync couldn't read {Title}'s properties on this PC: {e.Message}";
        }
    }

    /// <summary>The dialog's content, from what this PC knows of the game.</summary>
    public void Show(GameProperties properties)
    {
        _loaded = properties;
        Name = properties.Title;
        Favourite = properties.Favourite;
        Shown = !properties.Hidden;
        LaunchOptions = properties.About.LaunchOptions ?? "";
        Program = properties.About.ProgramPicked ? properties.About.Program ?? "" : "";
        SettingsFiles = properties.SettingsFiles;
        Screenshots = properties.Screenshots ? "on" : "off";
        SkipDefaults = properties.SkipDefaults;
        Mode = properties.Mode == GameMode.BackupOnly ? "backup" : "sync";
        Conflict = properties.Conflict switch
        {
            ConflictPolicy.AlwaysAsk => "ask",
            ConflictPolicy.ThisPcWins => "this-pc",
            _ => "newest",
        };
        BuildRows(properties);
        Art = properties.Art.Select(a => new ArtSlot(Recount, a, properties.Title)).ToList();
        IsLoaded = true;
        Changes = 0;
        foreach (var name in new[] { nameof(AntiCheatNote), nameof(HasAntiCheat), nameof(ThroughStore), nameof(OwnProgram), nameof(IsSteam), nameof(RouteTitle),
            nameof(RouteDescription), nameof(RouteText), nameof(StoreOptions), nameof(InstalledFacts), nameof(Installed), nameof(NotInstalled), nameof(CanOpenFolder),
            nameof(CanOpenStorePage), nameof(CanChooseFiles), nameof(NotSyncing), nameof(HasSettingsFiles) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>What changed, as the host saves it: only the settings the person touched.</summary>
    public GamePropertiesChange Change()
    {
        var p = _loaded;
        if (p is null)
        {
            return new GamePropertiesChange();
        }

        var mode = Mode == "backup" ? GameMode.BackupOnly : GameMode.Sync;
        var conflict = Conflict switch
        {
            "ask" => ConflictPolicy.AlwaysAsk,
            "this-pc" => ConflictPolicy.ThisPcWins,
            _ => ConflictPolicy.NewestWins,
        };
        return new GamePropertiesChange
        {
            Title = Name.Trim() != p.Title ? Name.Trim() : null,
            Favourite = Favourite != p.Favourite ? Favourite : null,
            Hidden = Shown == p.Hidden ? !Shown : null,
            LaunchOptions = !ThroughStore && LaunchOptions.Trim() != (p.About.LaunchOptions ?? "") ? LaunchOptions : null,
            Program = !ThroughStore && Program != (p.About.ProgramPicked ? p.About.Program ?? "" : "") ? Program : null,
            Mode = p.Syncs && mode != p.Mode ? mode : null,
            Conflict = p.Syncs && conflict != p.Conflict ? conflict : null,
            SettingsFiles = p.Syncs && SettingsFiles != p.SettingsFiles ? SettingsFiles : null,
            Screenshots = p.Syncs && (Screenshots == "on") != p.Screenshots ? Screenshots == "on" : null,
            SkipDefaults = p.Syncs && SkipDefaults != p.SkipDefaults ? SkipDefaults : null,
            Files = p.Syncs ? FileChoices() : [],
            Art = Art.Select(a => a.Change).OfType<ArtChoice>().ToList(),
        };
    }

    /// <summary>
    /// The ticks that changed, as the host takes them: a place unticked as a whole leaves it out; a place taken back
    /// comes back whole, less the files still unticked; otherwise each file that changed.
    /// </summary>
    public IReadOnlyList<FileChoice> FileChoices()
    {
        var choices = new List<FileChoice>();
        foreach (var place in _places)
        {
            var files = place.Children.Where(c => c.CanTick).ToList();
            if (place.Kind == "registry" || files.Count == 0)
            {
                if (place.IsChecked != place.Original && place.CanTick)
                {
                    choices.Add(new FileChoice(place.Root, "", place.IsChecked == true));
                }

                continue;
            }

            var wasOff = place.Original == false && files.All(f => f.Original == false);
            if (wasOff)
            {
                if (files.Any(f => f.IsChecked == true))
                {
                    choices.Add(new FileChoice(place.Root, "", true));
                    choices.AddRange(files.Where(f => f.IsChecked != true).Select(f => new FileChoice(place.Root, f.Path, false)));
                }

                continue;
            }

            if (files.All(f => f.IsChecked == false) && place.IsChecked == false)
            {
                choices.Add(new FileChoice(place.Root, "", false));
                continue;
            }

            choices.AddRange(files.Where(f => f.IsChecked != f.Original).Select(f => new FileChoice(place.Root, f.Path, f.IsChecked == true)));
        }

        return choices;
    }

    partial void OnSectionChanged(string value)
    {
        foreach (var name in new[] { nameof(IsGeneral), nameof(IsArt), nameof(IsLaunch), nameof(IsFiles), nameof(IsSaves), nameof(IsSync) })
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnNameChanged(string value) => Recount();

    partial void OnFavouriteChanged(bool value) => Recount();

    partial void OnShownChanged(bool value) => Recount();

    partial void OnLaunchOptionsChanged(string value) => Recount();

    partial void OnProgramChanged(string value)
    {
        OnPropertyChanged(nameof(ProgramPicked));
        Recount();
    }

    partial void OnSettingsFilesChanged(string value) => Recount();

    partial void OnScreenshotsChanged(string value) => Recount();

    partial void OnSkipDefaultsChanged(bool value) => Recount();

    partial void OnModeChanged(string value) => Recount();

    partial void OnConflictChanged(string value) => Recount();

    private readonly List<FileRow> _places = [];

    private void BuildRows(GameProperties properties)
    {
        _places.Clear();
        foreach (var place in properties.Places)
        {
            var tag = place.IsRegistry ? "Registry" : place.Category switch
            {
                SaveCategory.Config => "Settings",
                SaveCategory.Screenshots => "Screenshots",
                _ => null,
            };
            var meta = place.IsRegistry ? null : place.Missing ? "Not on this PC now" : FilesText(place.Files.Count + place.More);
            var row = new FileRow(FilesChanged, place.Root, "", place.Shown, place.IsRegistry ? "registry" : "folder", 0,
                !place.Off, tag: tag, meta: meta,
                why: place.Missing ? "Not on this PC now; it isn't taken for empty" : place.Off ? "You left it out" : null, locked: place.Missing, readOnly: !properties.Syncs);
            foreach (var file in place.Files)
            {
                row.Add(new FileRow(FilesChanged, place.Root, file.Path, file.Path, "file", 1, file.Included, file.Bytes, why: file.Why,
                    locked: file.Locked, meta: Cli.FormatSize(file.Bytes), readOnly: !properties.Syncs));
            }

            if (place.More > 0)
            {
                row.Add(new FileRow(FilesChanged, place.Root, "~more", $"{place.More.ToString(CultureInfo.InvariantCulture)} more files", "more", 1, !place.Off,
                    place.MoreBytes, place.More, meta: Cli.FormatSize(place.MoreBytes)));
            }

            if (!place.IsRegistry && !place.Off && row.Children.Any(c => c.CanTick || c.Locked))
            {
                var files = row.Children.Where(c => c.Kind == "file" && !c.Locked).ToList();
                var on = files.Count(c => c.IsChecked == true);
                row.IsChecked = files.Count == 0 ? true : on == files.Count ? true : on == 0 ? false : null;
            }

            row.Settle();
            row.IsExpanded = _places.Count == 0 && row.HasChildren;
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileRow.IsExpanded))
                {
                    Flatten();
                }
            };
            _places.Add(row);
        }

        Flatten();
        CountFiles();
    }

    /// <summary>The rows shown: each place, and the files of the open ones.</summary>
    private void Flatten() => Rows = _places.SelectMany(p => p.IsExpanded ? p.Children.Prepend(p) : [p]).ToList();

    private void FilesChanged()
    {
        CountFiles();
        Recount();
    }

    /// <summary>"12 of 14 files · 5.9 MB backed up".</summary>
    private void CountFiles()
    {
        var rows = _places.SelectMany(p => p.Children).Where(c => c.Kind is "file" or "more").ToList();
        var all = rows.Sum(r => r.Count);
        var inRows = rows.Where(r => r.IsChecked == true && !r.Locked).ToList();
        CountText = _loaded?.Syncs == false
            ? $"{all.ToString(CultureInfo.InvariantCulture)} files · {Cli.FormatSize(rows.Where(r => !r.Locked).Sum(r => r.Bytes))} found; nothing is backed up until its saves sync"
            : $"{inRows.Sum(r => r.Count).ToString(CultureInfo.InvariantCulture)} of {all.ToString(CultureInfo.InvariantCulture)} files · {Cli.FormatSize(inRows.Sum(r => r.Bytes))} backed up";
    }

    /// <summary>How many settings differ from what was read, for the dialog's foot.</summary>
    private void Recount()
    {
        if (_loaded is null)
        {
            return;
        }

        var change = Change();
        Changes = new object?[] { change.Title, change.Favourite, change.Hidden, change.LaunchOptions, change.Program, change.Mode, change.Conflict, change.SettingsFiles,
            change.Screenshots, change.SkipDefaults }.Count(v => v is not null) + change.Files.Count + change.Art.Count;
        if (Changes == 0)
        {
            ConfirmingDiscard = false;
        }
    }

    private void Select(string? choice)
    {
        if (choice?.Split(':') is not [var what, var value])
        {
            return;
        }

        switch (what)
        {
            case "settings":
                SettingsFiles = value;
                break;
            case "shots":
                Screenshots = value;
                break;
            case "conflict":
                Conflict = value;
                break;
        }
    }

    private void Save()
    {
        var change = Change();
        if (Changes > 0)
        {
            _actions?.SaveProperties?.Invoke(Id, change);
        }

        _actions?.CloseDialog?.Invoke();
    }

    private void StopSyncing()
    {
        _actions?.SaveProperties?.Invoke(Id, Change() with { StopSyncing = true });
        _actions?.CloseDialog?.Invoke();
    }

    /// <summary>Closes the dialog: at once with nothing changed, or once the person says to drop the changes.</summary>
    public void Close(bool force)
    {
        if (!force && Changes > 0)
        {
            ConfirmingDiscard = true;
            return;
        }

        _actions?.CloseDialog?.Invoke();
    }

    private static string FilesText(int count) => count == 1 ? "1 file" : $"{count.ToString(CultureInfo.InvariantCulture)} files";
}
