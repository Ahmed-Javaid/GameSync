using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace GameSync.UI.Controls;

/// <summary>
/// The window's Glossy backdrop (LOOK-17): its small blurred picture is prepared once at the window's own pixel size
/// and after that only copied, since stretching it across the window on the CPU every frame cost about 35 ms (KAN-58).
/// It's prepared again when the picture or the window's size changes.
/// <para>
/// A new picture can <see cref="GlowTo"/>: it lights up like a light behind frosted glass (KAN-54, the owner): a soft
/// circle of it, its edge feathered, grows from a point and brightens until it fills the window. Each frame is one pass
/// of Skia's, the new picture through a radial gradient, where a mask would take several.
/// </para>
/// </summary>
public sealed class GsBackdrop : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<GsBackdrop, IImage?>(nameof(Source));

    private Held? _now;
    private IImage? _nowFrom;
    private PixelSize _nowAt;

    private Held? _next;
    private IImage? _nextFrom;
    private Point _centre;
    private double _far;
    private double _t = 1;
    private DateTime _startedUtc;
    private TimeSpan _duration;
    private bool _settingSource;

    static GsBackdrop() => AffectsRender<GsBackdrop>(SourceProperty);

    /// <summary>The picture shown; setting it ends any glow at once.</summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>A glow still growing.</summary>
    public bool Glowing => _next is not null;

    /// <summary>The picture a glow is growing toward.</summary>
    public IImage? GlowingTo => _nextFrom;

    protected override Size MeasureOverride(Size availableSize) => default;

    /// <summary>
    /// Lights <paramref name="next"/> up over the picture shown, from <paramref name="centre"/> (in this control's
    /// coordinates) over <paramref name="duration"/>; then it's the picture shown. A glow already growing finishes first.
    /// </summary>
    public void GlowTo(IImage next, Point centre, TimeSpan duration)
    {
        FinishGlow();
        if (Prepare(next) is not { } prepared)
        {
            Source = next;
            return;
        }

        (_next, _nextFrom, _centre, _duration, _startedUtc, _t) = (prepared, next, centre, duration, DateTime.UtcNow, 0);
        var size = Bounds.Size;
        _far = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(0, size.Height), new Point(size.Width, size.Height) }
            .Max(corner => Math.Sqrt(Math.Pow(corner.X - centre.X, 2) + Math.Pow(corner.Y - centre.Y, 2)));
        InvalidateVisual();
        Step();
    }

    /// <summary>For the snapshot tool's benchmark: the glow toward <paramref name="next"/> held at <paramref name="t"/> (0 to 1), from the middle.</summary>
    internal void GlowFrame(IImage next, double t)
    {
        if (!ReferenceEquals(_nextFrom, next))
        {
            _next?.Release();
            (_next, _nextFrom) = (Prepare(next), next);
            var size = Bounds.Size;
            (_centre, _far) = (new Point(size.Width / 2, size.Height / 2), Math.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2);
        }

        _t = t;
        InvalidateVisual();
    }

    private void Step()
    {
        if (_next is null)
        {
            return;
        }

        _t = Math.Min(1, (DateTime.UtcNow - _startedUtc) / _duration);
        InvalidateVisual();
        if (_t >= 1)
        {
            FinishGlow();
        }
        else if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.RequestAnimationFrame(_ => Step());
        }
        else
        {
            FinishGlow();
        }
    }

    /// <summary>A glow still growing becomes the picture shown, at once.</summary>
    private void FinishGlow()
    {
        if (_next is not { } next)
        {
            return;
        }

        _now?.Release();
        (_now, _nowFrom, _nowAt) = (next, _nextFrom, next.Size);
        _settingSource = true;
        try
        {
            Source = _nextFrom;
        }
        finally
        {
            _settingSource = false;
        }

        (_next, _nextFrom, _t) = (null, null, 1);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && !_settingSource)
        {
            // A picture set from outside ends any glow; the new one is prepared as it's drawn.
            _next?.Release();
            (_next, _nextFrom, _t) = (null, null, 1);
            if (Source is null)
            {
                LetGo();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _next?.Release();
        (_next, _nextFrom) = (null, null);
        LetGo();
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pixels = PixelsFor(Bounds.Size);
        if (_now is null || !ReferenceEquals(_nowFrom, image) || _nowAt != pixels)
        {
            LetGo();
            (_now, _nowFrom, _nowAt) = (Prepare(image), image, pixels);
        }

        if (_now is null)
        {
            return;
        }

        // The light: solid in its middle, fading to nothing over its outer part, past the farthest corner by the end so no
        // edge is left, and brighter as it grows.
        var grown = 1 - Math.Pow(1 - _t, 2);
        context.Custom(new Draw(new Rect(Bounds.Size), _now.Take(), _next?.Take(), _centre, Math.Max(24, _far * 1.6 * grown), 0.25 + 0.75 * grown));
    }

    private PixelSize PixelsFor(Size size)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scaling)), Math.Max(1, (int)Math.Ceiling(size.Height * scaling)));
    }

    /// <summary>
    /// The picture as a Skia image at this control's pixel size, scaled and cropped as CSS's <c>cover</c> does, with high
    /// quality once; null for a picture that isn't a bitmap GameSync made.
    /// </summary>
    private Held? Prepare(IImage image)
    {
        if (image is not Bitmap bitmap || bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return null;
        }

        var (w, h) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var buffer = new byte[w * h * 4];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, w, h), pinned.AddrOfPinnedObject(), buffer.Length, w * 4);
            var colour = bitmap.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            using var small = SKImage.FromPixelCopy(new SKImageInfo(w, h, colour, SKAlphaType.Premul), pinned.AddrOfPinnedObject(), w * 4);
            var target = PixelsFor(Bounds.Size);
            using var surface = SKSurface.Create(new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (small is null || surface is null)
            {
                return null;
            }

            var scale = Math.Max((double)target.Width / w, (double)target.Height / h);
            double shownW = target.Width / scale, shownH = target.Height / scale;
            var from = SKRect.Create((float)((w - shownW) / 2), (float)((h - shownH) / 2), (float)shownW, (float)shownH);
            surface.Canvas.DrawImage(small, from, SKRect.Create(target.Width, target.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            return new Held(surface.Snapshot(), target);
        }
        finally
        {
            pinned.Free();
        }
    }

    private void LetGo()
    {
        _now?.Release();
        (_now, _nowFrom) = (null, null);
    }

    /// <summary>A prepared picture, held by the backdrop and by the drawings of it the renderer still keeps; let go when none holds it.</summary>
    private sealed class Held(SKImage image, PixelSize size)
    {
        private int _holders = 1;

        public SKImage Image { get; } = image;

        public PixelSize Size { get; } = size;

        public Held Take()
        {
            Interlocked.Increment(ref _holders);
            return this;
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _holders) == 0)
            {
                Image.Dispose();
            }
        }
    }

    /// <summary>One frame of the backdrop, drawn by the renderer: the picture copied, and the next one's light over it.</summary>
    private sealed class Draw(Rect bounds, Held now, Held? next, Point centre, double radius, double brightness) : ICustomDrawOperation
    {
        private static readonly SKColor[] LightColours = [SKColors.White, SKColors.White, new SKColor(255, 255, 255, 0x70), SKColors.Transparent];
        private static readonly float[] LightStops = [0f, 0.4f, 0.7f, 1f];
        private int _released;

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var area = SKRect.Create((float)bounds.Width, (float)bounds.Height);
            canvas.DrawImage(now.Image, area);
            if (next is null)
            {
                return;
            }

            // The next picture, mapped pixel for pixel onto this control's area, through the light's gradient: one pass.
            var toArea = SKMatrix.CreateScale((float)(bounds.Width / next.Size.Width), (float)(bounds.Height / next.Size.Height));
            using var picture = next.Image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKSamplingOptions.Default, toArea);
            using var light = SKShader.CreateRadialGradient(new SKPoint((float)centre.X, (float)centre.Y), (float)radius, LightColours, LightStops, SKShaderTileMode.Clamp);
            using var lit = SKShader.CreateBlend(SKBlendMode.DstIn, picture, light);
            using var paint = new SKPaint { Shader = lit, Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(brightness, 0, 1))) };
            canvas.DrawRect(area, paint);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                now.Release();
                next?.Release();
            }
        }
    }
}
