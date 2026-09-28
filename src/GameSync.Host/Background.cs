using GameSync.Core.Model;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// The app's jobs with no window (<c>GameSync.Tray.exe</c> with a command): <c>daily</c> from Task Scheduler,
/// <c>launch &lt;game&gt; -- %command%</c> from Steam, and any other command as the command line runs it. With no window
/// to print to, what goes wrong goes to the log and a notification.
/// </summary>
public static class Background
{
    /// <param name="notify">Shows a Windows notification: a title and a message.</param>
    public static async Task<int> RunAsync(string[] args, Action<string, string> notify)
    {
        var list = args.ToList();
        var dataDir = Cli.TakeOption(list, "--data") ?? Engine.DefaultDataDir;
        var output = new BackgroundOutput(dataDir, notify);
        try
        {
            switch (list.Count == 0 ? "" : list[0])
            {
                case "daily":
                    return await Cli.DailyAsync(dataDir, list.Skip(1).ToList(), output);
                case "launch":
                    return await Cli.LaunchAsync(dataDir, list.Skip(1).ToList(), output);
                default:
                    return await Cli.RunAsync(args);
            }
        }
        catch (UsageException e)
        {
            output.NeedsYou("GameSync", e.Message);
            return 2;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The last stop before the program ends: the whole story goes to the log.
            output.Say($"! {e}");
            output.NeedsYou("GameSync stopped", e.Message);
            return 1;
        }
    }
}

/// <summary>
/// The background app's output: a log file a day in the data folder's <c>logs</c>, and a notification for each thing
/// that needs the person, held while a fullscreen game runs (BG-05, BG-06).
/// </summary>
internal sealed class BackgroundOutput(string dataDir, Action<string, string> notify) : IAgentOutput
{
    private readonly NotificationQueue _notifications = new(FullScreen.IsBusy, notify);

    public void Say(string line)
    {
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(dataDir, "logs")).FullName;
            File.AppendAllText(Path.Combine(logs, $"agent-{DateTime.Now:yyyy-MM-dd}.log"), $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The activity log in state.db has the same news.
        }
    }

    public void NeedsYou(string title, string message)
    {
        Say($"! {title}: {message}");
        _notifications.Add(title, message);
    }

    public void Fine(string title) => _notifications.Forget(title);

    public void Flush() => _notifications.Flush();
}

/// <summary>
/// The agent's output inside GameSync's app: the background app's log file and notifications, and events the window
/// and the tray icon follow (BG-07). Events come on the agent's thread; the app moves them to its own.
/// </summary>
public sealed class AppOutput(string dataDir, Action<string, string> notify) : IAgentOutput
{
    private readonly BackgroundOutput _inner = new(dataDir, notify);

    /// <summary>A line of the agent's log, as it's written.</summary>
    public event Action<string>? Line;

    /// <summary>The agent started (true) or finished (false) work on saves or the cloud.</summary>
    public event Action<bool>? WorkingChanged;

    /// <summary>A sync finished, with each game's result.</summary>
    public event Action<IReadOnlyList<GameResult>>? SyncFinished;

    /// <summary>A game started (true) or stopped (false) being played.</summary>
    public event Action<GameId, string, bool>? PlayChanged;

    public void Say(string line)
    {
        lock (_inner)
        {
            _inner.Say(line);
        }

        Line?.Invoke(line);
    }

    public void NeedsYou(string title, string message)
    {
        lock (_inner)
        {
            _inner.NeedsYou(title, message);
        }

        Line?.Invoke($"! {title}: {message}");
    }

    public void Fine(string title)
    {
        lock (_inner)
        {
            _inner.Fine(title);
        }
    }

    public void Flush()
    {
        lock (_inner)
        {
            _inner.Flush();
        }
    }

    public void Working(bool busy) => WorkingChanged?.Invoke(busy);

    public void Synced(IReadOnlyList<GameResult> results) => SyncFinished?.Invoke(results);

    public void Played(GameId game, string title, bool playing) => PlayChanged?.Invoke(game, title, playing);
}

/// <summary>
/// BG-05, BG-06: notifications for what needs the person, held while a fullscreen game (or a presentation) runs and
/// shown once it closes. Each is shown once while its problem lasts; more than three at a time become one.
/// </summary>
internal sealed class NotificationQueue(Func<bool> busy, Action<string, string> show)
{
    public const int OneByOne = 3;

    private readonly List<(string Title, string Message)> _held = [];
    private readonly HashSet<(string Title, string Message)> _shown = [];

    public void Add(string title, string message)
    {
        if (!_shown.Contains((title, message)) && !_held.Contains((title, message)))
        {
            _held.Add((title, message));
        }

        Flush();
    }

    /// <summary>What <paramref name="title"/> needed the person for is over: held news of it goes, and its next problem shows.</summary>
    public void Forget(string title)
    {
        _held.RemoveAll(n => n.Title == title);
        _shown.RemoveWhere(n => n.Title == title);
    }

    public void Flush()
    {
        if (_held.Count == 0 || busy())
        {
            return;
        }

        if (_held.Count > OneByOne)
        {
            var titles = _held.Select(n => n.Title).Distinct().ToList();
            show("GameSync needs you", $"{string.Join(", ", titles.Take(5))}{(titles.Count > 5 ? $" and {titles.Count - 5} more" : "")}. Open GameSync to see what each needs.");
        }
        else
        {
            foreach (var (title, message) in _held)
            {
                show(title, message);
            }
        }

        _shown.UnionWith(_held);
        _held.Clear();
    }
}
