using System.Globalization;
using Avalonia.Controls;
using GameSync.UI.Branding;
using GameSync.Windows;

namespace GameSync.UI.Views;

/// <summary>
/// GameSync's window. Closing it leaves GameSync running in the tray with its agent watching the games; the window and
/// its pictures are let go, so GameSync stays small while it waits (PERF-01), and the tray icon opens a new one.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(new MemoryStream(MarkArt.Ico([16, 20, 24, 32, 40, 48, 64], MarkArt.AppIcon)));
    }

    /// <summary>Shows the window and brings it to the front, as a click on the tray icon or a second start asks.</summary>
    public void Bring()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>The title bar in the theme's ground and ink (Windows 11), or Windows' own dark or light bar (Windows 10).</summary>
    public void PaintFrame(IReadOnlyDictionary<string, string> tokens, bool dark)
    {
        if (TryGetPlatformHandle()?.Handle is { } window && tokens.TryGetValue("bg-100", out var ground) && tokens.TryGetValue("ink", out var ink))
        {
            WindowFrame.Paint(window, dark, Rgb(ground), Rgb(ink));
        }
    }

    private static int Rgb(string hex) => hex.StartsWith('#') && hex.Length == 7 ? int.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
}
