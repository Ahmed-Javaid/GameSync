using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.Sync;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>How the share window opens (SHARE-01, SHARE-04): picking saves or sharing all, with games ticked, or one version (Export).</summary>
public sealed record ShareStart(string Mode, IReadOnlyList<GameId> Games, GameId? Game = null, VersionId? Version = null)
{
    public const string Pick = "pick";
    public const string All = "all";
}

/// <summary>What the share and import windows ask of the app; the defaults do nothing, for renders and tests.</summary>
public sealed record ShareActions
{
    public Func<CancellationToken, Task<IReadOnlyList<ShareGameInfo>>> List { get; init; } = _ => Task.FromResult<IReadOnlyList<ShareGameInfo>>([]);

    public Func<IReadOnlyList<SharePick>, string, IProgress<(long Done, long Total)>, CancellationToken, Task<SharePack>> Pack { get; init; } =
        (_, path, _, _) => Task.FromResult(new SharePack(path, 0, 0, 0, []));

    /// <summary>FOLD-09: the folder shared zips go to, and whether to ask each time.</summary>
    public Func<(string Folder, bool Ask)> Where { get; init; } = () => (Path.GetTempPath(), false);

    public Func<string, CancellationToken, Task<ImportPreview>> Preview { get; init; } =
        (zip, _) => Task.FromResult(new ImportPreview(zip, DateTime.UtcNow, "", []));

    public Func<string, IReadOnlyList<GameId>, Task<Outcome>> Import { get; init; } = (_, _) => Task.FromResult(new Outcome(""));

    public Func<GameId, string?> CoverOf { get; init; } = _ => null;

    /// <summary>Explorer, with the zip selected.</summary>
    public Action<string> ShowInFolder { get; init; } = _ => { };

    public Action Close { get; init; } = () => { };
}

/// <summary>A version in the share window (SHARE-02): what it is, when and where it was saved, its size, ticked or not.</summary>
public sealed partial class ShareVersionRow(ShareVersionInfo info, DateTime nowLocal) : ObservableObject
{
    public ShareVersionInfo Info => info;

    public string Label => info.Label;

    /// <summary>"Today 21:04".</summary>
    public string When => SettingsViewModel.When(info.SavedUtc, nowLocal);

    public string Meta => $"{When} {info.Pc}{(info.Kept && !info.Current ? " · pinned" : "")}";

    public string Size => Cli.FormatSize(info.Bytes);

    [ObservableProperty]
    private bool _ticked;

    public override string ToString() => $"{Label}, {Meta}, {Size}";
}

/// <summary>A game in the share window: ticked takes its latest save; its chevron opens its versions (SHARE-01, SHARE-02); locked ones say why (SHARE-07).</summary>
public sealed partial class ShareGameRow : ObservableObject
{
    private readonly ShareGameInfo _info;
    private readonly Func<GameId, string?> _coverOf;
    private readonly Action _changed;
    private IImage? _cover;
    private bool _coverRead;
    private bool _quiet;

    public ShareGameRow(ShareGameInfo info, Func<GameId, string?> coverOf, Action changed, DateTime nowLocal)
    {
        _info = info;
        _coverOf = coverOf;
        _changed = changed;
        Versions = info.Versions.Select(v => new ShareVersionRow(v, nowLocal)).ToList();
        foreach (var version in Versions)
        {
            version.PropertyChanged += VersionChanged;
        }

        Latest = Versions.FirstOrDefault(v => v.Info.Current) ?? Versions.FirstOrDefault();
    }

    public GameId Id => _info.Id;

    public string Title => _info.Title;

    public string Initial => GsGameTile.InitialOf(_info.Title);

    public IImage? Cover
    {
        get
        {
            if (!_coverRead)
            {
                _coverRead = true;
                _cover = ArtImages.Load(_coverOf(_info.Id), 96);
            }

            return _cover;
        }
    }

    public bool HasCover => Cover is not null;

    public string? Blocked => _info.Blocked;

    public bool IsBlocked => _info.Blocked is not null;

    public bool CanTick => !IsBlocked;

    public IReadOnlyList<ShareVersionRow> Versions { get; }

    public ShareVersionRow? Latest { get; }

    [ObservableProperty]
    private bool _expanded;

    public string ExpandIcon => Expanded ? "chevronDown" : "chevronRight";

    public bool ShowsVersions => Expanded && !IsBlocked;

    public int TickedCount => Versions.Count(v => v.Ticked);

    /// <summary>Ticking takes its latest save; unticking leaves none.</summary>
    public bool Ticked
    {
        get => TickedCount > 0;
        set
        {
            if (IsBlocked || value == Ticked)
            {
                return;
            }

            _quiet = true;
            if (value && Latest is { } latest)
            {
                latest.Ticked = true;
            }
            else
            {
                foreach (var version in Versions)
                {
                    version.Ticked = false;
                }
            }

            _quiet = false;
            Refresh();
        }
    }

    public long Bytes => Versions.Where(v => v.Ticked).Sum(v => v.Info.Bytes);

    /// <summary>"1 of 3 versions" once ticked, "latest 21:04 today" before; a locked game, why.</summary>
    public string Meta => IsBlocked ? Blocked! : Ticked
        ? $"{TickedCount} of {Versions.Count} {(Versions.Count == 1 ? "version" : "versions")}"
        : Latest is { } latest ? $"latest {char.ToLowerInvariant(latest.When[0])}{latest.When[1..]}" : "no versions";

    public string Size => IsBlocked ? "" : Cli.FormatSize(Ticked ? Bytes : Latest?.Info.Bytes ?? 0);

    [RelayCommand]
    private void Expand() => Expanded = !Expanded;

    partial void OnExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandIcon));
        OnPropertyChanged(nameof(ShowsVersions));
    }

    /// <summary>Ticks exactly these versions, for Export (KAN-23) and Share all.</summary>
    public void TickOnly(IEnumerable<VersionId> versions)
    {
        if (IsBlocked)
        {
            return;
        }

        var wanted = versions.ToHashSet();
        _quiet = true;
        foreach (var version in Versions)
        {
            version.Ticked = wanted.Contains(version.Info.Id);
        }

        _quiet = false;
        Refresh();
    }

    private void VersionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_quiet && e.PropertyName == nameof(ShareVersionRow.Ticked))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Ticked));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(Size));
        _changed();
    }

    public override string ToString() => IsBlocked ? $"{Title}, can't be shared: {Blocked}" : $"{Title}, {Meta}, {Size}";
}

/// <summary>
/// The share window (design system → ShareSavesDialog; SHARE-01 to SHARE-09): Choose saves ticks games, each taking its
/// latest save, or opens one to pick exact versions; Share all ticks the current save of every game that can be shared.
/// The foot counts games, versions and the size as they change, with the zip's name; Create zip packs it, with progress
/// and Cancel, then says where it is.
/// </summary>
public sealed partial class ShareViewModel : ObservableObject
{
    private readonly ShareActions _actions;
    private readonly ShareStart _start;
    private CancellationTokenSource? _packing;
    private (string Folder, bool Ask) _where;

    public ShareViewModel(ShareActions actions, ShareStart start)
    {
        _actions = actions;
        _start = start;
        _mode = start.Mode == ShareStart.All ? ShareStart.All : ShareStart.Pick;
    }

    public IReadOnlyList<NavItem> Modes { get; } = [new(ShareStart.Pick, "Choose saves"), new(ShareStart.All, "Share all")];

    /// <summary>loading, choose, packing, done.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Loading), nameof(Choosing), nameof(Packing), nameof(Done), nameof(CanCreate))]
    private string _stage = "loading";

    public bool Loading => Stage == "loading";

    public bool Choosing => Stage == "choose";

    public bool Packing => Stage == "packing";

    public bool Done => Stage == "done";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string _mode;

    /// <summary>Under the title: Share all packs every game's current save (SHARE-04).</summary>
    public string Subtitle => Mode == ShareStart.All ? "Every game's current save, packed into one zip." : "Pick games, or open a game to pick exact versions.";

    [ObservableProperty]
    private IReadOnlyList<ShareGameRow> _games = [];

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _sizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private int _tickedVersions;

    public bool CanCreate => Choosing && TickedVersions > 0;

    /// <summary>Share all: the games left out because they can't be shared (SHARE-07).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeftOut))]
    private string? _leftOut;

    public bool HasLeftOut => LeftOut is not null;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string? _zipPath;

    [ObservableProperty]
    private string _doneText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private IReadOnlyList<string> _notes = [];

    public bool HasNotes => Notes.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>Asks where to save the zip, when Settings says to ask each time (FOLD-09); the page opens Windows' own picker. Null keeps the folder.</summary>
    public Func<string, Task<string?>>? PickZipPath { get; set; }

    /// <summary>Copy path: the page puts it on the clipboard.</summary>
    public event Action<string>? CopyText;

    public async void Load()
    {
        try
        {
            var now = DateTime.Now;
            _where = _actions.Where();
            var games = await _actions.List(CancellationToken.None);
            Games = games.Select(g => new ShareGameRow(g, _actions.CoverOf, Recount, now)).ToList();
            if (_start.Game is { } game && _start.Version is { } version && Games.FirstOrDefault(g => g.Id == game) is { } row)
            {
                row.TickOnly([version]);
                row.Expanded = true;
            }
            else
            {
                foreach (var picked in Games.Where(g => _start.Games.Contains(g.Id)))
                {
                    picked.Ticked = true;
                }
            }

            Stage = "choose";
            if (Mode == ShareStart.All)
            {
                ShareAll();
            }

            Recount();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Error = $"GameSync couldn't read your saves: {e.Message}";
            Stage = "choose";
        }
    }

    partial void OnModeChanged(string value)
    {
        if (value == ShareStart.All && Choosing)
        {
            ShareAll();
        }
        else
        {
            LeftOut = null;
        }
    }

    /// <summary>SHARE-04: the current save of every game that can be shared; the locked ones are named as left out.</summary>
    private void ShareAll()
    {
        foreach (var game in Games.Where(g => !g.IsBlocked))
        {
            game.TickOnly(game.Latest is { } latest ? [latest.Info.Id] : []);
        }

        var locked = Games.Where(g => g.IsBlocked).Select(g => g.Title).ToList();
        LeftOut = locked.Count == 0 ? null : $"Left out, since they can't be shared: {string.Join(", ", locked)}.";
    }

    private void Recount()
    {
        var games = Games.Count(g => g.Ticked);
        TickedVersions = Games.Sum(g => g.TickedCount);
        var name = _where.Ask ? "a name you choose" : Path.GetFileName(Sharing.ZipPathIn(_where.Folder, DateTime.Now));
        Summary = $"{Count(games, "game")} · {Count(TickedVersions, "version")} → {name}";
        SizeText = Cli.FormatSize(Games.Sum(g => g.Bytes));
    }

    /// <summary>Create zip (SHARE-09): where it goes, then packing with progress; done, where it is.</summary>
    [RelayCommand]
    private async Task Create()
    {
        if (!CanCreate)
        {
            return;
        }

        Error = null;
        var (folder, ask) = _actions.Where();
        var path = Sharing.ZipPathIn(folder, DateTime.Now);
        if (ask)
        {
            if (PickZipPath is null || await PickZipPath(Path.GetFileName(path)) is not { } picked)
            {
                return;
            }

            if (File.Exists(picked))
            {
                Error = $"{Path.GetFileName(picked)} is there already; GameSync never writes over a file. Pick another name.";
                return;
            }

            path = picked;
        }

        var picks = Games.Where(g => g.Ticked).Select(g => new SharePick(g.Id, g.Versions.Where(v => v.Ticked).Select(v => v.Info.Id).ToList())).ToList();
        _packing = new CancellationTokenSource();
        Stage = "packing";
        Progress = 0;
        ProgressText = "Getting your saves ready…";
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            Progress = p.Total == 0 ? 100 : p.Done * 100.0 / p.Total;
            ProgressText = $"Packing {Cli.FormatSize(p.Done)} of {Cli.FormatSize(p.Total)}";
        });
        try
        {
            var pack = await _actions.Pack(picks, path, progress, _packing.Token);
            ZipPath = pack.ZipPath;
            DoneText = $"{Count(pack.Versions, "save")} of {Count(pack.Games, "game")}, {Cli.FormatSize(pack.Bytes)}, in {Path.GetFileName(pack.ZipPath)}.";
            Notes = pack.Notes;
            Stage = "done";
        }
        catch (OperationCanceledException)
        {
            Stage = "choose";
            Error = "Cancelled; no zip was left behind.";
        }
        catch (Exception e)
        {
            Stage = "choose";
            Error = e.Message;
        }
        finally
        {
            _packing.Dispose();
            _packing = null;
        }
    }

    [RelayCommand]
    private void CancelPacking() => _packing?.Cancel();

    [RelayCommand]
    private void Close()
    {
        _packing?.Cancel();
        _actions.Close();
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (ZipPath is { } path)
        {
            CopyText?.Invoke(path);
        }
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ZipPath is { } path)
        {
            _actions.ShowInFolder(path);
        }
    }

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count.ToString(CultureInfo.InvariantCulture)} {what}s";
}

/// <summary>A game in a shared zip, as Import saves shows it: how it meets this PC, and anything to say (SHARE-10 to SHARE-13).</summary>
public sealed partial class ImportRow(ImportGamePreview game, string? coverPath) : ObservableObject
{
    private IImage? _cover;
    private bool _coverRead;

    public ImportGamePreview Game => game;

    public string Title => game.Title;

    public string Initial => GsGameTile.InitialOf(game.Title);

    public IImage? Cover
    {
        get
        {
            if (!_coverRead)
            {
                _coverRead = true;
                _cover = ArtImages.Load(coverPath, 96);
            }

            return _cover;
        }
    }

    public bool HasCover => Cover is not null;

    public bool CanTick => game.Match is not (ImportMatch.Unknown or ImportMatch.Blocked);

    [ObservableProperty]
    private bool _ticked = game.Match is not (ImportMatch.Unknown or ImportMatch.Blocked);

    public string Meta => game.Match switch
    {
        ImportMatch.Matched => $"{(game.Versions == 1 ? "1 version" : $"{game.Versions} versions")} · added as pinned, not current",
        ImportMatch.NotSyncing => $"{(game.Versions == 1 ? "1 version" : $"{game.Versions} versions")} · its saves are kept from now on, backed up only",
        ImportMatch.NotInstalled => $"{(game.Versions == 1 ? "1 version" : $"{game.Versions} versions")} · not installed here, waits until the game is found",
        ImportMatch.Blocked => "Can't be imported",
        _ => "Not in your library · can't be imported",
    };

    public string Size => CanTick ? Cli.FormatSize(game.Bytes) : "";

    public string? Warn => game.Match == ImportMatch.Blocked ? null : game.Warn;

    public bool HasWarn => Warn is not null;

    /// <summary>R16 (design system version 51): why its saves stay with the account that made them, shown with the shield in danger.</summary>
    public string? StaysWith => game.Match == ImportMatch.Blocked ? game.Warn : null;

    public bool HasStaysWith => StaysWith is not null;

    public string? Removed => game.Programs switch
    {
        0 => null,
        1 => "1 program file is left out.",
        var n => $"{n} program files are left out.",
    };

    public bool HasRemoved => Removed is not null;

    public override string ToString() => string.Join(". ", new[] { Title, Meta, Warn, StaysWith, Removed }.OfType<string>());
}

/// <summary>
/// Import saves (design system → ImportSavesDialog; SHARE-10 to SHARE-13): a shared zip's games, each matched to this PC's
/// library, ticked unless GameSync doesn't know it; Add puts their saves in the games' histories, pinned, never current.
/// </summary>
public sealed partial class ImportSavesViewModel(ShareActions actions, string zipPath) : ObservableObject
{
    public string ZipName => Path.GetFileName(zipPath);

    /// <summary>reading, review, importing, done.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Reading), nameof(Reviewing), nameof(Importing), nameof(Done), nameof(CanImport))]
    private string _stage = "reading";

    public bool Reading => Stage == "reading";

    public bool Reviewing => Stage == "review";

    public bool Importing => Stage == "importing";

    public bool Done => Stage == "done";

    [ObservableProperty]
    private string _from = "";

    [ObservableProperty]
    private IReadOnlyList<ImportRow> _rows = [];

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _sizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private int _tickedVersions;

    public bool CanImport => Reviewing && TickedVersions > 0;

    public string AddLabel => TickedVersions == 1 ? "Add 1 version" : $"Add {TickedVersions} versions";

    [ObservableProperty]
    private string _doneText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    public async void Load()
    {
        try
        {
            var preview = await actions.Preview(zipPath, CancellationToken.None);
            From = $"From {ZipName} · packed on {preview.PackedUtc.ToLocalTime():d MMM yyyy} on {preview.PackedOn}";
            Rows = preview.Games.Select(g => new ImportRow(g, g.LocalId is { } id ? actions.CoverOf(id) : null)).ToList();
            foreach (var row in Rows)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ImportRow.Ticked))
                    {
                        Recount();
                    }
                };
            }

            Stage = "review";
            Recount();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            From = $"From {ZipName}";
            Error = e.Message;
            Stage = "review";
        }
    }

    private void Recount()
    {
        var ticked = Rows.Where(r => r.Ticked && r.CanTick).ToList();
        TickedVersions = ticked.Sum(r => r.Game.Versions);
        Summary = $"{(ticked.Count == 1 ? "1 game" : $"{ticked.Count} games")} · {(TickedVersions == 1 ? "1 version" : $"{TickedVersions} versions")}, added as pinned versions";
        SizeText = Cli.FormatSize(ticked.Sum(r => r.Game.Bytes));
        OnPropertyChanged(nameof(AddLabel));
    }

    [RelayCommand]
    private async Task Add()
    {
        if (!CanImport)
        {
            return;
        }

        Error = null;
        Stage = "importing";
        var outcome = await actions.Import(zipPath, Rows.Where(r => r.Ticked && r.CanTick).Select(r => r.Game.Id).ToList());
        if (outcome.Failed)
        {
            Error = outcome.Text;
            Stage = "review";
            return;
        }

        DoneText = outcome.Text;
        Stage = "done";
    }

    [RelayCommand]
    private void Close() => actions.Close();
}
