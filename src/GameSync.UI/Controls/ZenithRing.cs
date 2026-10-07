using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace GameSync.UI.Controls;

/// <summary>
/// A game's 100% ring's light (design system version 49 → ProgressRing's ZenithRing; the owner, 5 Oct 2026: "I really want
/// players to feel awarded when they hit 100%", then "gold and red seems good"), drawn over a <see cref="GsProgressRing"/>'s
/// ring box: <see cref="IsFront"/> false, behind the ring, faint sun rays turning once a minute and a warm gold glow
/// breathing round it (four seconds each way); true, over the ring, a glint of light running round it every six seconds.
/// The moment's flash, burst and sparks are <see cref="GsZenithMoment"/>'s. Its light moves on the <see cref="Ambient"/>
/// clock while it's on screen, its window is the active one and Windows' animation effects are on (15 times a second, 30
/// while the glint passes); otherwise it holds still, and with the effects off the glint never shows. Spills past its box.
/// </summary>
public sealed class GsZenithGlow : Control, IAmbientLight
{
    public static readonly StyledProperty<bool> IsFrontProperty = AvaloniaProperty.Register<GsZenithGlow, bool>(nameof(IsFront));

    public static readonly StyledProperty<double> RingThicknessProperty = AvaloniaProperty.Register<GsZenithGlow, double>(nameof(RingThickness), 10);

    private static readonly Color Glow = Color.Parse("#ffd678");
    private static readonly Color GlowLight = Color.Parse("#d6a028");

    /// <summary>Its light's time, two minutes round: the rays' turn, the glow's breaths and the glint's runs all fit it.</summary>
    private readonly AmbientTime _time = new(120);

    private bool _listening;

    static GsZenithGlow()
    {
        AffectsRender<GsZenithGlow>(RingThicknessProperty, IsFrontProperty);
        IsVisibleProperty.Changed.AddClassHandler<GsZenithGlow>((g, _) => g.Listen());
    }

    public GsZenithGlow()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public bool IsFront
    {
        get => GetValue(IsFrontProperty);
        set => SetValue(IsFrontProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    /// <summary>Seconds on its light's time, 0 up to 120.</summary>
    internal double Time => _time.Seconds;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Listen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unlisten();
    }

    /// <summary>Takes the clock's ticks while it's on screen.</summary>
    private void Listen()
    {
        if (!IsVisible || VisualRoot is null)
        {
            Unlisten();
            return;
        }

        if (!_listening)
        {
            _listening = true;
            _time.Resume();
            Ambient.Start(this);
        }
    }

    private void Unlisten()
    {
        if (_listening)
        {
            _listening = false;
            Ambient.Stop(this);
        }
    }

    /// <summary>
    /// Behind the ring the rays and the glow move every tick. In front only the glint does: it's drawn while it passes, and
    /// once more as it goes, at the quick tick.
    /// </summary>
    TimeSpan IAmbientLight.Advance(double now)
    {
        var was = Glinting(_time.Seconds);
        if (!_time.Advance(now, Ambient.Moves(this)))
        {
            return Ambient.Waiting;
        }

        if (!IsFront)
        {
            InvalidateVisual();
            return Ambient.Slow;
        }

        var glinting = Glinting(_time.Seconds);
        if (glinting || was)
        {
            InvalidateVisual();
        }

        return glinting ? Ambient.Quick : Ambient.Slow;
    }

    /// <summary>The glint runs round in the first quarter of every six seconds.</summary>
    private static bool Glinting(double seconds) => seconds % 6 / 6 < 0.25;

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var light = ActualThemeVariant == ThemeVariant.Light;
        var glow = light ? GlowLight : Glow;
        var t = _time.Seconds;
        if (!IsFront)
        {
            DrawRays(context, centre, size, glow, t * 6);
            DrawHalo(context, centre, size, glow, t);
            return;
        }

        if (Motion.On)
        {
            DrawGlint(context, centre, size, t);
        }
    }

    internal static IBrush Solid(Color c, double alpha) =>
        new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));

    internal static Point At(Point centre, double radius, double degrees)
    {
        // Degrees clockwise from the top, as a ring's arc is drawn.
        var a = (degrees - 90) * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(a), centre.Y + radius * Math.Sin(a));
    }

    /// <summary>Sixteen faint rays from behind the ring out past it by nearly a fifth of its size, fading out, so they stay in its card.</summary>
    private static void DrawRays(DrawingContext context, Point centre, double size, Color glow, double turn)
    {
        var reach = size * 0.68;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < 16; i++)
            {
                var from = 6 + i * 22.5 + turn;
                g.BeginFigure(At(centre, reach * 0.5, from), true);
                g.LineTo(At(centre, reach, from));
                g.LineTo(At(centre, reach, from + 4));
                g.LineTo(At(centre, reach * 0.5, from + 4));
                g.EndFigure(true);
            }
        }

        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(centre, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(reach, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(reach, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.56),
                new GradientStop(Color.FromArgb(87, glow.R, glow.G, glow.B), 0.64),
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1),
            },
        };
        context.DrawGeometry(brush, null, geometry);
    }

    /// <summary>A warm glow hugging the ring's outside, breathing: brighter and a little larger, then back, four seconds each way.</summary>
    private static void DrawHalo(DrawingContext context, Point centre, double size, Color glow, double t)
    {
        var breath = (1 - Math.Cos(Math.PI * (t % 8) / 4)) / 2;
        var opacity = 0.55 + 0.4 * breath;
        var radius = size * 0.7 * (0.96 + 0.08 * breath);
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(centre, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.58),
                new GradientStop(Color.FromArgb((byte)(158 * opacity), glow.R, glow.G, glow.B), 0.66),
                new GradientStop(Color.FromArgb((byte)(51 * opacity), glow.R, glow.G, glow.B), 0.75),
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.9),
            },
        };
        context.DrawEllipse(brush, null, centre, radius, radius);
    }

    /// <summary>
    /// A glint running round the ring once every six seconds: a short stretch of it brightening to white, then gold,
    /// carried round in the first fifth of each turn of six, fading in and out.
    /// </summary>
    private void DrawGlint(DrawingContext context, Point centre, double size, double t)
    {
        var cycle = t % 6 / 6;
        if (cycle >= 0.25)
        {
            return;
        }

        var opacity = cycle < 0.04 ? cycle / 0.04 : cycle > 0.22 ? (0.25 - cycle) / 0.03 : 1;
        var travel = Ease(Math.Min(cycle / 0.22, 1));
        var start = -30 + 360 * travel;
        var thickness = Math.Min(RingThickness, size / 2);
        var radius = (size - thickness) / 2;
        // Four-degree steps along its 32 degrees: clear, white at 16, gold at 24, clear again.
        for (var step = 0; step < 8; step++)
        {
            var at = step * 4 + 2;
            var (colour, alpha) = at < 16 ? (Colors.White, 0.95 * at / 16)
                : at < 24 ? (Color.Parse("#fff1cf"), 0.95)
                : (Color.Parse("#ffe39a"), 0.9 * (32 - at) / 8);
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(At(centre, radius, start + step * 4), false);
                g.ArcTo(At(centre, radius, start + step * 4 + 4.4), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(Solid(colour, alpha * opacity), thickness), arc);
        }
    }

    /// <summary>The glint's easing: slow off the mark, quick, then settling (the design's cubic-bezier(.45, 0, .25, 1)).</summary>
    private static double Ease(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
}

/// <summary>
/// The Zenith's moment's light (version 49): a flash round the ring, a burst of light and gold sparks flying out. While it
/// plays it's drawn over a <see cref="GsProgressRing"/>'s ring box from the window's adorner layer, so it spills past the
/// ring's card as the design's does: a page's scrolling cuts off what's in it at the card's edge (seen on Counter-Strike
/// 2's page, 6 Oct 2026). It covers the window and draws where the ring is each frame, so it stays on the ring if the page
/// scrolls. Then it's gone.
/// </summary>
public sealed class GsZenithMoment : Control
{
    public static readonly StyledProperty<double> RingThicknessProperty = AvaloniaProperty.Register<GsZenithMoment, double>(nameof(RingThickness), 10);

    /// <summary>Seconds into the moment; below 0 when it doesn't play.</summary>
    public static readonly StyledProperty<double> MomentProperty = AvaloniaProperty.Register<GsZenithMoment, double>(nameof(Moment), -1);

    /// <summary>When the flash, burst and sparks start, in seconds: once the ring has filled its last stretch.</summary>
    public const double Burst = 0.9;

    /// <summary>How long the moment lasts.</summary>
    public const double Length = 2.2;

    private static readonly Color Spark = Color.Parse("#ffe39a");

    /// <summary>The ring box it plays over.</summary>
    private Visual? _box;

    static GsZenithMoment() => AffectsRender<GsZenithMoment>(MomentProperty, RingThicknessProperty);

    public GsZenithMoment()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public double Moment
    {
        get => GetValue(MomentProperty);
        set => SetValue(MomentProperty, value);
    }

    /// <summary>
    /// Plays the moment's light over <paramref name="box"/>, a ring box, from the start, above the page, then takes it away;
    /// the ring box's own parts are animated by <see cref="GsProgressRing.PlayMoment"/>.
    /// </summary>
    internal static async void PlayOver(Control box, double ringThickness)
    {
        if (!Motion.On || AdornerLayer.GetAdornerLayer(box) is not { } layer)
        {
            return;
        }

        var moment = new GsZenithMoment { RingThickness = ringThickness, Width = layer.Bounds.Width, Height = layer.Bounds.Height, _box = box };
        layer.Children.Add(moment);
        try
        {
            await new Animation
            {
                Duration = TimeSpan.FromSeconds(Length),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(MomentProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(MomentProperty, Length) } },
                },
            }.RunAsync(moment);
        }
        finally
        {
            layer.Children.Remove(moment);
        }
    }

    /// <summary>The moment, from <see cref="Burst"/> on, where the ring is now: a flash, a burst of light and gold sparks.</summary>
    public override void Render(DrawingContext context)
    {
        var t = Moment - Burst;
        if (t < 0 || _box is not { } box || box.TransformToVisual(this) is not { } toHere)
        {
            return;
        }

        // The ring box's pop as it flashes is in its transform.
        var size = Math.Min(box.Bounds.Width, box.Bounds.Height) * toHere.M11;
        if (size <= 0)
        {
            return;
        }

        var centre = new Point(box.Bounds.Width / 2, box.Bounds.Height / 2).Transform(toHere);
        var thickness = Math.Min(RingThickness * toHere.M11, size / 2);
        var radius = (size - thickness) / 2;
        if (t < 0.5)
        {
            var flash = t < 0.15 ? 0.9 * t / 0.15 : 0.9 * (0.5 - t) / 0.35;
            context.DrawEllipse(null, new Pen(GsZenithGlow.Solid(Colors.White, flash), thickness), centre, radius, radius);
        }

        if (t < 0.8)
        {
            var p = 1 - Math.Pow(1 - t / 0.8, 2);
            var burst = size / 2 * 1.04 * (0.92 + (1.75 - 0.92) * p);
            context.DrawEllipse(null, new Pen(GsZenithGlow.Solid(Spark, 0.9 * (1 - p)), 4), centre, burst, burst);
        }

        if (t < 1.2)
        {
            var spark = GsZenithGlow.Solid(Spark, 1);
            for (var i = 0; i < 16; i++)
            {
                var local = t - (i % 3) * 0.04;
                if (local <= 0)
                {
                    continue;
                }

                var p = Math.Min(local / 1.2, 1);
                var eased = 1 - Math.Pow(1 - p, 2);
                var from = size * 0.46;
                var to = size * (0.66 + (i * 37 % 40) / 100.0);
                var at = GsZenithGlow.At(centre, from + (to - from) * eased, i * 22.5 + (i % 2) * 8 + 90);
                var alpha = p < 0.12 ? p / 0.12 : 1 - (p - 0.12) / 0.88;
                var r = 2.5 * (1 - 0.6 * p);
                using (context.PushOpacity(Math.Clamp(alpha, 0, 1)))
                {
                    context.DrawEllipse(spark, null, at, r, r);
                }
            }
        }
    }
}
