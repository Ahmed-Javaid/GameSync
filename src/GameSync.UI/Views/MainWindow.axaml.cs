using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using GameSync.UI.Branding;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;
using GameSync.Windows;
using SkiaSharp;

namespace GameSync.UI.Views;

/// <summary>
/// GameSync's window. Closing it leaves GameSync running in the tray with its agent watching the games; the window and
/// its pictures are let go, so GameSync stays small while it waits (PERF-01), and the tray icon opens a new one.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>How long the next game's light takes to grow over the window (KAN-54).</summary>
    public static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(750);

    /// <summary>How long a new look takes to come through from a click (KAN-76), and to fade in when Windows made the change.</summary>
    public static readonly TimeSpan LookRevealTime = TimeSpan.FromMilliseconds(600);

    public static readonly TimeSpan LookFadeTime = TimeSpan.FromMilliseconds(400);

    /// <summary>How long Glossy and Solid take to turn into each other, evenly over the whole window (the owner, 1 Oct).</summary>
    public static readonly TimeSpan SurfaceFadeTime = TimeSpan.FromMilliseconds(500);

    private Point? _pressed;
    private DateTime _pressedAtUtc;
    private bool _changingLook;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(new MemoryStream(MarkArt.Ico([16, 20, 24, 32, 40, 48, 64], MarkArt.AppIcon)));
        Frame.TitleBarHeight = WindowDecorationMargin.Top;

        // Where the person last clicked: a page they opened grows its colours from there (KAN-54).
        AddHandler(PointerPressedEvent, (_, e) => (_pressed, _pressedAtUtc) = (e.GetPosition(this), DateTime.UtcNow),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // SHARE-10: a shared zip dropped anywhere on the window opens Import saves for it, as the save manager's picker does.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = TakesDrops && ZipIn(e) is not null ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (TakesDrops && DropZip is { } open && ZipIn(e) is { } zip)
            {
                e.Handled = true;
                open(zip);
            }
        });

        // For measuring (KAN-58): Avalonia's frame rate and its render and layout times, drawn over the window; "dirty" also
        // outlines what each frame repaints.
        if (Environment.GetEnvironmentVariable("GAMESYNC_RENDER_STATS") is "1" or "dirty")
        {
            RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.Fps |
                Avalonia.Rendering.RendererDebugOverlays.RenderTimeGraph | Avalonia.Rendering.RendererDebugOverlays.LayoutTimeGraph |
                (Environment.GetEnvironmentVariable("GAMESYNC_RENDER_STATS") == "dirty" ? Avalonia.Rendering.RendererDebugOverlays.DirtyRects : 0);
        }
    }

    /// <summary>SHARE-10: opens Import saves for a shared zip dropped on the window; null while nothing takes one.</summary>
    public Action<string>? DropZip { get; set; }

    /// <summary>A drop opens Import saves only over a page of the app, never over another window's work or first run.</summary>
    private bool TakesDrops => DropZip is not null && DataContext is ShellViewModel { HasDialog: false, ShowsRail: true };

    /// <summary>The one zip a drop holds, as a path on this PC: one file ending in .zip, and nothing else with it.</summary>
    public static string? ZipOf(IReadOnlyList<string?> paths) =>
        paths is [{ } path] && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : null;

    private static string? ZipIn(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { } items ? ZipOf(items.Select(i => i is IStorageFile ? i.TryGetLocalPath() : null).ToList()) : null;

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

    /// <summary>
    /// KAN-76: changes the look (Dark or Light, pure black, a preset, the surface) without a jump: the window as it looked
    /// stays over the new look as a picture, and the new look comes through it from where the person clicked, or fades in
    /// when Windows made the change. Hidden, minimized, or with Windows' animation effects off, it changes at once.
    /// </summary>
    /// <param name="evenly">Only Glossy or Solid changed: the window turns into the other evenly, all at once.</param>
    public void ChangeLook(Action apply, bool evenly = false)
    {
        var before = IsVisible && WindowState != WindowState.Minimized && AnimationsOn() ? Snapshot() : null;
        _changingLook = true;
        try
        {
            apply();
        }
        finally
        {
            _changingLook = false;
        }

        if (before is { } picture)
        {
            var from = !evenly && _pressed is { } pressed && DateTime.UtcNow - _pressedAtUtc < TimeSpan.FromSeconds(2) ? pressed : (Point?)null;
            Curtain.Show(picture.Image, picture.Size, from, evenly ? SurfaceFadeTime : from is null ? LookFadeTime : LookRevealTime);
        }
    }

    /// <summary>For the snapshot tool: a change of look held part way, at <paramref name="t"/> (0 to 1), from <paramref name="from"/>.</summary>
    public void ChangeLookFrame(Action apply, Point? from, double t)
    {
        var before = Snapshot();
        apply();
        if (before is { } picture)
        {
            Curtain.Hold(picture.Image, picture.Size, from, t);
        }
    }

    /// <summary>The window as it looks now, background and all, at its pixel size; null when it can't be drawn.</summary>
    private (SKImage Image, PixelSize Size)? Snapshot()
    {
        var scaling = RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(this);
        var buffer = new byte[size.Width * size.Height * 4];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
            var colour = bitmap.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            return SKImage.FromPixelCopy(new SKImageInfo(size.Width, size.Height, colour, SKAlphaType.Premul), pinned.AddrOfPinnedObject(), size.Width * 4) is { } image
                ? (image, size)
                : null;
        }
        finally
        {
            pinned.Free();
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
        // animation effects are off (A11Y-04), there was nothing before (the backdrop still being made), or the whole look
        // is changing, which comes through by itself (KAN-76).
        if (next is not null && Art.Source is { } before && !ReferenceEquals(before, next) && !ReferenceEquals(Art.GlowingTo, next) && AnimationsOn() && !_changingLook)
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
