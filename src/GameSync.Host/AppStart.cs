namespace GameSync.Host;

/// <summary>
/// How <c>GameSync.Tray.exe</c> was started. With no command it's the app, with its window; with <c>--background</c>,
/// as the Run key starts it at sign-in, the app in the tray with its window closed. Any command, such as <c>daily</c>
/// from Task Scheduler, <c>launch &lt;game&gt; -- %command%</c> from Steam or <c>--link gamesync://…</c> from a link
/// (R12), is a job with no window.
/// </summary>
public sealed record AppStart(string DataDir, bool IsApp, bool ShowWindow)
{
    public static AppStart Parse(IReadOnlyList<string> args)
    {
        var list = args.ToList();
        var dataDir = Path.GetFullPath(Cli.TakeOption(list, "--data") ?? Engine.DefaultDataDir);
        var background = list.Remove("--background");

        // "agent" is how Milestone 4's sign-in task started the app with no window.
        if (list is [] or ["agent"])
        {
            return new AppStart(dataDir, IsApp: true, ShowWindow: !background && list.Count == 0);
        }

        return new AppStart(dataDir, IsApp: false, ShowWindow: false);
    }
}
