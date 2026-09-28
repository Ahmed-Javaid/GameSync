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
            return AppBuilder.Configure<GsApp>()
                .UseWin32()
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
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
