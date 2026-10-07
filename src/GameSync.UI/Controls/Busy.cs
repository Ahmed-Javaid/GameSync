using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace GameSync.UI.Controls;

/// <summary>
/// Redraws a control every frame while it's on screen and something in it moves (KAN-80): the spinner, a job's bar. It
/// stops by itself once the control leaves the window, nothing moves any more, or Windows' animation effects are off
/// (A11Y-04), and waits without drawing while the control is hidden, such as on a page kept for when it's opened again.
/// </summary>
internal sealed class FrameLoop(Visual owner, Func<bool> moving)
{
    private bool _running;
    private bool _motion = true;
    private DateTime _motionAt = DateTime.MinValue;

    /// <summary>Windows' animation effects, asked at most once a second rather than every frame.</summary>
    public bool Motion
    {
        get
        {
            if (DateTime.UtcNow - _motionAt > TimeSpan.FromSeconds(1))
            {
                (_motion, _motionAt) = (Controls.Motion.On, DateTime.UtcNow);
            }

            return _motion;
        }
    }

    public void Start()
    {
        if (!_running)
        {
            _running = true;
            Next();
        }
    }

    public void Stop() => _running = false;

    private void Next()
    {
        if (!_running)
        {
            return;
        }

        if (TopLevel.GetTopLevel(owner) is not { } top || !moving() || !Motion)
        {
            _running = false;
            owner.InvalidateVisual();
            return;
        }

        if (!owner.IsEffectivelyVisible)
        {
            DispatcherTimer.RunOnce(Next, TimeSpan.FromMilliseconds(500));
            return;
        }

        owner.InvalidateVisual();
        top.RequestAnimationFrame(_ => Next());
    }
}

/// <summary>
/// The design system's spinner (KAN-80): a ring with a quarter of it lit, turning once a second, in the text colour
/// around it. It takes a busy button's icon's place, and leads a job's progress and a busy note. With Windows' animation
/// effects off it stands still.
/// </summary>
public sealed class GsSpinner : Control
{
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<GsSpinner, double>(nameof(Size), 18);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<GsSpinner>();

    private static readonly Geometry Arc = Geometry.Parse("M12 3a9 9 0 0 1 9 9");
    private readonly FrameLoop _loop;

    static GsSpinner()
    {
        AffectsRender<GsSpinner>(ForegroundProperty);
        AffectsMeasure<GsSpinner>(SizeProperty);
    }

    public GsSpinner()
    {
        _loop = new FrameLoop(this, () => IsVisible);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _loop.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loop.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            _loop.Start();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
        {
            return;
        }

        var scale = Size / 24;
        var turned = _loop.Motion ? DateTime.UtcNow.TimeOfDay.TotalMilliseconds % 1000 / 1000 * 360 : 0;
        var stroke = 2.25;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            using (context.PushOpacity(0.28))
            {
                context.DrawEllipse(null, new Pen(brush, stroke), new Point(12, 12), 9, 9);
            }

            using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateRotation(turned * Math.PI / 180) * Matrix.CreateTranslation(12, 12)))
            {
                context.DrawGeometry(null, new Pen(brush, stroke, lineCap: PenLineCap.Round), Arc);
            }
        }
    }
}

/// <summary>
/// Words that say what GameSync is doing, with three dots counting up after them every 0.4 s ("Syncing", "Syncing.",
/// "Syncing..", "Syncing..."), always as wide as with all three, so nothing beside them moves (KAN-80). A screen reader
/// hears the words with an ellipsis. With Windows' animation effects off the three dots stay.
/// </summary>
public sealed class GsBusyText : TextBlock
{
    public static readonly StyledProperty<string?> WordsProperty = AvaloniaProperty.Register<GsBusyText, string?>(nameof(Words));

    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<GsBusyText, bool>(nameof(IsBusy), true);

    private readonly Run _words = new();
    private readonly Run _shown = new();
    private readonly Run _hidden = new() { Foreground = Brushes.Transparent };
    private DispatcherTimer? _timer;
    private int _step;

    public GsBusyText()
    {
        Inlines = [_words, _shown, _hidden];
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Words
    {
        get => GetValue(WordsProperty);
        set => SetValue(WordsProperty, value);
    }

    /// <summary>False: the words alone, as a job that's ended says them.</summary>
    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WordsProperty || change.Property == IsBusyProperty)
        {
            _words.Text = Words;
            AutomationProperties.SetName(this, IsBusy && Words is { Length: > 0 } words ? words + "…" : Words);
            Tick(restart: change.Property == IsBusyProperty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick(restart: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    private void Tick(bool restart)
    {
        var counting = IsBusy && VisualRoot is not null && Motion.On;
        if (!counting)
        {
            _timer?.Stop();
            _timer = null;
            (_shown.Text, _hidden.Text) = IsBusy ? ("...", "") : ("", "");
            return;
        }

        if (restart)
        {
            _step = 0;
        }

        (_shown.Text, _hidden.Text) = (new string('.', _step), new string('.', 3 - _step));
        if (_timer is null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
            {
                _step = (_step + 1) % 4;
                Tick(restart: false);
            });
            _timer.Start();
        }
    }
}

/// <summary>
/// A job's bar (design system → JobProgress): a 6px track in <c>bg-400</c> and the fill in <c>primary</c>, or in the
/// colour of how it ended. While the job runs a light sweeps along the fill, so it moves even while the bytes wait; with
/// no value yet, a stripe slides along the track. With Windows' animation effects off nothing moves, and a bar with no
/// value yet fills faintly (KAN-80).
/// </summary>
public sealed class GsJobBar : Control
{
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<GsJobBar, double?>(nameof(Value));

    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<GsJobBar, bool>(nameof(IsRunning), true);

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<GsJobBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<GsJobBar, IBrush?>(nameof(Track));

    private static readonly IBrush Sheen = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(77, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)],
    }.ToImmutable();

    private readonly FrameLoop _loop;

    static GsJobBar()
    {
        AffectsRender<GsJobBar>(ValueProperty, IsRunningProperty, FillProperty, TrackProperty);
    }

    public GsJobBar()
    {
        _loop = new FrameLoop(this, () => IsRunning && IsVisible);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    /// <summary>0 to 100; null while how much there is isn't known yet.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 6);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _loop.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loop.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == IsRunningProperty || change.Property == IsVisibleProperty) && IsRunning && IsVisible)
        {
            _loop.Start();
        }
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var track = new RoundedRect(new Rect(0, 0, width, 6), 3);
        if (Track is { } trackBrush)
        {
            context.DrawRectangle(trackBrush, null, track);
        }

        if (Fill is not { } fill)
        {
            return;
        }

        var moving = IsRunning && _loop.Motion;
        var seconds = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        using (context.PushClip(track))
        {
            if (Value is not { } value)
            {
                if (!moving)
                {
                    using (context.PushOpacity(0.4))
                    {
                        context.DrawRectangle(fill, null, track);
                    }

                    return;
                }

                // A stripe a third of the track wide, eased from beyond the left edge to beyond the right, every 1.4 s.
                var t = seconds % 1.4 / 1.4;
                var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                var stripe = width * 0.32;
                var x = -stripe * 1.05 + (width + stripe * 1.05) * eased;
                context.DrawRectangle(fill, null, new RoundedRect(new Rect(x, 0, stripe, 6), 3));
                return;
            }

            var filled = width * Math.Clamp(value, 0, 100) / 100;
            if (filled <= 0)
            {
                return;
            }

            var bar = new RoundedRect(new Rect(0, 0, filled, 6), 3);
            context.DrawRectangle(fill, null, bar);
            if (moving)
            {
                // A light 40% of the fill wide sweeps along it every 1.6 s.
                var band = filled * 0.4;
                var at = -band + (filled + band) * (seconds % 1.6 / 1.6);
                using (context.PushClip(bar))
                {
                    context.DrawRectangle(Sheen, null, new Rect(at, 0, band, 6));
                }
            }
        }
    }
}

/// <summary>
/// A job under way, where its result will show (design system → JobProgress; KAN-80): a spinner and what it's doing, a
/// bar, how far it is, how fast and how long is left. Paused, failed and done stop the spinner and say why or how it
/// ended. Class <c>boxed</c> sets it on a card of its own, as on a game's saves.
/// </summary>
public class GsJobProgress : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsJobProgress, string?>(nameof(Title));

    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<GsJobProgress, double?>(nameof(Value));

    public static readonly StyledProperty<string?> DetailProperty = AvaloniaProperty.Register<GsJobProgress, string?>(nameof(Detail));

    public static readonly StyledProperty<string?> SpeedProperty = AvaloniaProperty.Register<GsJobProgress, string?>(nameof(Speed));

    public static readonly StyledProperty<string?> LeftProperty = AvaloniaProperty.Register<GsJobProgress, string?>(nameof(Left));

    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<GsJobProgress, string?>(nameof(Note));

    /// <summary><c>running</c> (the default), <c>paused</c>, <c>done</c> or <c>failed</c>.</summary>
    public static readonly StyledProperty<string> StateProperty = AvaloniaProperty.Register<GsJobProgress, string>(nameof(State), "running");

    public static readonly DirectProperty<GsJobProgress, bool> IsRunningProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, bool>(nameof(IsRunning), p => p.IsRunning);

    public static readonly DirectProperty<GsJobProgress, string> IconNameProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, string>(nameof(IconName), p => p.IconName);

    public static readonly DirectProperty<GsJobProgress, string?> RightTextProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, string?>(nameof(RightText), p => p.RightText);

    public static readonly DirectProperty<GsJobProgress, string?> FootTextProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, string?>(nameof(FootText), p => p.FootText);

    public static readonly DirectProperty<GsJobProgress, bool> ShowsLeftProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, bool>(nameof(ShowsLeft), p => p.ShowsLeft);

    public static readonly DirectProperty<GsJobProgress, bool> ShowsNoteProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, bool>(nameof(ShowsNote), p => p.ShowsNote);

    public static readonly DirectProperty<GsJobProgress, double?> BarValueProperty =
        AvaloniaProperty.RegisterDirect<GsJobProgress, double?>(nameof(BarValue), p => p.BarValue);

    private bool _isRunning = true;
    private string _iconName = "sync";
    private string? _rightText;
    private string? _footText;
    private bool _showsLeft;
    private bool _showsNote;
    private double? _barValue;
    private string? _spoken;

    public GsJobProgress() => Update();

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>0 to 100; null while how much there is isn't known yet.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>How far it is, in mono: "42.1 of 113.0 MB · 412 of 1,108 files".</summary>
    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>"6.1 MB/s", once there's enough to say; the percentage stands in until then.</summary>
    public string? Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    /// <summary>"about 12 s left".</summary>
    public string? Left
    {
        get => GetValue(LeftProperty);
        set => SetValue(LeftProperty, value);
    }

    /// <summary>Why it's paused, or how it ended: "Paused while you play Bloodborne GOTY", "113 MB in 19 s".</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public string State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetAndRaise(IsRunningProperty, ref _isRunning, value);
    }

    public string IconName
    {
        get => _iconName;
        private set => SetAndRaise(IconNameProperty, ref _iconName, value);
    }

    public string? RightText
    {
        get => _rightText;
        private set => SetAndRaise(RightTextProperty, ref _rightText, value);
    }

    public string? FootText
    {
        get => _footText;
        private set => SetAndRaise(FootTextProperty, ref _footText, value);
    }

    public bool ShowsLeft
    {
        get => _showsLeft;
        private set => SetAndRaise(ShowsLeftProperty, ref _showsLeft, value);
    }

    /// <summary>It's stopped and says why or how it ended: prose, wrapping, in place of how far.</summary>
    public bool ShowsNote
    {
        get => _showsNote;
        private set => SetAndRaise(ShowsNoteProperty, ref _showsNote, value);
    }

    /// <summary>Full once it's done; as it was when it paused or failed.</summary>
    public double? BarValue
    {
        get => _barValue;
        private set => SetAndRaise(BarValueProperty, ref _barValue, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == ValueProperty || change.Property == DetailProperty || change.Property == SpeedProperty ||
            change.Property == LeftProperty || change.Property == NoteProperty || change.Property == StateProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var state = State;
        IsRunning = state == "running";
        IconName = state switch { "paused" => "pause", "done" => "check", "failed" => "alert", _ => "sync" };
        foreach (var s in new[] { "running", "paused", "done", "failed" })
        {
            PseudoClasses.Set(":" + s, s == state);
        }

        BarValue = state == "done" ? 100 : Value;
        RightText = IsRunning ? Speed ?? (Value is { } v ? $"{Math.Round(v).ToString(CultureInfo.InvariantCulture)}%" : null) : null;
        ShowsNote = !IsRunning && Note is not null;
        FootText = ShowsNote ? Note : Detail;
        ShowsLeft = IsRunning && Left is not null;

        // A screen reader hears what it's doing and how far, every tenth of the way and when it ends, not every report.
        var tenth = Value is { } p ? (int)(p / 10) * 10 : -1;
        var spoken = IsRunning
            ? tenth >= 0 ? $"{Title}, {tenth.ToString(CultureInfo.InvariantCulture)}%" : Title
            : $"{Title}. {FootText}";
        if (spoken != _spoken)
        {
            _spoken = spoken;
            AutomationProperties.SetName(this, spoken);
        }
    }
}
