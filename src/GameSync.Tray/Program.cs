using Avalonia;
using Avalonia.Controls;
using GameSync.Host;
using GameSync.UI;

namespace GameSync.Tray;

internal static class Program
{
    /// <summary>
    /// GameSync's app (design.md → Background work, UI). With no command, the app: its window, the tray icon and the
    /// agent, once per Windows user, and started again it brings the running one's window forward (BG-01). With
    /// <c>--background</c>, as at sign-in, the same with the window closed. With a command, such as <c>daily</c> from
    /// Task Scheduler or <c>launch &lt;game&gt; -- %command%</c> from Steam, that job with no window.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var start = AppStart.Parse(args);
        if (!start.IsApp)
        {
            return Background.RunAsync(args, Toasts.Show).GetAwaiter().GetResult();
        }

        // R17: the app never runs as an administrator: started that way, it says so and closes.
        if (GameSync.Windows.Elevation.IsElevated())
        {
            GameSync.Windows.Elevation.Tell("GameSync", GameSync.Windows.Elevation.Refusal);
            return 1;
        }

        using var claim = AppPipe.TryClaim(start.DataDir);
        if (claim is null)
        {
            // GameSync already runs: its window comes forward, unless this start came from signing in.
            if (start.ShowWindow)
            {
                AppPipe.SendAsync(start.DataDir, "show", TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }

            return 0;
        }

        try
        {
            // KAN-58: how the window is drawn. The CPU keeps GameSync small while it waits in the tray; the GPU scrolls a
            // screen of covers smoothly. GAMESYNC_RENDERING=gpu or software picks one, for measuring both.
            Win32RenderingMode[] rendering = Environment.GetEnvironmentVariable("GAMESYNC_RENDERING") == "gpu"
                ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                : [Win32RenderingMode.Software];
            return AppBuilder.Configure<GsApp>()
                .UseWin32()
                .With(new Win32PlatformOptions { RenderingMode = rendering })
                .UseSkia()
                .UseHarfBuzz()
                .StartWithClassicDesktopLifetime(args, lifetime =>
                {
                    lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    lifetime.Startup += (_, _) => TrayApp.Start(start, lifetime);
                });
        }
        finally
        {
            claim.ReleaseMutex();
        }
    }
}
