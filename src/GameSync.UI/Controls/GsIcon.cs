using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GameSync.UI.Controls;

/// <summary>
/// One of the design system's icons (design/system/components/Icon): outlined on a 24px grid, a 1.75 stroke with round
/// caps and joins, in the text colour around it. Only <c>play</c> is filled, and the <c>star</c> of a favourite.
/// </summary>
public sealed class GsIcon : Control
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsIcon, string?>(nameof(Icon));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<GsIcon, double>(nameof(Size), 20);

    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<GsIcon, double>(nameof(StrokeWidth), 1.75);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<GsIcon>();

    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<GsIcon, bool>(nameof(Filled));

    static GsIcon()
    {
        AffectsRender<GsIcon>(IconProperty, StrokeWidthProperty, ForegroundProperty, FilledProperty);
        AffectsMeasure<GsIcon>(SizeProperty);
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

    /// <summary>Fills the <c>star</c>, for a favourite; <c>play</c> is always filled and no other icon ever is.</summary>
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
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var fill = Icon == "play" || Filled && Icon == "star" ? brush : null;
            foreach (var path in paths)
            {
                context.DrawGeometry(fill, pen, path);
            }
        }
    }
}
