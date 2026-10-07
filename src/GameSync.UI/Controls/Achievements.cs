using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GameSync.UI.Controls;

/// <summary>The track and the arc of a <see cref="GsProgressRing"/>, drawn round-ended from the top, clockwise.</summary>
public sealed class GsRingArc : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<GsRingArc, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<GsRingArc, double>(nameof(Thickness), 8);

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<GsRingArc, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> ArcProperty = AvaloniaProperty.Register<GsRingArc, IBrush?>(nameof(Arc));

    static GsRingArc() => AffectsRender<GsRingArc>(ValueProperty, ThicknessProperty, TrackProperty, ArcProperty);

    /// <summary>0 to 100.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IBrush? Arc
    {
        get => GetValue(ArcProperty);
        set => SetValue(ArcProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var t = Math.Min(Thickness, size / 2);
        if (size <= 0 || t <= 0)
        {
            return;
        }

        var r = (size - t) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Track is { } track)
        {
            context.DrawEllipse(null, new Pen(track, t), c, r, r);
        }

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0 || Arc is not { } arc)
        {
            return;
        }

        var pen = new Pen(arc, t, lineCap: PenLineCap.Round);
        if (value >= 99.95)
        {
            context.DrawEllipse(null, pen, c, r, r);
            return;
        }

        var sweep = value / 100 * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + (r * Math.Sin(sweep)), c.Y - (r * Math.Cos(sweep)));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>
/// A ring of progress (design system → ProgressRing): a game's achievements unlocked around its percent, with
/// <see cref="Sub"/> under it ("123 / 171"). <see cref="IsZenith"/> is a game's 100% (version 49; platinum before): the ring
/// gold at the top running to deep red at the bottom and back, with its light (<see cref="GsZenithGlow"/>): faint rays and a
/// breathing glow behind it, a glint over it. <see cref="PlayMoment"/> plays the first time a game's 100% is seen.
/// </summary>
public class GsProgressRing : TemplatedControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<GsProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GsProgressRing, string?>(nameof(Label));

    public static readonly StyledProperty<string?> SubProperty = AvaloniaProperty.Register<GsProgressRing, string?>(nameof(Sub));

    public static readonly StyledProperty<bool> IsZenithProperty = AvaloniaProperty.Register<GsProgressRing, bool>(nameof(IsZenith));

    public static readonly StyledProperty<double> RingThicknessProperty = AvaloniaProperty.Register<GsProgressRing, double>(nameof(RingThickness), 10);

    public static readonly StyledProperty<double> LabelSizeProperty = AvaloniaProperty.Register<GsProgressRing, double>(nameof(LabelSize), 24);

    public static readonly StyledProperty<double> SubSizeProperty = AvaloniaProperty.Register<GsProgressRing, double>(nameof(SubSize), 11);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>In the middle: the percent unless given.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Sub
    {
        get => GetValue(SubProperty);
        set => SetValue(SubProperty, value);
    }

    public bool IsZenith
    {
        get => GetValue(IsZenithProperty);
        set => SetValue(IsZenithProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public double LabelSize
    {
        get => GetValue(LabelSizeProperty);
        set => SetValue(LabelSizeProperty, value);
    }

    public double SubSize
    {
        get => GetValue(SubSizeProperty);
        set => SetValue(SubSizeProperty, value);
    }

    /// <summary>The ring is this small or smaller: <see cref="Sub"/> goes under it, outside, where it has room.</summary>
    public const double SubUnderBelow = 96;

    public static readonly DirectProperty<GsProgressRing, bool> ShowsSubInsideProperty =
        AvaloniaProperty.RegisterDirect<GsProgressRing, bool>(nameof(ShowsSubInside), r => r.ShowsSubInside);

    public static readonly DirectProperty<GsProgressRing, bool> ShowsSubUnderProperty =
        AvaloniaProperty.RegisterDirect<GsProgressRing, bool>(nameof(ShowsSubUnder), r => r.ShowsSubUnder);

    public static readonly DirectProperty<GsProgressRing, Thickness> GlowMarginProperty =
        AvaloniaProperty.RegisterDirect<GsProgressRing, Thickness>(nameof(GlowMargin), r => r.GlowMargin);

    private bool _showsSubInside;
    private bool _showsSubUnder;
    private Thickness _glowMargin = new(-14);

    /// <summary>How far a finished ring's halo reaches past it, the same on every side (KAN-121): a seventh of the ring. Unused since version 49: the light draws past its box itself.</summary>
    public Thickness GlowMargin
    {
        get => _glowMargin;
        private set => SetAndRaise(GlowMarginProperty, ref _glowMargin, value);
    }

    /// <summary>
    /// The Zenith's moment (version 49): the ring fills its last stretch, pops a little larger as it flashes, light bursts
    /// out with gold sparks, and its glow and rays come up. Nothing moves with Windows' animation effects off.
    /// </summary>
    public void PlayMoment()
    {
        if (!Motion.On || _arc is not { } arc || _box is not { } box)
        {
            return;
        }

        _ = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(GsZenithMoment.Burst),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(GsRingArc.ValueProperty, 97d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(GsRingArc.ValueProperty, 100d) } },
            },
        }.RunAsync(arc);
        _ = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(0.5),
            Delay = TimeSpan.FromSeconds(GsZenithMoment.Burst),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 1d), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.35), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 1.07), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1.07) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 1d), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1d) } },
            },
        }.RunAsync(box);
        if (_light is { } light)
        {
            _ = new Avalonia.Animation.Animation
            {
                Duration = TimeSpan.FromSeconds(0.9),
                Delay = TimeSpan.FromSeconds(1.2),
                FillMode = Avalonia.Animation.FillMode.Backward,
                Children =
                {
                    new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
                    new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
                },
            }.RunAsync(light);
        }

        GsZenithMoment.PlayOver(box, RingThickness);
    }

    private GsRingArc? _arc;
    private Control? _box;
    private Control? _light;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _arc = e.NameScope.Find<GsRingArc>("PART_Arc");
        _box = e.NameScope.Find<Control>("PART_Box");
        _light = e.NameScope.Find<Control>("PART_Light");
    }

    /// <summary>The line under the percent, inside the ring: a ring of 96px or more has room for it.</summary>
    public bool ShowsSubInside
    {
        get => _showsSubInside;
        private set => SetAndRaise(ShowsSubInsideProperty, ref _showsSubInside, value);
    }

    /// <summary>The line under the ring, outside it (design system version 35: Home's "123 / 171" ran into its 76px ring).</summary>
    public bool ShowsSubUnder
    {
        get => _showsSubUnder;
        private set => SetAndRaise(ShowsSubUnderProperty, ref _showsSubUnder, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsZenithProperty)
        {
            PseudoClasses.Set(":zenith", IsZenith);
        }
        else if ((change.Property == WidthProperty || change.Property == SubProperty) && !double.IsNaN(Width))
        {
            // The design's proportions: an eleventh of the ring for its stroke, the percent at about a quarter of it.
            var under = Sub is not null && Width < SubUnderBelow;
            ShowsSubInside = Sub is not null && !under;
            ShowsSubUnder = under;
            RingThickness = Math.Max(4, Math.Round(Width / 11));
            GlowMargin = new Thickness(-Math.Round(Width / 7));
            LabelSize = Math.Round(Width * (ShowsSubInside ? 0.22 : 0.26));
            SubSize = Math.Max(10, Math.Round(Width * 0.1));
        }
    }
}

/// <summary>
/// An achievement's icon in its metal (design system → AchievementBadge): <see cref="State"/> unlocked, locked (Steam's own
/// grey icon, dimmed, with a lock) or hidden (a "?": nothing about it shows until it's unlocked); <see cref="Tier"/>
/// gold, silver or bronze rings an unlocked one, and Gold glows; <see cref="Size"/> sm (40), md (64) or lg (104).
/// </summary>
public class GsAchievementBadge : TemplatedControl
{
    public static readonly StyledProperty<IImage?> IconProperty = AvaloniaProperty.Register<GsAchievementBadge, IImage?>(nameof(Icon));

    public static readonly StyledProperty<string> StateProperty = AvaloniaProperty.Register<GsAchievementBadge, string>(nameof(State), "unlocked");

    public static readonly StyledProperty<string?> TierProperty = AvaloniaProperty.Register<GsAchievementBadge, string?>(nameof(Tier));

    public static readonly StyledProperty<string> SizeProperty = AvaloniaProperty.Register<GsAchievementBadge, string>(nameof(Size), "md");

    public GsAchievementBadge()
    {
        Classes.Add("md");
        UpdateState();
    }

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Tier
    {
        get => GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public string Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty || change.Property == TierProperty || change.Property == IconProperty)
        {
            UpdateState();
        }
        else if (change.Property == SizeProperty)
        {
            Classes.Set("sm", Size == "sm");
            Classes.Set("md", Size == "md");
            Classes.Set("lg", Size == "lg");
        }
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":locked", State == "locked");
        PseudoClasses.Set(":hidden", State == "hidden");

        // No icon kept yet: the trophy in its place. A hidden one shows its "?" instead.
        PseudoClasses.Set(":noicon", Icon is null && State != "hidden");
        var tier = State == "unlocked" ? Tier : null;
        PseudoClasses.Set(":gold", tier == "gold");
        PseudoClasses.Set(":silver", tier == "silver");
        PseudoClasses.Set(":bronze", tier == "bronze");
    }
}

/// <summary>A tier in words with its metal dot (design system → AchievementBadge): "Gold", or a count of them ("12 Gold").</summary>
public class GsTierChip : TemplatedControl
{
    public static readonly StyledProperty<string> TierProperty = AvaloniaProperty.Register<GsTierChip, string>(nameof(Tier), "bronze");

    public static readonly StyledProperty<string?> CountProperty = AvaloniaProperty.Register<GsTierChip, string?>(nameof(Count));

    /// <summary>For the template's binding to the word.</summary>
    public static readonly DirectProperty<GsTierChip, string> WordProperty = AvaloniaProperty.RegisterDirect<GsTierChip, string>(nameof(Word), c => c.Word);

    private string _word = "Bronze";

    public GsTierChip() => PseudoClasses.Set(":bronze", true);

    public string Tier
    {
        get => GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public string? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>The tier's word.</summary>
    public string Word
    {
        get => _word;
        private set => SetAndRaise(WordProperty, ref _word, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TierProperty)
        {
            foreach (var tier in (string[])["bronze", "silver", "gold", "zenith"])
            {
                PseudoClasses.Set(":" + tier, Tier == tier);
            }

            Word = Tier switch
            {
                "gold" => "Gold",
                "silver" => "Silver",
                "zenith" => "Zenith",
                _ => "Bronze",
            };
        }
    }
}

/// <summary>
/// A game's Zenith, its 100% (Platinum until the owner renamed it, 3 Oct 2026; design system → AchievementBadge): earned,
/// since version 49 the Zenith's banner (<see cref="GsTierBanner"/>: Everest on red, the sun at its height, a laurel) in its
/// box with a warm glow under it; <see cref="Unfurl"/> drops it open from its bar (the moment, the popup:
/// <see cref="UnfurlsOnShow"/>), still with Windows' animations off. Not yet, the banner's outline, dashed, round the
/// Zenith's mark. Versions 44 to 48 had the Zenith badge (<see cref="GsZenithBadge"/>) with a sheen.
/// </summary>
public class GsZenithMedal : TemplatedControl
{
    public static readonly StyledProperty<bool> IsEarnedProperty = AvaloniaProperty.Register<GsZenithMedal, bool>(nameof(IsEarned));


    public static readonly StyledProperty<double> GlyphSizeProperty = AvaloniaProperty.Register<GsZenithMedal, double>(nameof(GlyphSize), 18);

    /// <summary>Unfurls as it shows: the popup's Zenith.</summary>
    public static readonly StyledProperty<bool> UnfurlsOnShowProperty = AvaloniaProperty.Register<GsZenithMedal, bool>(nameof(UnfurlsOnShow));

    public bool UnfurlsOnShow
    {
        get => GetValue(UnfurlsOnShowProperty);
        set => SetValue(UnfurlsOnShowProperty, value);
    }

    public bool IsEarned
    {
        get => GetValue(IsEarnedProperty);
        set => SetValue(IsEarnedProperty, value);
    }

    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    /// <summary>An earned medal this size or larger has its glow; a smaller one (a table's row) has none.</summary>
    public const double GlowFrom = 30;

    public static readonly DirectProperty<GsZenithMedal, bool> ShowsGlowProperty =
        AvaloniaProperty.RegisterDirect<GsZenithMedal, bool>(nameof(ShowsGlow), m => m.ShowsGlow);

    public static readonly DirectProperty<GsZenithMedal, Thickness> GlowMarginProperty =
        AvaloniaProperty.RegisterDirect<GsZenithMedal, Thickness>(nameof(GlowMargin), m => m.GlowMargin);

    private bool _showsGlow;
    private Thickness _glowMargin;
    private Control? _banner;

    /// <summary>The round glow under it (KAN-106: the drop shadow it had was cut off square by its own box).</summary>
    public bool ShowsGlow
    {
        get => _showsGlow;
        private set => SetAndRaise(ShowsGlowProperty, ref _showsGlow, value);
    }

    /// <summary>How far the glow spills past the medal: the same on every side, so it's centred on it (KAN-118).</summary>
    public Thickness GlowMargin
    {
        get => _glowMargin;
        private set => SetAndRaise(GlowMarginProperty, ref _glowMargin, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _banner = e.NameScope.Find<Control>("PART_Banner");
        if (UnfurlsOnShow)
        {
            Unfurl(TimeSpan.Zero);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEarnedProperty)
        {
            PseudoClasses.Set(":earned", IsEarned);
            UpdateGlow();
        }
        else if (change.Property == WidthProperty && !double.IsNaN(Width))
        {
            GlyphSize = Math.Round(Width * 0.5);
            UpdateGlow();
        }
    }

    private void UpdateGlow()
    {
        var size = double.IsNaN(Width) ? 40 : Width;
        ShowsGlow = IsEarned && size >= GlowFrom;
        GlowMargin = new Thickness(-Math.Round(size * 0.18));
    }

    /// <summary>
    /// The banner drops open from its bar after <paramref name="delay"/>, overshooting a little and settling (the design's
    /// gs-unfurl); hidden until then. Nothing moves with Windows' animation effects off.
    /// </summary>
    public void Unfurl(TimeSpan delay)
    {
        if (_banner is not { } banner || !IsEarned || !Motion.On)
        {
            return;
        }

        _ = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(0.7),
            Delay = delay,
            FillMode = Avalonia.Animation.FillMode.Backward,
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 0d), new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.15), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.7), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1.05) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1d), new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
            },
        }.RunAsync(banner);
    }
}

/// <summary>Diagonal stripes, as a hidden achievement's face has: <see cref="Brush"/> every 12px, half of each.</summary>
public sealed class GsHatch : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<GsHatch, IBrush?>(nameof(Brush));

    static GsHatch() => AffectsRender<GsHatch>(BrushProperty);

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Brush is not { } brush)
        {
            return;
        }

        // Lines at 135 degrees, 6px thick and 12px apart across them, as the design's repeating gradient draws them.
        const double Period = 12;
        var pen = new Pen(brush, Period / 2);
        var (w, h) = (Bounds.Width, Bounds.Height);
        var step = Period * Math.Sqrt(2);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            for (var x = -h; x < w + h; x += step)
            {
                context.DrawLine(pen, new Point(x, h), new Point(x + h, 0));
            }
        }
    }
}

/// <summary>
/// How far a game is (design system → AchievementsOverview): its ring, the tiers unlocked, and its Zenith, earned or how
/// many to go. Large on the game's page and its achievements, the Zenith its banner; <see cref="IsSmall"/> on Home, with
/// the tiers in a column and, since version 52, the Zenith's dot under them (an empty ring until it's earned).
/// </summary>
public class GsAchievementsOverview : TemplatedControl
{
    public static readonly StyledProperty<int> DoneProperty = AvaloniaProperty.Register<GsAchievementsOverview, int>(nameof(Done));

    public static readonly StyledProperty<int> TotalProperty = AvaloniaProperty.Register<GsAchievementsOverview, int>(nameof(Total));

    public static readonly StyledProperty<int> GoldProperty = AvaloniaProperty.Register<GsAchievementsOverview, int>(nameof(Gold));

    public static readonly StyledProperty<int> SilverProperty = AvaloniaProperty.Register<GsAchievementsOverview, int>(nameof(Silver));

    public static readonly StyledProperty<int> BronzeProperty = AvaloniaProperty.Register<GsAchievementsOverview, int>(nameof(Bronze));

    /// <summary>The day Zenith was earned ("2 Oct"), once every one is unlocked; null when Steam kept no time.</summary>
    public static readonly StyledProperty<string?> CompletedOnProperty = AvaloniaProperty.Register<GsAchievementsOverview, string?>(nameof(CompletedOn));

    public static readonly StyledProperty<bool> IsSmallProperty = AvaloniaProperty.Register<GsAchievementsOverview, bool>(nameof(IsSmall));

    /// <summary>
    /// Version 49: this is the first time this game's 100% is seen on this PC, so its moment plays (the ring's, then the
    /// banner unfurling, or on Home the Zenith's dot popping in), once; <see cref="MomentPlayedCommand"/> then says so, for
    /// it not to play again.
    /// </summary>
    public static readonly StyledProperty<bool> PlaysMomentProperty = AvaloniaProperty.Register<GsAchievementsOverview, bool>(nameof(PlaysMoment));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> MomentPlayedCommandProperty =
        AvaloniaProperty.Register<GsAchievementsOverview, System.Windows.Input.ICommand?>(nameof(MomentPlayedCommand));

    public bool PlaysMoment
    {
        get => GetValue(PlaysMomentProperty);
        set => SetValue(PlaysMomentProperty, value);
    }

    public System.Windows.Input.ICommand? MomentPlayedCommand
    {
        get => GetValue(MomentPlayedCommandProperty);
        set => SetValue(MomentPlayedCommandProperty, value);
    }

    private GsProgressRing? _ring;
    private GsZenithMedal? _medal;
    private Control? _dot;
    private bool _played;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _ring = e.NameScope.Find<GsProgressRing>("PART_Ring");
        _medal = e.NameScope.Find<GsZenithMedal>("PART_Medal");
        _dot = e.NameScope.Find<Control>("PART_ZenithDot");
        Avalonia.Threading.Dispatcher.UIThread.Post(Play, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The moment, once, when it's wanted and the game is at 100%; the parts' own templates are in by then. It waits until
    /// the ring is wholly on screen: a card below the fold plays it when it's scrolled to (Counter-Strike 2's page in a
    /// smaller window played it out of sight, 6 Oct 2026, and then it was seen).
    /// </summary>
    private void Play()
    {
        if (!PlaysMoment || !IsZenith || _played || _ring is not { } ring || VisualRoot is null)
        {
            WatchRing(false);
            return;
        }

        if (!WhollyOnScreen(ring))
        {
            WatchRing(true);
            return;
        }

        WatchRing(false);
        _played = true;
        ring.PlayMoment();
        if (IsSmall)
        {
            PopDot(TimeSpan.FromSeconds(1.15));
        }
        else
        {
            _medal?.Unfurl(TimeSpan.FromSeconds(1.15));
        }

        if (MomentPlayedCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>
    /// Home's Zenith dot (version 52) pops in after <paramref name="delay"/>, growing past its size and settling (the
    /// design's gs-dot-pop), where a game's page unfurls the banner; hidden until then. Still with Windows' animation
    /// effects off.
    /// </summary>
    private void PopDot(TimeSpan delay)
    {
        if (_dot is not { } dot || !Motion.On)
        {
            return;
        }

        _ = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(0.6),
            Delay = delay,
            FillMode = Avalonia.Animation.FillMode.Backward,
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 0d), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 0d), new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.2), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.6), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 1.6), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1.6) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(ScaleTransform.ScaleXProperty, 1d), new Avalonia.Styling.Setter(ScaleTransform.ScaleYProperty, 1d), new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
            },
        }.RunAsync(dot);
    }

    private GsProgressRing? _watched;

    /// <summary>Asks to hear when what's visible of the ring changes (the page scrolling, the window growing), or stops.</summary>
    private void WatchRing(bool watch)
    {
        var ring = watch ? _ring : null;
        if (ring == _watched)
        {
            return;
        }

        if (_watched is { } old)
        {
            old.EffectiveViewportChanged -= RingSeenMore;
        }

        _watched = ring;
        if (ring is not null)
        {
            ring.EffectiveViewportChanged += RingSeenMore;
        }
    }

    private void RingSeenMore(object? sender, EffectiveViewportChangedEventArgs e) => Play();

    /// <summary>The whole of <paramref name="visual"/> is in the window, inside every scrolling area it's in.</summary>
    private static bool WhollyOnScreen(Visual visual)
    {
        if (TopLevel.GetTopLevel(visual) is not { } root || visual.TransformToVisual(root) is not { } toRoot || visual.Bounds.Width <= 0)
        {
            return false;
        }

        var shown = new Rect(root.Bounds.Size);
        for (var parent = visual.GetVisualParent(); parent is not null && parent != root; parent = parent.GetVisualParent())
        {
            if (parent.ClipToBounds && parent.TransformToVisual(root) is { } clip)
            {
                shown = shown.Intersect(new Rect(parent.Bounds.Size).TransformToAABB(clip));
            }
        }

        return shown.Inflate(1).Contains(new Rect(visual.Bounds.Size).TransformToAABB(toRoot));
    }

    public static readonly DirectProperty<GsAchievementsOverview, double> PercentProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, double>(nameof(Percent), o => o.Percent);

    public static readonly DirectProperty<GsAchievementsOverview, string> PercentLabelProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, string>(nameof(PercentLabel), o => o.PercentLabel);

    public static readonly DirectProperty<GsAchievementsOverview, string> CountLabelProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, string>(nameof(CountLabel), o => o.CountLabel);

    public static readonly DirectProperty<GsAchievementsOverview, bool> IsZenithProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, bool>(nameof(IsZenith), o => o.IsZenith);

    public static readonly DirectProperty<GsAchievementsOverview, string> ZenithTitleProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, string>(nameof(ZenithTitle), o => o.ZenithTitle);

    public static readonly DirectProperty<GsAchievementsOverview, string> ZenithSubProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, string>(nameof(ZenithSub), o => o.ZenithSub);

    public static readonly DirectProperty<GsAchievementsOverview, string> SpokenProperty =
        AvaloniaProperty.RegisterDirect<GsAchievementsOverview, string>(nameof(Spoken), o => o.Spoken);

    private double _percent;
    private string _percentLabel = "0%";
    private string _countLabel = "0 / 0";
    private bool _isZenith;
    private string _zenithTitle = "";
    private string _zenithSub = "";
    private string _spoken = "";

    public GsAchievementsOverview() => Update();

    public int Done
    {
        get => GetValue(DoneProperty);
        set => SetValue(DoneProperty, value);
    }

    public int Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public int Gold
    {
        get => GetValue(GoldProperty);
        set => SetValue(GoldProperty, value);
    }

    public int Silver
    {
        get => GetValue(SilverProperty);
        set => SetValue(SilverProperty, value);
    }

    public int Bronze
    {
        get => GetValue(BronzeProperty);
        set => SetValue(BronzeProperty, value);
    }

    public string? CompletedOn
    {
        get => GetValue(CompletedOnProperty);
        set => SetValue(CompletedOnProperty, value);
    }

    public bool IsSmall
    {
        get => GetValue(IsSmallProperty);
        set => SetValue(IsSmallProperty, value);
    }

    public double Percent
    {
        get => _percent;
        private set => SetAndRaise(PercentProperty, ref _percent, value);
    }

    /// <summary>In the ring: "72%", rounded down so it never says 100% before Zenith.</summary>
    public string PercentLabel
    {
        get => _percentLabel;
        private set => SetAndRaise(PercentLabelProperty, ref _percentLabel, value);
    }

    /// <summary>Under it: "123 / 171".</summary>
    public string CountLabel
    {
        get => _countLabel;
        private set => SetAndRaise(CountLabelProperty, ref _countLabel, value);
    }

    /// <summary>Every one unlocked: the ring turns platinum, and the medal is earned.</summary>
    public bool IsZenith
    {
        get => _isZenith;
        private set => SetAndRaise(IsZenithProperty, ref _isZenith, value);
    }

    /// <summary>"Zenith", or "48 to go for its Zenith".</summary>
    public string ZenithTitle
    {
        get => _zenithTitle;
        private set => SetAndRaise(ZenithTitleProperty, ref _zenithTitle, value);
    }

    public string ZenithSub
    {
        get => _zenithSub;
        private set => SetAndRaise(ZenithSubProperty, ref _zenithSub, value);
    }

    /// <summary>What a screen reader says for the ring.</summary>
    public string Spoken
    {
        get => _spoken;
        private set => SetAndRaise(SpokenProperty, ref _spoken, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DoneProperty || change.Property == TotalProperty || change.Property == CompletedOnProperty)
        {
            Update();
        }
        else if (change.Property == IsSmallProperty)
        {
            PseudoClasses.Set(":small", IsSmall);
        }

        if (change.Property == PlaysMomentProperty || change.Property == DoneProperty || change.Property == TotalProperty)
        {
            // Another game's card (Home's banner moving on) may want its own moment.
            if (!PlaysMoment)
            {
                _played = false;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(Play, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void Update()
    {
        var done = Math.Clamp(Done, 0, Math.Max(0, Total));
        Percent = Total > 0 ? 100.0 * done / Total : 0;
        IsZenith = Total > 0 && done >= Total;
        PseudoClasses.Set(":zenith", IsZenith);
        PercentLabel = Math.Floor(Percent).ToString(CultureInfo.InvariantCulture) + "%";
        CountLabel = $"{done.ToString(CultureInfo.InvariantCulture)} / {Total.ToString(CultureInfo.InvariantCulture)}";
        ZenithTitle = IsZenith ? "Zenith" : $"{(Total - done).ToString(CultureInfo.InvariantCulture)} to go for its Zenith";
        ZenithSub = IsZenith ? $"Every achievement, {CompletedOn ?? "unlocked"}" : "Every achievement unlocked earns it";
        Spoken = $"{done.ToString(CultureInfo.InvariantCulture)} of {Total.ToString(CultureInfo.InvariantCulture)} achievements unlocked, {PercentLabel}";
    }
}

/// <summary>
/// An achievement in its own light (design system → AchievementSpotlight): its badge large in its metal, what it is, and
/// how few players have it; a game's rarest unlocked, by default. <see cref="IsSmall"/>: a smaller badge, no description.
/// </summary>
public class GsAchievementSpotlight : TemplatedControl
{
    public static readonly StyledProperty<IImage?> IconProperty = AvaloniaProperty.Register<GsAchievementSpotlight, IImage?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsAchievementSpotlight, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<GsAchievementSpotlight, string?>(nameof(Description));

    /// <summary>gold, silver or bronze; null while its rarity isn't known.</summary>
    public static readonly StyledProperty<string?> TierProperty = AvaloniaProperty.Register<GsAchievementSpotlight, string?>(nameof(Tier));

    /// <summary>"Very rare · 2.7% of players".</summary>
    public static readonly StyledProperty<string?> MetaProperty = AvaloniaProperty.Register<GsAchievementSpotlight, string?>(nameof(Meta));

    public static readonly StyledProperty<string> EyebrowProperty = AvaloniaProperty.Register<GsAchievementSpotlight, string>(nameof(Eyebrow), "RAREST YOU HAVE");

    public static readonly StyledProperty<bool> IsSmallProperty = AvaloniaProperty.Register<GsAchievementSpotlight, bool>(nameof(IsSmall));

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Tier
    {
        get => GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    /// <summary>Above its name, in capitals as an overline is.</summary>
    public string Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public bool IsSmall
    {
        get => GetValue(IsSmallProperty);
        set => SetValue(IsSmallProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TierProperty)
        {
            PseudoClasses.Set(":gold", Tier == "gold");
            PseudoClasses.Set(":silver", Tier == "silver");
            PseudoClasses.Set(":bronze", Tier == "bronze");
        }
        else if (change.Property == IsSmallProperty)
        {
            PseudoClasses.Set(":small", IsSmall);
        }
    }
}
