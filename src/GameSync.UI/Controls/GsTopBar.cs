using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GameSync.UI.Controls;

/// <summary>
/// A page's top bar: its title on the left, its tabs in the middle and its buttons on the right, in one row while they
/// fit. With big text in a small window (A11Y-04) the tabs go under the title instead of pushing the buttons off the page.
/// Its children, in order: the title, the tabs, the buttons.
/// </summary>
public sealed class GsTopBar : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<GsTopBar, double>(nameof(Spacing), 16);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The tabs are on a row of their own now.</summary>
    public bool Stacked { get; private set; }

    static GsTopBar() => AffectsMeasure<GsTopBar>(SpacingProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
        {
            child.Measure(unbounded);
        }

        if (Children.Count < 3)
        {
            return new Size(Children.Sum(c => c.DesiredSize.Width), Children.Count == 0 ? 0 : Children.Max(c => c.DesiredSize.Height));
        }

        var (title, tabs, buttons) = (Children[0].DesiredSize, Children[1].DesiredSize, Children[2].DesiredSize);
        var oneRow = title.Width + tabs.Width + buttons.Width + (2 * Spacing);
        Stacked = !double.IsInfinity(availableSize.Width) && oneRow > availableSize.Width;
        return Stacked
            ? new Size(availableSize.Width, Math.Max(title.Height, buttons.Height) + Spacing * 0.75 + tabs.Height)
            : new Size(double.IsInfinity(availableSize.Width) ? oneRow : availableSize.Width, Math.Max(title.Height, Math.Max(tabs.Height, buttons.Height)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 3)
        {
            var x = 0.0;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width + Spacing;
            }

            return finalSize;
        }

        var (title, tabs, buttons) = (Children[0], Children[1], Children[2]);
        var width = finalSize.Width;
        if (Stacked)
        {
            var top = Math.Max(title.DesiredSize.Height, buttons.DesiredSize.Height);
            var buttonsWidth = Math.Min(buttons.DesiredSize.Width, width);
            title.Arrange(new Rect(0, 0, Math.Max(0, width - buttonsWidth - Spacing), top));
            buttons.Arrange(new Rect(width - buttonsWidth, 0, buttonsWidth, top));
            tabs.Arrange(new Rect(0, top + (Spacing * 0.75), Math.Min(tabs.DesiredSize.Width, width), tabs.DesiredSize.Height));
            return finalSize;
        }

        // One row: the tabs in the middle of the bar, or as near it as the title and buttons leave room for.
        var height = finalSize.Height;
        var tabsWidth = tabs.DesiredSize.Width;
        var left = title.DesiredSize.Width + Spacing;
        var right = width - buttons.DesiredSize.Width - Spacing - tabsWidth;
        var tabsX = Math.Clamp((width - tabsWidth) / 2, left, Math.Max(left, right));
        title.Arrange(new Rect(0, 0, Math.Max(0, tabsX - Spacing), height));
        tabs.Arrange(new Rect(tabsX, (height - tabs.DesiredSize.Height) / 2, tabsWidth, tabs.DesiredSize.Height));
        buttons.Arrange(new Rect(width - buttons.DesiredSize.Width, 0, buttons.DesiredSize.Width, height));
        return finalSize;
    }
}
