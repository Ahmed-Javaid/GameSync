using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GameSync.UI.Branding;
using GameSync.UI.Theming;
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
        Frame.TitleBarHeight = WindowDecorationMargin.Top;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The page starts below the title bar's strip, however tall Windows makes it. The margin first changes while the
        // window loads, before its frame exists.
        if (change.Property == WindowDecorationMarginProperty && Frame is not null)
        {
            Frame.TitleBarHeight = WindowDecorationMargin.Top;
        }
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

    /// <summary>
    /// Glossy (LOOK-17): the page and the rail take the strength's see-through surfaces over <paramref name="backdrop"/>,
    /// with the backdrop's base colour showing while it isn't ready; null goes back to Solid.
    /// </summary>
    public void ShowSurface(GlassSurface? glass, IImage? backdrop)
    {
        ThemeService.Scope(Frame.Resources, glass?.Tokens);
        Art.Source = glass is null ? null : backdrop;
        if (glass is null)
        {
            ClearValue(BackgroundProperty);
        }
        else
        {
            Background = new SolidColorBrush(Color.Parse(glass.Base));
        }
    }

    private static int Rgb(string hex) => hex.StartsWith('#') && hex.Length == 7 ? int.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
}
