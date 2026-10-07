using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GameSync.UI.Controls;

/// <summary>
/// A tier's banner (design system version 49 → TierBanner; the owner, 5 Oct 2026: "What if we had flags. I want the color of
/// zenith to be red. Like the main its silver bronze and gold? zenith will be red", "maybe try difficulty of mountains as the
/// tiers", "Keep everest as zenith and K2 as second"): each tier a banner hanging from its bar, each a harder mountain, each
/// adding a layer. Bronze is Fuji on teal cloth, one point and a plain rod; Silver the Matterhorn on blue, two tails, knobs
/// and loops round the bar; Gold K2 on purple, three tails with a gold fringe, a spear tip, cords and tassels, stitching and
/// a lozenge in each top corner; the Zenith Everest on red, its plume of snow, the sun at its height above it, a chief with
/// three stars, a braided gold border, a laurel, a sun on its bar and a tassel on every tail. Drawn from the design's
/// 120 × 190 to fit its box, centred; under 40px tall a banner keeps its cloth, one border and its mountain (the Zenith's:
/// the sun and Everest with its plume). Its colours are fixed in every theme, like the metals. The design system's own
/// drawing (BANNER in its bundle) has the same paths; the app leaves out the cloth's faint weave.
/// </summary>
public sealed class GsTierBanner : Control
{
    public static readonly StyledProperty<string> TierProperty = AvaloniaProperty.Register<GsTierBanner, string>(nameof(Tier), "zenith");

    static GsTierBanner() => AffectsRender<GsTierBanner>(TierProperty);

    /// <summary><c>bronze</c>, <c>silver</c>, <c>gold</c> or <c>zenith</c>.</summary>
    public string Tier
    {
        get => GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    /// <summary>A banner this tall or taller has everything; a smaller one its cloth, one border and its mountain.</summary>
    public const double FullFrom = 40;

    /// <summary>The design's box: a banner is 120 wide for every 190 tall.</summary>
    public const double Aspect = 120.0 / 190;

    public override void Render(DrawingContext context)
    {
        var (w, h) = (Bounds.Width, Bounds.Height);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        Draw(context, new Rect(0, 0, w, h), Tier);
    }

    /// <summary>The banner fitted into <paramref name="box"/>, centred.</summary>
    public static void Draw(DrawingContext context, Rect box, string tier)
    {
        var scale = Math.Min(box.Width / 120, box.Height / 190);
        var offset = new Point(box.X + (box.Width - 120 * scale) / 2, box.Y + (box.Height - 190 * scale) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            TierBannerArt.Draw(context, tier, 190 * scale < FullFrom);
        }
    }
}

/// <summary>The banners' drawing, in the design's 120 × 190, as the design system's BANNER draws it.</summary>
internal static class TierBannerArt
{
    private sealed record Metal(Color Hi, Color Mid, Color Lo, Color Deep);

    private sealed record Look(Metal Metal, Metal Cloth, int Rank);

    private static Metal M(string hi, string mid, string lo, string deep) => new(Color.Parse(hi), Color.Parse(mid), Color.Parse(lo), Color.Parse(deep));

    private static readonly Metal Gold = M("#ffe39a", "#f0c35a", "#c08a2a", "#8f6214");

    private static readonly Dictionary<string, Look> Looks = new()
    {
        ["bronze"] = new(M("#f2c497", "#d7955c", "#a5683a", "#7a4a24"), M("#2f6a62", "#265a53", "#1d4842", "#12312d"), 0),
        ["silver"] = new(M("#f7f9fb", "#d5dbe2", "#9aa5b1", "#6d7884"), M("#3c4e6d", "#30405b", "#25324a", "#1a2436"), 1),
        ["gold"] = new(Gold, M("#58386f", "#482c5e", "#38214b", "#261632"), 2),
        ["zenith"] = new(Gold, M("#bb2a3f", "#a51d32", "#871527", "#5c0d1a"), 3),
    };

    /// <summary>Bronze one point, Silver two tails, Gold and the Zenith three.</summary>
    private static readonly string[] Shapes =
    [
        "M22 19H98V150L60 172L22 150Z",
        "M22 19H98V170L60 147L22 170Z",
        "M22 19H98V166L79 150L60 168L41 150L22 166Z",
        "M22 19H98V170L79 152L60 172L41 152L22 170Z",
    ];

    private static readonly Color Snow = Color.Parse("#fbf7ee");

    private static readonly Dictionary<string, Geometry> Parsed = [];

    private static Geometry G(string path)
    {
        if (!Parsed.TryGetValue(path, out var geometry))
        {
            geometry = Geometry.Parse(path);
            Parsed[path] = geometry;
        }

        return geometry;
    }

    private static IBrush Solid(Color c, double alpha = 1) =>
        new ImmutableSolidColorBrush(alpha >= 1 ? c : Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B));

    /// <summary>Cloth hanging in folds: light and shade in vertical bands across it.</summary>
    private static IBrush Folds(Metal c) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(c.Lo, 0), new GradientStop(c.Hi, 0.14), new GradientStop(c.Mid, 0.3), new GradientStop(c.Hi, 0.46),
            new GradientStop(c.Lo, 0.62), new GradientStop(c.Hi, 0.8), new GradientStop(c.Mid, 1),
        },
    };

    /// <summary>A metal lit from above.</summary>
    private static IBrush Shine(Metal m) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(m.Hi, 0), new GradientStop(m.Mid, 0.5), new GradientStop(m.Lo, 1) },
    };

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static void Draw(DrawingContext context, string tier, bool min)
    {
        var look = Looks.GetValueOrDefault(tier) ?? Looks["zenith"];
        var (m, c, r) = (look.Metal, look.Cloth, look.Rank);
        var cloth = G(Shapes[r]);
        var shine = Shine(m);

        // The bar: Bronze a plain rod; Silver round knobs; Gold a spear tip, cords and tassels; the Zenith a sun on top.
        if (r >= 2 && !min)
        {
            context.DrawGeometry(null, new Pen(Solid(m.Lo), 1.1), G("M12 15L60 6L108 15"));
        }

        if (r == 3 && !min)
        {
            Sun(context, new Point(60, 5), 3.4, 12, m);
        }
        else if (r >= 1 || min)
        {
            context.DrawGeometry(shine, null, G("M60 0L64.5 8.5L60 11.5L55.5 8.5Z"));
        }

        var plain = r == 0 && !min;
        context.DrawRectangle(shine, null, new Rect(plain ? 16 : 11, 12, plain ? 88 : 98, min ? 7 : 5), r == 0 ? 1 : 2.5, r == 0 ? 1 : 2.5);
        if (r >= 1 || min)
        {
            var knob = min ? 5 : 4;
            context.DrawEllipse(shine, null, new Point(10, 14.5), knob, knob);
            context.DrawEllipse(shine, null, new Point(110, 14.5), knob, knob);
        }

        if (r >= 2 && !min)
        {
            Tassel(context, 10, 18, r == 3 ? 30 : 24, m);
            Tassel(context, 110, 18, r == 3 ? 30 : 24, m);
        }

        // The cloth, in folds; Gold's fringe hangs below its foot.
        if (r == 2 && !min)
        {
            Fringe(context, [new(22, 166), new(41, 150), new(60, 168), new(79, 150), new(98, 166)], m);
        }

        var clothBrush = min ? Solid(c.Mid) : Folds(c);
        context.DrawGeometry(clothBrush, null, cloth);
        if (r >= 1 && !min)
        {
            foreach (var x in (double[])[32, 55, 78])
            {
                context.DrawRectangle(Solid(c.Lo), null, new Rect(x, 9.5, 10, 12), 2.5, 2.5);
                context.DrawRectangle(Solid(c.Hi, 0.6), null, new Rect(x, 9.5, 10, 3), 1.5, 1.5);
            }
        }

        // The Zenith: a darker chief across the top with three gold stars.
        if (r == 3 && !min)
        {
            using (context.PushGeometryClip(cloth))
            {
                context.DrawRectangle(Solid(c.Deep, 0.55), null, new Rect(20, 19, 80, 18));
            }

            context.DrawLine(new Pen(Solid(m.Mid), 1), new Point(22, 37), new Point(98, 37));
            foreach (var x in (double[])[42, 60, 78])
            {
                Star(context, new Point(x, 28), 3.6, Solid(m.Mid));
            }
        }

        // The border: Bronze one band; Silver a band and a line; Gold a band, a line and stitches with a lozenge in each top
        // corner; the Zenith a braided band and a line. A stroke along the edge, clipped to the cloth, is a band inside it.
        if (min)
        {
            using (context.PushGeometryClip(cloth))
            {
                context.DrawGeometry(null, new Pen(Solid(m.Mid), 6), cloth);
            }
        }
        else
        {
            using (context.PushGeometryClip(cloth))
            {
                context.DrawGeometry(null, new Pen(shine, r == 3 ? 10 : r == 2 ? 8.4 : 7), cloth);
                if (r == 3)
                {
                    context.DrawGeometry(null, new Pen(Solid(m.Lo), 6.4, new DashStyle([0.8 / 6.4, 2.2 / 6.4], 0)), cloth);
                }
            }

            if (r >= 1)
            {
                var inset = r == 3 ? 0.84 : r == 2 ? 0.86 : 0.88;
                using (context.PushTransform(Matrix.CreateTranslation(-60, -96) * Matrix.CreateScale(inset, inset + 0.04) * Matrix.CreateTranslation(60, 96)))
                {
                    context.DrawGeometry(null, new Pen(Solid(m.Mid), 1 / inset), cloth);
                }
            }

            if (r == 2)
            {
                using (context.PushTransform(Matrix.CreateTranslation(-60, -96) * Matrix.CreateScale(0.78, 0.83) * Matrix.CreateTranslation(60, 96)))
                {
                    context.DrawGeometry(null, new Pen(Solid(m.Mid, 0.8), 1, new DashStyle([2.4, 2.4], 0)), cloth);
                }

                context.DrawGeometry(Solid(m.Mid), null, G("M36 35.6l3.4 3.4-3.4 3.4-3.4-3.4ZM84 35.6l3.4 3.4-3.4 3.4-3.4-3.4Z"));
            }
        }

        if (min && tier == "zenith")
        {
            // Under 40px: the sun and Everest with its plume.
            context.DrawEllipse(Solid(m.Hi), null, new Point(60, 54), 11, 11);
            context.DrawGeometry(Solid(Color.Parse("#46474e")), new Pen(Solid(m.Mid), 3, lineJoin: PenLineJoin.Round), G("M30 140L60 78L92 140Z"));
            context.DrawGeometry(Solid(Snow), null, G("M60 78C70 74 79 73 88 70C81 77 72 79.6 63 83Z"));
        }
        else
        {
            Mountain(context, tier, m);
        }

        if (r == 3 && !min)
        {
            foreach (var (x, y) in ((double, double)[])[(22, 170), (60, 172), (98, 170)])
            {
                Tassel(context, x, y - 1, 2, m);
            }
        }
    }

    /// <summary>Each mountain drawn smaller about the middle of the cloth, so it stays clear of the border.</summary>
    private static DrawingContext.PushedState Fit(DrawingContext context, double scale, double centreY, double from) =>
        context.PushTransform(Matrix.CreateTranslation(-60, -from) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(60, centreY));

    private static void Mountain(DrawingContext context, string tier, Metal m)
    {
        var snow = Solid(Snow);
        switch (tier)
        {
            case "bronze":
                // Fuji: a wide cone with a flat top, snow streaming down from it.
                using (Fit(context, 0.8, 92, 108))
                {
                    context.DrawGeometry(Solid(m.Mid), null, G("M26 132C40 120 49 100 53 84H67C71 100 80 120 94 132Z"));
                    context.DrawGeometry(Solid(m.Lo), null, G("M60 84H67C71 100 80 120 94 132H63Z"));
                    context.DrawGeometry(snow, null, G("M53 84H67C67.8 88.6 69 93.2 70.4 97.4L68.6 96.2L67.8 102.4L65.8 97.2L64.4 104.6L62.6 97.8L60.6 102.8L58.8 97.4L56.8 104L55.2 97L53.6 101.4L52 97.8L49.8 99.2C51 94.8 52.2 89.6 53 84Z"));
                }

                break;
            case "silver":
                // The Matterhorn: a narrow horn, its tip hooked to the left, the north face in shade.
                using (Fit(context, 0.8, 92, 101))
                {
                    context.DrawGeometry(Solid(m.Mid), null, G("M28 134L44 110L50 94L52.4 82L53.6 71L56 68.6L58.6 74L61 82L66 92L74 106L84 120L94 134Z"));
                    context.DrawGeometry(Solid(m.Lo), null, G("M56 68.6L58.6 74L61 82L66 92L74 106L84 120L94 134H62L59 110L57.6 90Z"));
                    context.DrawGeometry(snow, null, G("M53.6 71L56 68.6L58.6 74L57.6 80.6L55.8 76.8L54.4 82.4L52.8 78.6Z"));
                    context.DrawGeometry(Solid(Snow, 0.9), null, G("M63.4 87.6L66 92L64.6 96.4L62.8 92.6ZM70.6 100.6L74 106L71.6 108.6L69.6 104.4ZM46.6 104L50 94L49.8 100.4L48 106Z"));
                }

                break;
            case "gold":
                // K2: the savage mountain, a steep pyramid with its shoulder on the right, snow on its upper faces, the
                // Karakoram behind.
                using (Fit(context, 0.78, 94, 104))
                {
                    context.DrawGeometry(Solid(m.Deep), null, G("M26 136L42 108L58 136ZM62 136L79 104L94 136Z"));
                    context.DrawGeometry(Solid(Snow, 0.8), null, G("M79 104L83.2 112.6L80.4 111.4L78 114.4L76.2 111.2Z"));
                    context.DrawGeometry(Solid(m.Mid), null, G("M30 138L38 122L44 108L49 96L54 84L58 74L60 70L62 75L65 82L67.6 88.4L71.6 91L74.6 93.4L78.4 103L84 115L91 138Z"));
                    context.DrawGeometry(Solid(m.Lo), null, G("M60 70L62 75L65 82L67.6 88.4L71.6 91L74.6 93.4L78.4 103L84 115L91 138H62L61.2 108L60.6 90Z"));
                    context.DrawGeometry(snow, null, G("M60 70L62 75L65 82L67.6 88.4L71.6 91L70.4 95.6L67.4 93.6L64.8 100.2L62 94.4L59.4 101.4L56.8 95L54.2 99.2L52.6 94.6L55.4 86L58 76Z"));
                    context.DrawGeometry(Solid(Snow, 0.85), null, G("M45.6 106L49 96L49.6 101.6L47.4 108.4ZM74.6 106.4L78.4 103L77.6 108.4L75.8 110.6Z"));
                }

                break;
            default:
                // Everest: the roof of the world, a broad pyramid of rock behind lower ridges, a plume of snow streaming from
                // its summit; the sun at its height above, a laurel below.
                Sun(context, new Point(60, 60), 8.5, 16, m);
                using (Fit(context, 0.78, 104, 106))
                {
                    var edge = new Pen(Solid(m.Mid), 1.6, lineJoin: PenLineJoin.Round);
                    context.DrawGeometry(Solid(Color.Parse("#46474e")), edge, G("M24 134L40 112L50 100L57 86L62 78L66 84L74 96L81 104L88 113L96 134Z"));
                    context.DrawGeometry(Solid(Color.Parse("#2f3036")), null, G("M62 78L66 84L74 96L81 104L88 113L96 134H64L63 104Z"));
                    context.DrawGeometry(snow, null, G("M62 78L64.6 82.2L62.8 81.4L61.2 84L59.6 81.2L58 82.6Z"));
                    context.DrawGeometry(Solid(Snow, 0.92), null, G("M62 78C71 73.6 83 73.4 94 69.6C86 76 76 78.4 64.6 82Z"));
                    context.DrawGeometry(Solid(Snow, 0.9), null, G("M52 98L56 90L57 94.6L54.6 100.6ZM70.4 92L74 96L72.4 99.6L70 96.6Z"));
                    context.DrawGeometry(Solid(Color.Parse("#26272c")), new Pen(Solid(m.Mid), 1.3, lineJoin: PenLineJoin.Round),
                        G("M24 134L34 122L42 125L50 117L58 126L68 119L78 126L87 121L96 134Z"));
                    context.DrawGeometry(Solid(Snow, 0.85), null, G("M50 117L54.4 121.6L52 121L50.4 123.4L48.6 120.8L46.6 121.6ZM87 121L90.4 125.6L88.2 125.2L86.6 127L85.6 124.4Z"));
                }

                Laurel(context, m);
                break;
        }
    }

    /// <summary>The sun in splendour: a disc and rays, long and short in turn.</summary>
    private static void Sun(DrawingContext context, Point centre, double r, int rays, Metal m)
    {
        if (rays > 0)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (var i = 0; i < rays; i++)
                {
                    var a = (double)i / rays * 2 * Math.PI;
                    var len = i % 2 == 0 ? r * 1.95 : r * 1.55;
                    var w = i % 2 == 0 ? 0.16 : 0.12;
                    g.BeginFigure(new Point(centre.X + r * 1.08 * Math.Cos(a - w), centre.Y + r * 1.08 * Math.Sin(a - w)), true);
                    g.LineTo(new Point(centre.X + len * Math.Cos(a), centre.Y + len * Math.Sin(a)));
                    g.LineTo(new Point(centre.X + r * 1.08 * Math.Cos(a + w), centre.Y + r * 1.08 * Math.Sin(a + w)));
                    g.EndFigure(true);
                }
            }

            context.DrawGeometry(Solid(m.Mid), null, geometry);
        }

        context.DrawEllipse(Solid(m.Hi), null, centre, r, r);
    }

    private static void Star(DrawingContext context, Point centre, double r, IBrush fill)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var a = -Math.PI / 2 + i * Math.PI / 5;
                var rr = i % 2 == 1 ? r * 0.45 : r;
                var p = new Point(centre.X + rr * Math.Cos(a), centre.Y + rr * Math.Sin(a));
                if (i == 0)
                {
                    g.BeginFigure(p, true);
                }
                else
                {
                    g.LineTo(p);
                }
            }

            g.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);
    }

    /// <summary>A laurel: two curved branches meeting under the mountains, their leaves along them in pairs.</summary>
    private static void Laurel(DrawingContext context, Metal m)
    {
        var leaf = Solid(m.Mid);
        foreach (var side in (int[])[1, -1])
        {
            Point p0 = new(60, 150), p1 = new(60 + side * 17, 152), p2 = new(60 + side * 26, 133);
            var branch = new StreamGeometry();
            using (var g = branch.Open())
            {
                g.BeginFigure(p0, false);
                g.QuadraticBezierTo(p1, p2);
                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(Solid(m.Lo), 1.4, lineCap: PenLineCap.Round), branch);
            for (var i = 1; i <= 6; i++)
            {
                var t = i / 6.4;
                var u = 1 - t;
                var x = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
                var y = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
                var dx = 2 * u * (p1.X - p0.X) + 2 * t * (p2.X - p1.X);
                var dy = 2 * u * (p1.Y - p0.Y) + 2 * t * (p2.Y - p1.Y);
                var n = Math.Sqrt(dx * dx + dy * dy);
                var a = Math.Atan2(dy, dx) * 180 / Math.PI;
                foreach (var k in (int[])[-1, 1])
                {
                    var o = new Point(x - k * dy / n * 2.4, y + k * dx / n * 2.4);
                    // The design rounds its rotation to whole degrees.
                    var turn = Math.Round(a + k * 32) * Math.PI / 180;
                    using (context.PushTransform(Matrix.CreateTranslation(-o.X, -o.Y) * Matrix.CreateRotation(turn) * Matrix.CreateTranslation(o.X, o.Y)))
                    {
                        context.DrawEllipse(leaf, null, o, 3.6, 1.6);
                    }
                }
            }
        }
    }

    /// <summary>A tassel hanging from (x, y): a cord, a knot and a skirt of threads.</summary>
    private static void Tassel(DrawingContext context, double x, double y, double len, Metal m)
    {
        var b = y + len;
        context.DrawLine(new Pen(Solid(m.Lo), 1.1), new Point(x, y), new Point(x, b));
        context.DrawEllipse(Solid(m.Mid), null, new Point(x, b + 1.5), 2.1, 2.1);
        context.DrawGeometry(Solid(m.Mid), null, G($"M{F(x - 2.2)} {F(b + 3)}L{F(x - 3.4)} {F(b + 12)}L{F(x + 3.4)} {F(b + 12)}L{F(x + 2.2)} {F(b + 3)}Z"));
        var thread = new Pen(Solid(m.Lo), 0.5);
        foreach (var dx in (double[])[-1.4, 0, 1.4])
        {
            context.DrawLine(thread, new Point(x + dx, b + 4), new Point(x + dx, b + 12));
        }
    }

    /// <summary>A fringe of gold threads hanging from the cloth's foot, along each of its edges.</summary>
    private static void Fringe(DrawingContext context, Point[] points, Metal m)
    {
        var pen = new Pen(Solid(m.Mid), 0.9);
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var n = (int)Math.Floor(Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)) / 1.7);
            for (var k = 0; k <= n; k++)
            {
                var x = a.X + (b.X - a.X) * k / n;
                var y = a.Y + (b.Y - a.Y) * k / n;
                context.DrawLine(pen, new Point(x, y), new Point(x, y + 4.2));
            }
        }
    }
}
