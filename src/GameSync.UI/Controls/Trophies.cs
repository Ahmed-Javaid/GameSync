using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GameSync.UI.Controls;

/// <summary>
/// A trophy in its metal, lit from the top left (design system version 35 → TrophyFigure): the cup, its handles, the
/// stem, the base and the plinth, drawn from the design's 120 × 150 paths to fit its box. <see cref="Metal"/> is
/// <c>bronze</c>, <c>silver</c>, <c>gold</c> or <c>platinum</c>, the achievements' fixed metals.
/// </summary>
public sealed class GsTrophyFigure : Control
{
    public static readonly StyledProperty<string> MetalProperty = AvaloniaProperty.Register<GsTrophyFigure, string>(nameof(Metal), "gold");

    private static readonly Dictionary<string, (Color Light, Color Deep)> Metals = new()
    {
        ["bronze"] = (Color.Parse("#d7955c"), Color.Parse("#8a5530")),
        ["silver"] = (Color.Parse("#e3e8ee"), Color.Parse("#8d99a6")),
        ["gold"] = (Color.Parse("#ffd76e"), Color.Parse("#c08a2a")),
        ["platinum"] = (Color.Parse("#eef7ff"), Color.Parse("#9bb6cd")),
    };

    private static Geometry? _cup, _handles, _stem, _base, _plinth, _plate, _shine;

    static GsTrophyFigure() => AffectsRender<GsTrophyFigure>(MetalProperty);

    public string Metal
    {
        get => GetValue(MetalProperty);
        set => SetValue(MetalProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        _cup ??= Geometry.Parse("M30 14h60v30c0 25-14 42-30 46-16-4-30-21-30-46z");
        _handles ??= Geometry.Parse("M31 25H16v9c0 14 9 25 22 27 M89 25h15v9c0 14-9 25-22 27");
        _stem ??= Geometry.Parse("M53 89h14v19H53z");
        _base ??= Geometry.Parse("M37 108h46l5 12H32z");
        _plinth ??= Geometry.Parse("M25 120h70a3 3 0 0 1 3 3v15a3 3 0 0 1-3 3H25a3 3 0 0 1-3-3v-15a3 3 0 0 1 3-3z");
        _plate ??= Geometry.Parse("M44 127h32v7H44z");
        _shine ??= Geometry.Parse("M43 22c-2 19 2 35 13 47");

        var (light, deep) = Metals.GetValueOrDefault(Metal, Metals["gold"]);
        var metal = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(light, 0.18), new GradientStop(deep, 1) },
        };

        // The drawing's own 120 × 150 box, scaled to fit and centred.
        var scale = Math.Min(Bounds.Width / 120, Bounds.Height / 150);
        var offset = new Point((Bounds.Width - 120 * scale) / 2, (Bounds.Height - 150 * scale) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawGeometry(null, new Pen(metal, 7, lineCap: PenLineCap.Round), _handles);
            foreach (var part in new[] { _cup, _stem, _base, _plinth })
            {
                context.DrawGeometry(metal, null, part);
            }

            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(56, 27, 33, 41)), null, _plate);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), 5, lineCap: PenLineCap.Round), _shine);
        }
    }
}

/// <summary>
/// The tiers' banners rising at the right of the Achievements page's band (design system version 35 → TrophyRise, banners
/// since version 49; the owner, 3 Oct 2026: "trophy signs in it, emerging from the right"): the Zenith's Everest tallest
/// at the back, Gold's K2 and Silver's Matterhorn either side, Bronze's Fuji in front, in a light of their own. They rise
/// into place one after another as the page opens, then stay still; with Windows' animation effects off they're simply
/// there. Decoration only: a screen reader hears nothing of it. Its box is 280 wide; place it at the band's bottom right.
/// Versions 35 to 48 had cups (<see cref="GsTrophyFigure"/>).
/// </summary>
public sealed class GsTrophyRise : Panel
{
    /// <summary>Each banner: its tier, its height, its distance from the right and from the bottom, and when it rises.</summary>
    private static readonly (string Tier, double Height, double Right, double Bottom, double Delay)[] Trophies =
    [
        ("silver", 118, 196, 22, 0.25),
        ("zenith", 166, 96, 6, 0.05),
        ("gold", 138, 8, 12, 0.15),
        ("bronze", 100, 168, -4, 0.35),
    ];

    private readonly List<Control> _rising = [];

    public GsTrophyRise()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Width = 280;
        Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 340,
            Height = 340,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -10, -150),
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(41, 255, 215, 110), 0),
                    new GradientStop(Color.FromArgb(18, 165, 29, 50), 0.42),
                    new GradientStop(Color.FromArgb(0, 165, 29, 50), 0.68),
                },
            },
        });

        foreach (var (tier, height, right, bottom, _) in Trophies)
        {
            var figure = new GsTierBanner
            {
                Tier = tier,
                Height = height,
                Width = Math.Round(height * GsTierBanner.Aspect),
            };
            var rise = new Panel
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, right, bottom),
                ClipToBounds = false,
                RenderTransform = new TranslateTransform(),
                Children = { figure },
            };
            _rising.Add(rise);
            Children.Add(rise);
        }
    }

    /// <summary>Never makes the band any taller: the trophies stand in whatever height it has, their plinths below its edge.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        return new Size(double.IsNaN(Width) ? 280 : Width, 0);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rise();
    }

    /// <summary>Each trophy from 48px lower and clear, to its place, one after another.</summary>
    private void Rise()
    {
        if (!Motion.On)
        {
            return;
        }

        for (var i = 0; i < _rising.Count; i++)
        {
            var rise = _rising[i];
            var animation = new Animation
            {
                Duration = TimeSpan.FromSeconds(0.7),
                Delay = TimeSpan.FromSeconds(Trophies[i].Delay),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Both,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, 48d), new Setter(OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d), new Setter(OpacityProperty, 1d) } },
                },
            };
            _ = animation.RunAsync(rise);
        }
    }
}

/// <summary>The Zenith badge's brushes.</summary>
internal static class ZenithPaint
{
    internal static LinearGradientBrush Gradient(double x2, double y2, params (string Colour, double At)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(x2, y2, RelativeUnit.Relative),
        };
        foreach (var (colour, at) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.Parse(colour), at));
        }

        return brush;
    }

    internal static IBrush Solid(string colour, double alpha = 1)
    {
        var c = Color.Parse(colour);
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B));
    }
}

/// <summary>
/// The Zenith badge (design system version 45 → ZenithBadge; the owner, 4 Oct 2026, of version 44's print: "IN the red
/// badge, make it pointy mountains"; and of the Achievements page, which still had the mountain and star: "you didn't make
/// zenith changes here"): the Zenith's symbol everywhere, a print inside a ring. A misty sky; a red sun with halftone rings
/// of dots round it and within it; a tall pointy peak in front of the sun, its left face lit and its right in shade, with
/// faint facets; a pointy peak either side behind it and a jagged range behind those; hazy pines behind and dark pines
/// either side; pale grass in front; a dark teal ring with a fine pale line, and a ring of dots round it all. It's the
/// earned medal's face, the Zeniths monument, and the Zeniths card's mark. Drawn from the design's 64 × 64 to fit its box;
/// the pines, the grass and the rings come from <see cref="ZenithArt"/>, generated with the design system's own, so both
/// draw the same badge. Its colours are fixed in every theme, like the metals. From 48px it has everything; from 30px a
/// simpler halo, coarser grass and no facets; below that the sun, the peaks and the pines in the ring. Version 44 had a
/// rounded, craggy summit with two figures on top.
/// </summary>
public sealed class GsZenithBadge : Control
{
    /// <summary>The sun's centre in the design's 64 × 64; the halo's rings are round it.</summary>
    private static readonly Point Sun = new(32, 27);

    private static readonly Point Centre = new(32, 32);

    private static readonly Dictionary<string, Shapes> Made = [];
    private static readonly Lock MadeLock = new();

    private static readonly IBrush Ring = ZenithPaint.Solid("#1b3640"), Line = ZenithPaint.Solid("#e8eef0", 0.9), Range = ZenithPaint.Solid("#8aa4ae", 0.55),
        SidesLit = ZenithPaint.Solid("#9db3bc", 0.35), Lit = ZenithPaint.Solid("#6b8d99", 0.5), Shade = ZenithPaint.Solid("#132c36", 0.45),
        Facet = ZenithPaint.Solid("#122a33", 0.45), Far = ZenithPaint.Solid("#3e6170"), Near = ZenithPaint.Solid("#183641"), Grass = ZenithPaint.Solid("#eef3f4", 0.85);

    private static readonly IBrush Sky = ZenithPaint.Gradient(0, 1, ("#9fb6c1", 0), ("#c4d3d9", 0.55), ("#dbe4e7", 1));
    private static readonly IBrush Sides = ZenithPaint.Gradient(0, 1, ("#6f8f9b", 0), ("#4b6b78", 1));
    private static readonly IBrush Rock = ZenithPaint.Gradient(1, 1, ("#466a78", 0), ("#2d4c59", 0.5), ("#1b3440", 1));
    private static readonly IBrush Ground = ZenithPaint.Gradient(0, 1, ("#2a4d5a", 0), ("#173039", 1));
    private static readonly IBrush Haze = ZenithPaint.Gradient(0, 1, ("#007d98a3", 0), ("#8c7d98a3", 1));

    private static readonly IBrush SunFill = new RadialGradientBrush
    {
        Center = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#e0503f"), 0), new GradientStop(Color.Parse("#c73a30"), 0.7), new GradientStop(Color.Parse("#b02e27"), 1) },
    };

    /// <summary>One size's shapes, made once: the rings of dots with their brushes, the grass with its widths, the peaks and the pines.</summary>
    private sealed record Shapes((Geometry Dots, IBrush Fill)[] Halo, (Geometry Dots, IBrush Fill)[] SunRings, (Geometry Dots, IBrush Fill)[] Rim,
        (Geometry Blades, double Width)[] Grass, Geometry Range, Geometry Sides, Geometry SidesLit, Geometry Peak, Geometry Lit, Geometry Shade,
        Geometry Facets, Geometry Far, Geometry Near, Geometry Ground, Geometry Window);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size > 0)
        {
            Draw(context, new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size));
        }
    }

    /// <summary>The badge, filling the square <paramref name="box"/>.</summary>
    internal static void Draw(DrawingContext context, Rect box)
    {
        var kind = box.Width >= 48 ? "fine" : box.Width >= 30 ? "mid" : "small";
        var shaded = kind != "small";
        var s = ShapesOf(kind);
        using (context.PushTransform(Matrix.CreateScale(box.Width / 64, box.Width / 64) * Matrix.CreateTranslation(box.X, box.Y)))
        {
            context.DrawEllipse(Ring, null, Centre, 31.4, 31.4);
            context.DrawEllipse(null, new Pen(Line, 0.7), Centre, 29.4, 29.4);
            using (context.PushGeometryClip(s.Window))
            {
                context.DrawRectangle(Sky, null, new Rect(0, 0, 64, 64));

                // The halo's rings, the sun, then the finer rings within it (the last one white, at its edge).
                foreach (var (dots, fill) in s.Halo)
                {
                    context.DrawGeometry(fill, null, dots);
                }

                context.DrawEllipse(SunFill, null, Sun, 13.4, 13.4);
                foreach (var (dots, fill) in s.SunRings)
                {
                    context.DrawGeometry(fill, null, dots);
                }

                // The jagged range, the side peaks, then the tall peak in front of the sun, each lit from the left.
                context.DrawGeometry(Range, null, s.Range);
                context.DrawGeometry(Sides, null, s.Sides);
                if (shaded)
                {
                    context.DrawGeometry(SidesLit, null, s.SidesLit);
                }

                context.DrawGeometry(Rock, null, s.Peak);
                if (shaded)
                {
                    context.DrawGeometry(Lit, null, s.Lit);
                    context.DrawGeometry(Shade, null, s.Shade);
                }

                if (kind == "fine")
                {
                    context.DrawGeometry(null, new Pen(Facet, 0.4, lineCap: PenLineCap.Round), s.Facets);
                }

                context.DrawRectangle(Haze, null, new Rect(0, 50, 64, 14));
                context.DrawGeometry(Far, null, s.Far);
                context.DrawGeometry(Near, null, s.Near);
                context.DrawGeometry(Ground, null, s.Ground);
                foreach (var (blades, width) in s.Grass)
                {
                    context.DrawGeometry(null, new Pen(Grass, width, lineCap: PenLineCap.Round), blades);
                }
            }

            foreach (var (dots, fill) in s.Rim)
            {
                context.DrawGeometry(fill, null, dots);
            }
        }
    }

    private static Shapes ShapesOf(string kind)
    {
        lock (MadeLock)
        {
            if (Made.TryGetValue(kind, out var made))
            {
                return made;
            }

            var (halo, sun, rim, grass) = kind switch
            {
                "fine" => (ZenithArt.FineHalo, ZenithArt.FineSun, ZenithArt.FineRim, ZenithArt.FineGrass),
                "mid" => (ZenithArt.MidHalo, ZenithArt.MidSun, ZenithArt.MidRim, ZenithArt.MidGrass),
                _ => (ZenithArt.SmallHalo, ZenithArt.SmallSun, ZenithArt.SmallRim, ZenithArt.SmallGrass),
            };
            made = new Shapes(
                halo.Select(r => (Dots(Sun, r), ZenithPaint.Solid("#ffffff", r.Opacity))).ToArray(),
                sun.Select((r, i) => (Dots(Sun, r), ZenithPaint.Solid(i == sun.Length - 1 ? "#ffffff" : "#ffd9d0", r.Opacity))).ToArray(),
                rim.Select(r => (Dots(Centre, r), ZenithPaint.Solid("#e8eef0", r.Opacity))).ToArray(),
                grass.Select(g => (Geometry.Parse(g.Path), g.Width)).ToArray(),
                Geometry.Parse("M0 45L4 41.5 6.5 43 10.5 37 13.5 40.5 16 38.5 19.5 42.5 23 39 27 43 32 40 36 43.5 40 39.5 44 42 47.5 37.5 51 41 54.5 36 58 40.5 61 38.5 64 41V64H0Z"),
                Geometry.Parse("M2 58L7 47 9.4 44.6 11.4 39.6 13.2 36.4 15.4 31.2 17.4 35 19 37.4 21 41.6 23.2 45 26 52 27 58ZM37 58L39.8 50.6 41.6 46.4 43.6 41.2 45.6 37 47.2 33.4 48.8 29.6 50.6 33.4 52.4 36.2 54.6 41 57 44.8 60.4 52 63 58Z"),
                Geometry.Parse("M2 58L7 47 9.4 44.6 11.4 39.6 13.2 36.4 15.4 31.2 14.8 35.4 13.6 39.8 14.2 44 12.4 49 12.8 58ZM37 58L39.8 50.6 41.6 46.4 43.6 41.2 45.6 37 47.2 33.4 48.8 29.6 48.4 34.2 47.2 38.4 47.8 43 46.2 48.4 46.6 58Z"),
                Geometry.Parse("M32 20.4L30.7 23.1 29.6 24.9 28 28.4 26.7 30.4 24.9 34.5 23.4 36.8 21.6 40.9 19.6 44.2 17.8 48.7 15.5 53.3 13.6 58.4 12.2 64 51.6 64 50.2 58.7 48.5 54.1 46.1 49.5 44.7 45.3 42.5 41.5 41.1 37.7 39.2 34.7 37.9 31.1 36.3 28.7 35 25.5 33.5 23.3Z"),
                Geometry.Parse("M32 20.4L30.7 23.1 29.6 24.9 28 28.4 26.7 30.4 24.9 34.5 23.4 36.8 21.6 40.9 19.6 44.2 17.8 48.7 15.5 53.3 13.6 58.4 12.2 64 27.5 64 27.1 59.6 28.7 54 28 48.4 29.9 43.2 29.2 38.6 30.8 33.8 30.2 29.6 31.4 25Z"),
                Geometry.Parse("M32 20.4L34 26.4 35 32 36.9 37.4 37.6 43.4 39.7 49.4 40.6 55.6 42.2 64 51.6 64 50.2 58.7 48.5 54.1 46.1 49.5 44.7 45.3 42.5 41.5 41.1 37.7 39.2 34.7 37.9 31.1 36.3 28.7 35 25.5 33.5 23.3Z"),
                Geometry.Parse("M30.4 29.2L27.6 33.6M29.6 38.4L26 43.8M28.3 47.8L24.2 54.4M35 31.6L37.2 35.6M37.4 42.2L40.6 47.6M40.2 53.4L43.4 59"),
                Geometry.Parse(ZenithArt.Far),
                Geometry.Parse(ZenithArt.Near),
                Geometry.Parse("M0 57Q16 54.5 32 56T64 56.5V64H0Z"),
                new EllipseGeometry(new Rect(32 - 27.6, 32 - 27.6, 55.2, 55.2)));
            Made[kind] = made;
            return made;
        }
    }

    /// <summary>A ring of dots round <paramref name="centre"/>, worked out as the design system's badge does, its rounding included.</summary>
    internal static Geometry Dots(Point centre, (double Radius, double Step, double Dot, double Opacity) ring)
    {
        var (radius, step, dot, _) = ring;
        var n = Math.Max(6, (int)Math.Floor(2 * Math.PI * radius / step + 0.5));
        var group = new GeometryGroup { FillRule = FillRule.NonZero };
        for (var i = 0; i < n; i++)
        {
            var a = (double)i / n * 2 * Math.PI;
            group.Children.Add(new EllipseGeometry(new Rect(centre.X + radius * Math.Cos(a) - dot, centre.Y + radius * Math.Sin(a) - dot, dot * 2, dot * 2)));
        }

        return group;
    }
}

/// <summary>
/// The Zenith's scene (design system version 47 → ZenithScene; the owner, 4 Oct 2026, drawing over the Zeniths card's
/// corner: "Make it something like that"): the badge's sun and peaks set free of its ring, large and faint behind the
/// Zeniths card's content: a big red sun in halftone rings of dots behind a tall pointy peak, a smaller peak either side
/// and a low range, all rising from the card's bottom edge. The peaks are in <see cref="Ink"/>, the page's ink, lit from
/// the left, so the scene shows on dark and light pages alike; the sun keeps its red. Drawn from the design's 300 × 340 box,
/// anchored at its own bottom right; 13% strong in a dark theme, 10% in a light one. Decoration only.
/// </summary>
public sealed class GsZenithScene : Control
{
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<GsZenithScene, IBrush?>(nameof(Ink));

    private static readonly Point Sun = new(192, 112);
    private const double SunRadius = 108;

    private static readonly (double Radius, double Step, double Dot, double Opacity)[] Halo = [(118, 6.5, 2.2, 0.9), (130, 7, 2, 0.7), (143, 7.5, 1.8, 0.5), (157, 8, 1.6, 0.35)];
    private static readonly (double Radius, double Step, double Dot, double Opacity)[] Within = [(26, 6, 1.6, 0.35), (48, 6, 1.7, 0.32), (70, 6, 1.8, 0.3), (92, 6, 1.9, 0.45)];

    private static readonly IBrush Shadow = ZenithPaint.Solid("#000000", 0.28);
    private static readonly IBrush SunFill = new RadialGradientBrush
    {
        Center = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#e0503f"), 0), new GradientStop(Color.Parse("#c73a30"), 0.7), new GradientStop(Color.Parse("#b02e27"), 1) },
    };

    /// <summary>The scene's shapes, made once: the rings of dots with their brushes, the range, the peaks and their shade.</summary>
    private sealed record Shapes((Geometry Dots, IBrush Fill)[] Halo, (Geometry Dots, IBrush Fill)[] Within, Geometry Range, Geometry Peaks, Geometry Shade);

    private static readonly Lazy<Shapes> Made = new(() => new Shapes(
        Halo.Select(r => (GsZenithBadge.Dots(Sun, r), ZenithPaint.Solid("#ffffff", r.Opacity))).ToArray(),
        Within.Select((r, i) => (GsZenithBadge.Dots(Sun, r), ZenithPaint.Solid(i == Within.Length - 1 ? "#ffffff" : "#ffd9d0", r.Opacity))).ToArray(),
        Geometry.Parse("F1 M0 340L14 306 24 312 36 296 46 306 60 290 72 302 84 296 98 310 110 340ZM226 340L238 300 250 308 262 288 274 300 290 282 304 292V340Z"),
        // F1: the peaks overlap at their feet, and overlapping parts of one path are filled, not left as holes.
        Geometry.Parse("F1 M56 240L46 258 38 272 26 302 12 340 110 340 92 302 78 272 66 258ZM286 214L276 234 266 250 254 282 240 340 304 340 304 246 296 230ZM176 96L168 118 160 134 149 164 139 182 124 218 112 240 98 274 84 300 70 340 290 340 276 314 262 290 248 250 234 226 222 192 208 170 198 140 186 120Z"),
        Geometry.Parse("F1 M56 240L66 258 78 272 92 302 110 340 48 340 56 310 52 280ZM286 214L296 230 304 246 304 340 278 340 286 290 282 252ZM176 96L186 120 198 140 208 170 222 192 234 226 248 250 262 290 276 314 290 340 150 340 162 280 158 240 168 200 164 166 172 132Z")));

    static GsZenithScene() => AffectsRender<GsZenithScene>(InkProperty);

    public GsZenithScene()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The peaks' colour: the page's ink (<c>ink</c>).</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Ink is not { } ink || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var s = Made.Value;
        var scale = Math.Min(Bounds.Width / 300, Bounds.Height / 340);
        var at = new Point(Bounds.Width - 300 * scale, Bounds.Height - 340 * scale);

        // One layer, so the peaks hide the sun behind them, then faint as a whole.
        using (context.PushOpacity(ActualThemeVariant == ThemeVariant.Light ? 0.10 : 0.13))
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            foreach (var (dots, fill) in s.Halo)
            {
                context.DrawGeometry(fill, null, dots);
            }

            context.DrawEllipse(SunFill, null, Sun, SunRadius, SunRadius);
            foreach (var (dots, fill) in s.Within)
            {
                context.DrawGeometry(fill, null, dots);
            }

            using (context.PushOpacity(0.55))
            {
                context.DrawGeometry(ink, null, s.Range);
            }

            context.DrawGeometry(ink, null, s.Peaks);
            context.DrawGeometry(Shadow, null, s.Shade);
        }
    }
}

/// <summary>
/// The Zeniths monument (design system version 40; the owner, 3 Oct 2026, of the band's small medal: "I want it to be
/// bigger, take more space. It needs to look mighty, something thats monumental"): since version 49 the Zenith's banner
/// (Everest on red), nearly its box's height and centred in it, with gold light fanning out from behind it all round and a
/// glow (a deeper gold on a light page); versions 45 to 48 had the Zenith badge. With no Zenith yet, the banner stands
/// faint, without its light. Since version 50 its light moves (the owner, 7 Oct 2026: "Animate the light rays"): the rays
/// turn once a minute, a finer, fainter set between them turns the other way every 90 seconds, so the light shimmers where
/// they cross, and the glow breathes (four seconds each way), on the <see cref="Ambient"/> clock: 15 times a second, while
/// it's on screen, its window is the active one and Windows' animation effects are on; otherwise it holds still.
/// </summary>
public sealed class GsZenithMonument : Control, IAmbientLight
{
    public static readonly StyledProperty<bool> IsEarnedProperty = AvaloniaProperty.Register<GsZenithMonument, bool>(nameof(IsEarned));

    static GsZenithMonument()
    {
        AffectsRender<GsZenithMonument>(IsEarnedProperty);
        IsVisibleProperty.Changed.AddClassHandler<GsZenithMonument>((m, _) => m.Listen());
        IsEarnedProperty.Changed.AddClassHandler<GsZenithMonument>((m, _) => m.Listen());
    }

    public GsZenithMonument() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public bool IsEarned
    {
        get => GetValue(IsEarnedProperty);
        set => SetValue(IsEarnedProperty, value);
    }

    /// <summary>How many rays fan out from behind the badge, all the way round, and how wide each is (degrees).</summary>
    private const int Rays = 12;

    private const double RayWidth = 3.4;

    /// <summary>The finer rays between them: how wide each is (degrees), and how much further they reach.</summary>
    private const double FineWidth = 1.6;

    private const double FineReach = 1.1;

    /// <summary>How fast each set turns, in degrees a second: the rays clockwise once a minute, the finer ones back every 90 seconds.</summary>
    internal const double Turn = 6, FineTurn = -4;

    /// <summary>Its light's time: six minutes round, in which both sets of rays and the glow's breaths come back to the start together.</summary>
    private readonly AmbientTime _time = new(360);

    private bool _listening;

    /// <summary>Seconds on its light's time, 0 up to 360.</summary>
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

    /// <summary>Takes the clock's ticks while it's on screen with its light: a Zenith earned.</summary>
    private void Listen()
    {
        if (!IsVisible || !IsEarned || VisualRoot is null)
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

    TimeSpan IAmbientLight.Advance(double now)
    {
        if (!_time.Advance(now, Ambient.Moves(this)))
        {
            return Ambient.Waiting;
        }

        InvalidateVisual();
        return Ambient.Slow;
    }

    public override void Render(DrawingContext context)
    {
        var (w, h) = (Bounds.Width, Bounds.Height);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var tall = Math.Round(h * 0.98);
        var wide = Math.Round(tall * GsTierBanner.Aspect);
        var banner = new Rect((w - wide) / 2, (h - tall) / 2, wide, tall);
        var centre = new Point(w / 2, h / 2);
        if (!IsEarned)
        {
            using (context.PushOpacity(0.3))
            {
                GsTierBanner.Draw(context, banner, "zenith");
            }

            return;
        }

        // Gold light, a deeper gold on a light page so it shows.
        var light = ActualThemeVariant == ThemeVariant.Light;
        var hue = light ? Color.Parse("#c08a2a") : Color.Parse("#ffd76e");
        RadialGradientBrush Light(double radius, params (byte Alpha, double At)[] stops)
        {
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(centre, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
            };
            foreach (var (alpha, at) in stops)
            {
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, hue.R, hue.G, hue.B), at));
            }

            return brush;
        }

        // Twelve rays from the centre, the first `from` degrees round, each `width` degrees either side, `reach` long.
        StreamGeometry Fan(double from, double width, double reach)
        {
            var rays = new StreamGeometry();
            using var draw = rays.Open();
            for (var i = 0; i < Rays; i++)
            {
                var angle = from + i * (360.0 / Rays);
                Point At(double degrees) => new(centre.X + reach * Math.Cos(degrees * Math.PI / 180), centre.Y + reach * Math.Sin(degrees * Math.PI / 180));
                draw.BeginFigure(centre, true);
                draw.LineTo(At(angle - width));
                draw.LineTo(At(angle + width));
                draw.EndFigure(true);
            }

            return rays;
        }

        var t = _time.Seconds;
        var reach = Math.Max(w, h) * 0.78;
        context.DrawGeometry(Light(reach, (light ? (byte)110 : (byte)125, 0), (light ? (byte)40 : (byte)46, 0.5), (0, 1)), null, Fan(15 + Turn * t, RayWidth, reach));
        context.DrawGeometry(Light(reach * FineReach, (light ? (byte)72 : (byte)87, 0), (light ? (byte)22 : (byte)26, 0.5), (0, 1)), null,
            Fan(FineTurn * t, FineWidth, reach * FineReach));

        // The glow breathes: brighter and a little larger, then back, four seconds each way; still, it's at its fullest.
        var moving = Motion.On;
        var breath = moving ? (1 - Math.Cos(Math.PI * (t % 8) / 4)) / 2 : 1;
        var strength = 0.8 + 0.2 * breath;
        var glow = w * 0.46 * (moving ? 0.97 + 0.06 * breath : 1);
        context.DrawEllipse(Light(glow, ((byte)Math.Round((light ? 120 : 110) * strength), 0), ((byte)Math.Round((light ? 40 : 36) * strength), 0.55), (0, 1)), null,
            centre, glow, glow);
        GsTierBanner.Draw(context, banner, "zenith");
    }
}
