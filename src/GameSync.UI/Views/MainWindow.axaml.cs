using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// <summary>How long the next game's light takes to grow over the window (KAN-54).</summary>
    public static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(750);

    private Point? _pressed;
    private DateTime _pressedAtUtc;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(new MemoryStream(MarkArt.Ico([16, 20, 24, 32, 40, 48, 64], MarkArt.AppIcon)));
        Frame.TitleBarHeight = WindowDecorationMargin.Top;

        // Where the person last clicked: a page they opened grows its colours from there (KAN-54).
        AddHandler(PointerPressedEvent, (_, e) => (_pressed, _pressedAtUtc) = (e.GetPosition(this), DateTime.UtcNow),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // For measuring (KAN-58): Avalonia's frame rate and its render and layout times, drawn over the window.
        if (Environment.GetEnvironmentVariable("GAMESYNC_RENDER_STATS") == "1")
        {
            RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.Fps |
                Avalonia.Rendering.RendererDebugOverlays.RenderTimeGraph | Avalonia.Rendering.RendererDebugOverlays.LayoutTimeGraph;
        }
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
        var next = glass is null ? null : backdrop;

        // KAN-54: a new game's colours glow in over the ones before like a light behind frosted glass, unless Windows'
        // animation effects are off (A11Y-04) or there was nothing before (the backdrop still being made).
        if (next is not null && Art.Source is { } before && !ReferenceEquals(before, next) && !ReferenceEquals(Art.GlowingTo, next) && AnimationsOn())
        {
            var size = Bounds.Size;
            var centre = _pressed is { } pressed && DateTime.UtcNow - _pressedAtUtc < TimeSpan.FromSeconds(2) ? pressed : new Point(size.Width / 2, size.Height / 2);
            Art.GlowTo(next, centre, RevealTime);
        }
        else if (!ReferenceEquals(Art.GlowingTo, next) || next is null)
        {
            Art.Source = next;
        }

        if (glass is null)
        {
            ClearValue(BackgroundProperty);
        }
        else
        {
            Background = new SolidColorBrush(Color.Parse(glass.Base));
        }
    }

    private static bool AnimationsOn() => GameSync.UI.Controls.Motion.On;

    /// <summary>For the snapshot tool's benchmark: the glow toward <paramref name="next"/> at <paramref name="t"/> (0 to 1), from the window's middle.</summary>
    public void GlowFrame(IImage next, double t) => Art.GlowFrame(next, t);

    private static int Rgb(string hex) => hex.StartsWith('#') && hex.Length == 7 ? int.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
}
