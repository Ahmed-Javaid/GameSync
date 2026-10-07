using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GameSync.UI.Controls;

/// <summary>
/// One of the design system's icons (design/system/components/Icon): outlined on a 24px grid, a 1.75 stroke with round
/// caps and joins, in the text colour around it. Only <c>play</c> is filled, and the <c>star</c> of a favourite; the stores' marks
/// (<c>steam</c>, <c>epic</c>, <c>ea</c>) are filled and never outlined.
/// </summary>
public sealed class GsIcon : Control
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsIcon, string?>(nameof(Icon));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<GsIcon, double>(nameof(Size), 20);

    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<GsIcon, double>(nameof(StrokeWidth), 1.75);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<GsIcon>();

    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<GsIcon, bool>(nameof(Filled));

    public static readonly StyledProperty<bool> IsSpinningProperty = AvaloniaProperty.Register<GsIcon, bool>(nameof(IsSpinning));

    private FrameLoop? _loop;

    static GsIcon()
    {
        AffectsRender<GsIcon>(IconProperty, StrokeWidthProperty, ForegroundProperty, FilledProperty, IsSpinningProperty);
        AffectsMeasure<GsIcon>(SizeProperty);
    }

    /// <summary>KAN-80: it turns once a second about its middle, as Sync now's arrows do while GameSync syncs; still with Windows' animation effects off.</summary>
    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSpinningProperty && IsSpinning)
        {
            (_loop ??= new FrameLoop(this, () => IsSpinning && IsVisible)).Start();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsSpinning)
        {
            (_loop ??= new FrameLoop(this, () => IsSpinning && IsVisible)).Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loop?.Stop();
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Fills the <c>star</c>, for a favourite; <c>play</c> and the <c>zenith</c> icon's star are always filled and no other icon ever is.</summary>
    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush || Icons.Geometries(Icon) is not { } paths)
        {
            return;
        }

        var scale = Size / 24;
        var turned = IsSpinning && _loop?.Motion != false ? DateTime.UtcNow.TimeOfDay.TotalMilliseconds % 1000 / 1000 * 2 * Math.PI : 0;
        using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateRotation(turned) * Matrix.CreateTranslation(12, 12) * Matrix.CreateScale(scale, scale)))
        {
            // A store's mark is filled and never outlined (KAN-55); the design system's icons are outlined.
            var brand = Icons.IsBrand(Icon);
            var pen = brand ? null : new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var fill = brand || Icon == "play" || Filled && Icon == "star" ? brush : null;
            for (var i = 0; i < paths.Length; i++)
            {
                // The Zenith's star is solid over its outlined mountain (design system version 41), with a finer edge.
                var solid = Icon == "zenith" && i == 1;
                context.DrawGeometry(solid ? brush : fill, solid ? new Pen(brush, 1, lineJoin: PenLineJoin.Round) : pen, paths[i]);
            }
        }
    }
}
