using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// The background app's start (<c>GameSync.Tray.exe</c>): with no command, the agent, once per user (BG-01); with a
/// command, such as <c>daily</c> from Task Scheduler or <c>launch &lt;game&gt; -- %command%</c> from Steam, that
/// command, with no window. With no window to print to, what goes wrong goes to the log and a notification.
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
            switch (list.Count == 0 ? "agent" : list[0])
            {
                case "agent":
                    return await RunAgentAsync(dataDir, output);
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

    private static async Task<int> RunAgentAsync(string dataDir, BackgroundOutput output)
    {
        if (!File.Exists(AppConfig.PathIn(dataDir)))
        {
            return 2;
        }

        using var agentLock = EngineLock.TryAcquireAgent(dataDir);
        if (agentLock is null)
        {
            return 0;
        }

        using var agent = new Agent(dataDir, output);
        await agent.RunAsync(CancellationToken.None);
        return 0;
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
            show("GameSync needs you", $"{string.Join(", ", titles.Take(5))}{(titles.Count > 5 ? $" and {titles.Count - 5} more" : "")}. See what each needs with: gamesync games");
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
