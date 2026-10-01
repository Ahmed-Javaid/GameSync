using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>The activity log's lines as the console draws them, and the time they're stamped with.</summary>
public static class ActivityLog
{
    /// <summary>A line from the log's level and tag; a line without a tag, from before tags were kept, shows its level.</summary>
    public static LogLine Line(string time, string level, string? tag, string message) => new(time, level switch
    {
        "error" => LogLevel.Error,
        "warn" => LogLevel.Warn,
        "ok" => LogLevel.Ok,
        // A save that moved gets the ok colour, as the design's log shows uploads, downloads and restores.
        _ => tag is not null && EventTags.Moves.Contains(tag) ? LogLevel.Ok : LogLevel.Info,
    }, tag ?? level, message);

    /// <summary>"Today 21:06", "Yesterday 23:31", "27 Sep 18:05", "27 Sep 2025 18:05"; with seconds for the log's lines.</summary>
    public static string Stamp(DateTime utc, DateTime nowLocal, bool seconds = false)
    {
        var local = utc.ToLocalTime();
        var time = local.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        return local.Date == nowLocal.Date ? $"Today {time}"
            : local.Date == nowLocal.Date.AddDays(-1) ? $"Yesterday {time}"
            : local.Year == nowLocal.Year ? $"{local.ToString("d MMM", CultureInfo.InvariantCulture)} {time}"
            : $"{local.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} {time}";
    }

    /// <summary>"DESKTOP", "DESKTOP and LAPTOP", "DESKTOP, LAPTOP and DECK".</summary>
    public static string Names(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    public static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} {many}";
}

/// <summary>A row of the Versions tab: when the save was made, the game, the PC, why it was kept, its files and size.</summary>
public sealed record VersionRow(SavedVersion Version, string Saved)
{
    public string Game => Version.Title;

    public string Pc => Version.Pc.ToUpperInvariant();

    public string Why => Version.Why;

    /// <summary>The design's icon for why it was kept: after play, a quiet folder, the daily clock, the first upload, held, pinned.</summary>
    public string Icon => Version.For switch
    {
        KeptFor.Play => "play",
        KeptFor.Quiet => "folder",
        KeptFor.Daily => "clock",
        KeptFor.First => "upload",
        KeptFor.Held => "pause",
        KeptFor.Kept => "pin",
        KeptFor.Restored => "reset",
        _ => "archive",
    };

    public string Files => Version.Files.ToString("N0", CultureInfo.InvariantCulture);

    public string Size => Cli.FormatSize(Version.Bytes);

    public override string ToString() => $"{Saved}, {Game}, {Pc}, {Why}, {ActivityLog.Count(Version.Files, "file", "files")}, {Size}";
}

/// <summary>
/// The save manager's Versions tab (design system → VersionsScreen, MGR-08): every version of every game from every PC,
/// newest first, filtered by game and by PC, with Named and kept only. A row opens its game's saves with the version
/// picked, where Restore is: this tab only finds a version. The newest 100 show, and Show N older loads the rest.
/// </summary>
public sealed partial class VersionsViewModel : ObservableObject
{
    public const string All = "all";

    /// <summary>How many versions show before Show N older.</summary>
    public const int PageSize = 100;

    private readonly LauncherActions? _actions;
    private VersionsView? _view;
    private DateTime _nowLocal = DateTime.Now;
    private bool _everything;
    private int _loads;

    /// <summary>The game shown: <see cref="All"/>, or a game's ID.</summary>
    [ObservableProperty]
    private string _game = All;

    /// <summary>The PC shown: <see cref="All"/>, or a PC's name.</summary>
    [ObservableProperty]
    private string _pc = All;

    /// <summary>Named and kept only: the versions that stay pinned.</summary>
    [ObservableProperty]
    private bool _keptOnly;

    [ObservableProperty]
    private IReadOnlyList<SelectOption> _games = [new(All, "All games")];

    [ObservableProperty]
    private IReadOnlyList<SelectOption> _pcs = [new(All, "Every PC")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(IsEmpty))]
    private IReadOnlyList<VersionRow> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<SaveStat> _stats = [];

    /// <summary>"DESKTOP · 214 versions of 12 games, from DESKTOP and LAPTOP".</summary>
    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>What a row does, and how much of the history is kept where.</summary>
    [ObservableProperty]
    private string _foot = "";

    /// <summary>Why nothing shows, when a filter matches nothing.</summary>
    [ObservableProperty]
    private string _emptyText = "";

    /// <summary>How many older versions wait behind Show N older.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOlder), nameof(OlderLabel))]
    private int _older;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="open">Opens a game's saves with a version picked.</param>
    public VersionsViewModel(LauncherActions? actions, Action<GameId, VersionId> open)
    {
        _actions = actions;
        OpenCommand = new RelayCommand<VersionRow>(row =>
        {
            if (row is not null)
            {
                open(row.Version.Game, row.Version.Id);
            }
        });
        ShowOlderCommand = new RelayCommand(() =>
        {
            _everything = true;
            Refilter();
        });
    }

    /// <summary>A row: its game's saves, with the version picked.</summary>
    public ICommand OpenCommand { get; }

    public ICommand ShowOlderCommand { get; }

    public bool HasRows => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0 && _view is not null && !IsLoading;

    public bool HasOlder => Older > 0;

    public string OlderLabel => $"Show {Older.ToString("N0", CultureInfo.InvariantCulture)} older";

    public bool HasError => Error is not null;

    /// <summary>Reads the versions again, off the UI thread; the filters and Show N older stay as they were.</summary>
    public async Task LoadAsync()
    {
        if (_actions?.LoadVersions is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        IsLoading = _view is null;
        Error = null;
        try
        {
            var view = await load(CancellationToken.None);
            if (ticket == _loads)
            {
                Show(view, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                Error = $"GameSync couldn't read the versions on this PC: {e.Message}";
            }
        }
        finally
        {
            if (ticket == _loads)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>The versions as read; a game or PC chosen that's gone goes back to all of them.</summary>
    public void Show(VersionsView view, DateTime nowLocal)
    {
        _view = view;
        _nowLocal = nowLocal;
        Games = [new(All, "All games"), .. view.Games.Select(g => new SelectOption(g.Id.Value, g.Title))];
        Pcs = [new(All, "Every PC"), .. view.Pcs.Select(p => new SelectOption(p, p))];
        if (Games.All(o => o.Id != Game))
        {
            Game = All;
        }

        if (Pcs.All(o => !string.Equals(o.Id, Pc, StringComparison.OrdinalIgnoreCase)))
        {
            Pc = All;
        }

        var from = view.Versions.Select(v => v.Pc).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Subtitle = view.Versions.Count == 0
            ? $"{view.ThisPc} · No versions yet"
            : $"{view.ThisPc} · {ActivityLog.Count(view.Versions.Count, "version", "versions")} of {ActivityLog.Count(view.Versions.Select(v => v.Game).Distinct().Count(), "game", "games")}, from {ActivityLog.Names(from)}";
        Foot = "A row opens its game's saves, where Restore is. " + (!view.HasCloud
            ? "Every version is kept on this PC until you connect a cloud."
            : view.KeptOnPc is { } kept
                ? $"Every version is kept: all of them in the cloud, and the last {kept.ToString(CultureInfo.InvariantCulture)} of each game on this PC."
                : "Every version is kept, in the cloud and on this PC.");
        Refilter();
    }

    partial void OnGameChanged(string value) => Refilter(fresh: true);

    partial void OnPcChanged(string value) => Refilter(fresh: true);

    partial void OnKeptOnlyChanged(bool value) => Refilter(fresh: true);

    private void Refilter(bool fresh = false)
    {
        if (_view is null)
        {
            return;
        }

        if (fresh)
        {
            _everything = false;
        }

        var chosen = _view.Versions
            .Where(v => (Game == All || v.Game.Value == Game) && (Pc == All || string.Equals(v.Pc, Pc, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var shown = KeptOnly ? chosen.Where(v => v.Kept).ToList() : chosen;
        var take = _everything ? shown.Count : Math.Min(PageSize, shown.Count);
        Rows = shown.Take(take).Select(v => new VersionRow(v, ActivityLog.Stamp(v.SavedUtc, _nowLocal))).ToList();
        Older = shown.Count - take;

        // The numbers are the game's and PC's chosen, whatever Named and kept only hides.
        var weekAgo = _nowLocal.ToUniversalTime().AddDays(-7);
        Stats =
        [
            new SaveStat("Versions", chosen.Count.ToString("N0", CultureInfo.InvariantCulture)),
            new SaveStat("Named", chosen.Count(v => v.Named).ToString("N0", CultureInfo.InvariantCulture)),
            new SaveStat("Kept", chosen.Count(v => v.Kept).ToString("N0", CultureInfo.InvariantCulture)),
            new SaveStat("This week", chosen.Count(v => v.CreatedUtc >= weekAgo).ToString("N0", CultureInfo.InvariantCulture)),
            new SaveStat("Oldest", chosen.Count == 0 ? "None yet" : GameDetails.Day(chosen.Min(v => v.SavedUtc))),
        ];

        var title = _view.Games.FirstOrDefault(g => g.Id.Value == Game)?.Title ?? Game;
        EmptyText = _view.Versions.Count == 0 ? "No versions yet: each game's first backup shows here."
            : chosen.Count > 0 ? $"Nothing named or kept{(Game == All ? "" : $" for {title}")}{(Pc == All ? "" : $" from {Pc}")}. Named saves, and the saves kept before an update, a restore or after a conflict, show here."
            : Game != All && _view.Versions.All(v => v.Game.Value != Game) ? $"No version of {title} yet: it hasn't synced since it was added."
            : Game != All ? $"No version of {title} came from {Pc}."
            : $"No version came from {Pc} yet.";
    }
}

/// <summary>
/// The save manager's Log tab (design system → LogScreen, MGR-09): everything GameSync did, per game, newest first and
/// kept for good, with a search that matches as the library's does, a game filter, and what to show. Copy the log puts
/// the lines shown on the clipboard for a bug report, never sign-in tokens or save files (SET-05).
/// </summary>
public sealed partial class LogViewModel : ObservableObject
{
    public const string All = "all";

    private readonly LauncherActions? _actions;
    private IReadOnlyList<LogEntry> _entries = [];
    private IReadOnlyList<LogEntry> _shown = [];
    private DateTime _nowLocal = DateTime.Now;
    private bool _loaded;
    private int _loads;

    [ObservableProperty]
    private string _search = "";

    /// <summary>The game shown: <see cref="All"/>, or a game's ID.</summary>
    [ObservableProperty]
    private string _game = All;

    /// <summary>What shows: <see cref="All"/>, <c>needs</c> (warnings and errors) or <c>moves</c> (uploads, downloads and restores).</summary>
    [ObservableProperty]
    private string _showing = All;

    [ObservableProperty]
    private IReadOnlyList<SelectOption> _games = [new(All, "All games")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLines), nameof(NoLines))]
    private IReadOnlyList<LogLine> _lines = [];

    /// <summary>"10 of 1,284 lines".</summary>
    [ObservableProperty]
    private string _countText = "";

    [ObservableProperty]
    private string _emptyText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoLines))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public LogViewModel(LauncherActions? actions) => _actions = actions;

    public IReadOnlyList<SelectOption> ShowOptions { get; } =
        [new(All, "Everything"), new("needs", "Only what needs you"), new("moves", "Uploads, downloads and restores")];

    public bool HasLines => Lines.Count > 0;

    public bool NoLines => Lines.Count == 0 && _loaded && !IsLoading;

    public bool HasError => Error is not null;

    /// <summary>Reads the log again, off the UI thread; the search and filters stay as they were.</summary>
    public async Task LoadAsync()
    {
        if (_actions?.LoadLog is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        IsLoading = !_loaded;
        Error = null;
        try
        {
            var entries = await load(CancellationToken.None);
            if (ticket == _loads)
            {
                Show(entries, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                Error = $"GameSync couldn't read its log on this PC: {e.Message}";
            }
        }
        finally
        {
            if (ticket == _loads)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>The log as read, newest first; a game chosen that's no longer in it goes back to all of them.</summary>
    public void Show(IReadOnlyList<LogEntry> entries, DateTime nowLocal)
    {
        _entries = entries;
        _nowLocal = nowLocal;
        _loaded = true;
        Games =
        [
            new(All, "All games"),
            .. entries.DistinctBy(e => e.Game).OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).Select(e => new SelectOption(e.Game.Value, e.Title)),
        ];
        if (Games.All(o => o.Id != Game))
        {
            Game = All;
        }

        Refilter();
    }

    /// <summary>The lines shown, for a bug report: each with its full date, and never a sign-in token (SET-05).</summary>
    public string CopyText() => string.Join(Environment.NewLine, _shown.Select(e =>
        $"{e.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {(e.Tag ?? e.Level).ToUpperInvariant(),-8}  {SettingsData.Scrub(Said(e))}"));

    /// <summary>A line as the Log tab says it: the game's name first, since every game shares the log.</summary>
    public static string Said(LogEntry entry) =>
        entry.Message.StartsWith(entry.Title, StringComparison.OrdinalIgnoreCase) ? entry.Message : $"{entry.Title}: {entry.Message}";

    partial void OnSearchChanged(string value) => Refilter();

    partial void OnGameChanged(string value) => Refilter();

    partial void OnShowingChanged(string value) => Refilter();

    private void Refilter()
    {
        _shown = _entries.Where(e =>
                (Game == All || e.Game.Value == Game) &&
                Showing switch
                {
                    "needs" => e.Level is "warn" or "error",
                    "moves" => e.Tag is not null && EventTags.Moves.Contains(e.Tag),
                    _ => true,
                } &&
                Launcher.Matches($"{e.Title} {e.Message}", Search))
            .ToList();
        Lines = _shown.Select(e => ActivityLog.Line(ActivityLog.Stamp(e.AtUtc, _nowLocal, seconds: true), e.Level, e.Tag, Said(e))).ToList();
        CountText = $"{_shown.Count.ToString("N0", CultureInfo.InvariantCulture)} of {ActivityLog.Count(_entries.Count, "line", "lines")}";
        EmptyText = _entries.Count == 0
            ? "Nothing in the log yet: every sync, restore and named save adds a line here."
            : "Nothing in the log matches. Try fewer words, or Everything.";
    }
}
