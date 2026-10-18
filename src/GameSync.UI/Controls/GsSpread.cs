using Avalonia;
using Avalonia.Controls;

namespace GameSync.UI.Controls;

/// <summary>
/// A page's blocks one under another, spread evenly when there's room (the owner, 18 Oct 2026, of first run's Your games,
/// its cards at the top and the lower half of the window empty: "use equal block spacing if there is alot of empty
/// space"). When the blocks are shorter than the room they're given, what's left is shared equally above the first, between
/// them and below the last, so with the page's own margins above and below (the same as <see cref="Spacing"/>) every gap is
/// the same. Each gap grows by <see cref="MostExtra"/> at most, and what's left past that is split above and below, so a
/// tall window doesn't pull the blocks far apart. Taller than the room, they stack <see cref="Spacing"/> apart. Inside a
/// ScrollViewer, which gives its content no height to fill, bind MinHeight to the viewport's height.
/// </summary>
public sealed class GsSpread : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<GsSpread, double>(nameof(Spacing), 16);

    public static readonly StyledProperty<double> MostExtraProperty = AvaloniaProperty.Register<GsSpread, double>(nameof(MostExtra), 160);

    static GsSpread() => AffectsMeasure<GsSpread>(SpacingProperty, MostExtraProperty);

    /// <summary>The gap between blocks when there's no room to spare.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The most any gap grows by.</summary>
    public double MostExtra
    {
        get => GetValue(MostExtraProperty);
        set => SetValue(MostExtraProperty, value);
    }

    /// <summary>
    /// Where the first of <paramref name="count"/> blocks goes and the gap between them, when they come to
    /// <paramref name="content"/> in height and are given <paramref name="room"/>: each gap, and the space above the first
    /// and below the last, take an equal share of the room left, <paramref name="mostExtra"/> each at most; past that the
    /// rest is split above the first and below the last.
    /// </summary>
    public static (double Top, double Gap) Gaps(double room, double content, int count, double spacing, double mostExtra)
    {
        var free = room - content - (Math.Max(1, count) - 1) * spacing;
        if (count == 0 || double.IsInfinity(room) || double.IsNaN(room) || free <= 0)
        {
            return (0, spacing);
        }

        var extra = Math.Min(free / (count + 1), mostExtra);
        return (extra + (free - extra * (count + 1)) / 2, spacing + extra);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (width, height, shown) = (0.0, 0.0, 0);
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (child.IsVisible)
            {
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
                shown++;
            }
        }

        return new Size(width, height + Math.Max(0, shown - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(c => c.IsVisible).ToList();
        var (y, gap) = Gaps(finalSize.Height, shown.Sum(c => c.DesiredSize.Height), shown.Count, Spacing, MostExtra);
        foreach (var child in shown)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height + gap;
        }

        return finalSize;
    }
}
