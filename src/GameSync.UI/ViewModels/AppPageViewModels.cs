using System.Collections.ObjectModel;
using System.Globalization;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>A screen the window has a place for but that isn't built yet: its name and what it will hold.</summary>
public sealed record PlaceholderViewModel(string Title, string Icon, string Text);

/// <summary>
/// The Console page: the agent's log as it happens, today's lines first, newest at the bottom. It keeps the last
/// 2,000 lines; the day's full log stays in the data folder's <c>logs</c>.
/// </summary>
public sealed class ConsoleViewModel
{
    public const int Keep = 2000;

    public string Title { get; init; } = "Console";

    public string Subtitle { get; init; } = "What GameSync does in the background, as it happens";

    public ObservableCollection<LogLine> Lines { get; } = [];

    /// <summary>A line as the agent writes it; lines that start with "!" need a look.</summary>
    public void Add(DateTime atLocal, string line)
    {
        var warn = line.StartsWith('!');
        Lines.Add(new LogLine(atLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), warn ? LogLevel.Warn : LogLevel.Info, warn ? "note" : "agent",
            warn ? line[1..].TrimStart() : line));
        while (Lines.Count > Keep)
        {
            Lines.RemoveAt(0);
        }
    }

    /// <summary>Today's log file, as <c>HH:mm:ss  line</c>.</summary>
    public void AddFile(IEnumerable<string> fileLines, DateTime day)
    {
        foreach (var fileLine in fileLines)
        {
            if (fileLine.Length > 10 && TimeOnly.TryParseExact(fileLine[..8], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                Add(day.Date + time.ToTimeSpan(), fileLine[10..]);
            }
        }
    }
}
