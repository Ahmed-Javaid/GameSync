using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace GameSync.UI.Controls;

/// <summary>
/// KAN-76 (the owner, 1 Oct 2026): a change of look, such as Dark to Light, isn't a jump. <see cref="Show"/> lays the
/// window as it looked over the new look, as a picture, and the new look comes through it: from where the person
/// clicked, a circle of it with a soft edge grows until the old picture is gone, as a new game's backdrop lights up
/// (KAN-54); with no click to start from (Windows changed its mode), the old picture fades.
/// Each frame is one pass of Skia's, the picture cut away through the light's gradient. Clicks go through to the new
/// look underneath.
/// </summary>
public sealed class GsLookCurtain : Control
{
    private HeldImage? _before;
    private Point? _from;
    private double _far;
    private double _t = 1;
    private DateTime _startedUtc;
    private TimeSpan _duration;

    public GsLookCurtain() => IsHitTestVisible = false;

    /// <summary>A change of look still coming through.</summary>
    public bool Showing => _before is not null;

    protected override Size MeasureOverride(Size availableSize) => default;

    /// <summary>
    /// Shows <paramref name="before"/>, the window as it looked at <paramref name="size"/> pixels, over the new look and
    /// takes it away over <paramref name="duration"/>: from <paramref name="from"/> (in this control's coordinates)
    /// outward, or all of it evenly when that's null. A change still coming through gives way to this one.
    /// </summary>
    public void Show(SKImage before, PixelSize size, Point? from, TimeSpan duration)
    {
        _before?.Release();
        _before = new HeldImage(before, size);
        (_from, _duration, _startedUtc, _t) = (from, duration, DateTime.UtcNow, 0);
        if (from is { } centre)
        {
            var area = Bounds.Size;
            _far = new[] { new Point(0, 0), new Point(area.Width, 0), new Point(0, area.Height), new Point(area.Width, area.Height) }
                .Max(corner => Math.Sqrt(Math.Pow(corner.X - centre.X, 2) + Math.Pow(corner.Y - centre.Y, 2)));
        }

        InvalidateVisual();
        Step();
    }

    /// <summary>For the snapshot tool: the change held at <paramref name="t"/> (0 to 1), as it looks part way.</summary>
    internal void Hold(SKImage before, PixelSize size, Point? from, double t)
    {
        Show(before, size, from, TimeSpan.FromDays(1));
        _t = t;
        InvalidateVisual();
    }

    private void Step()
    {
        if (_before is null || _duration >= TimeSpan.FromDays(1))
        {
            return;
        }

        _t = Math.Min(1, (DateTime.UtcNow - _startedUtc) / _duration);
        InvalidateVisual();
        if (_t >= 1)
        {
            Finish();
        }
        else if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.RequestAnimationFrame(_ => Step());
        }
        else
        {
            Finish();
        }
    }

    private void Finish()
    {
        _before?.Release();
        (_before, _t) = (null, 1);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Finish();
    }

    public override void Render(DrawingContext context)
    {
        if (_before is not { } before || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // Fast at first and gentle at the end. The circle's clear middle reaches the farthest corner by the end, so nothing
        // of the old look is left when the picture goes.
        var grown = 1 - Math.Pow(1 - _t, 2);
        context.Custom(new Draw(new Rect(Bounds.Size), before.Take(), _from, Math.Max(24, _far / Draw.Solid * grown), grown));
    }

    /// <summary>One frame: the old look, cut away where the new one's light is, or faded evenly.</summary>
    private sealed class Draw(Rect bounds, HeldImage before, Point? from, double radius, double gone) : ICustomDrawOperation
    {
        /// <summary>
        /// How far out from the middle the new look is wholly through, as a part of the light's radius; its edge is feathered
        /// over the rest. Narrow, so text is never seen twice for long.
        /// </summary>
        public const float Solid = 0.85f;

        private static readonly SKColor[] LightColours = [SKColors.White, SKColors.White, SKColors.Transparent];
        private static readonly float[] LightStops = [0f, Solid, 1f];
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
            if (from is not { } centre)
            {
                using var fading = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(1 - gone, 0, 1))) };
                canvas.DrawImage(before.Image, area, fading);
                return;
            }

            var toArea = SKMatrix.CreateScale((float)(bounds.Width / before.Size.Width), (float)(bounds.Height / before.Size.Height));
            using var picture = before.Image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKSamplingOptions.Default, toArea);
            using var glow = SKShader.CreateRadialGradient(new SKPoint((float)centre.X, (float)centre.Y), (float)radius, LightColours, LightStops, SKShaderTileMode.Clamp);
            using var cut = SKShader.CreateBlend(SKBlendMode.DstOut, picture, glow);
            using var paint = new SKPaint { Shader = cut };
            canvas.DrawRect(area, paint);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                before.Release();
            }
        }
    }
}
