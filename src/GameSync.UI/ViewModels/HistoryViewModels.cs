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
