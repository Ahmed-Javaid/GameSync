using Avalonia.Controls;

namespace GameSync.UI.Theming;

/// <summary>
/// A11Y-04: Windows' text size (Settings → Accessibility → Text size), as GameSync's type scale. Every text size and line
/// height on screen is a resource here (<c>fs-13</c>, <c>lh-19</c>: the design system's size times the scale), so a change
/// in Windows' setting resizes the text at once, and only the text: icons, art and spacing stay as drawn.
/// </summary>
public static class TextScale
{
    /// <summary>The design system's text sizes, as the screens use them.</summary>
    public static readonly IReadOnlyList<int> Sizes = [10, 11, 12, 13, 14, 15, 16, 18, 20, 24, 28, 32, 56];

    /// <summary>The line heights that go with them.</summary>
    public static readonly IReadOnlyList<int> LineHeights = [14, 16, 18, 19, 20, 21, 22, 24, 26, 30, 32, 38, 62];

    /// <summary>The dialogs' widths in the design, and Properties' section list.</summary>
    public static readonly IReadOnlyList<int> DialogWidths = [208, 620, 640, 720, 880];

    /// <summary>Table columns as wide as their text, in the design's widths.</summary>
    public static readonly IReadOnlyList<int> TextColumns = [64, 96, 104, 148];

    /// <summary>The scale applied now: 1 at Windows' default, up to 2.25.</summary>
    public static double Current { get; private set; } = 1;

    /// <summary>Puts the sizes for <paramref name="scale"/> into the app's resources; true when they changed.</summary>
    public static bool Apply(IResourceDictionary resources, double scale)
    {
        scale = Math.Clamp(scale, 1, 2.25);
        if (resources.ContainsKey("fs-13") && Math.Abs(scale - Current) < 0.001)
        {
            return false;
        }

        Current = scale;
        foreach (var size in Sizes)
        {
            resources[$"fs-{size}"] = size * scale;
        }

        foreach (var height in LineHeights)
        {
            resources[$"lh-{height}"] = height * scale;
        }

        // Fluent's own controls (text fields, menus, tooltips) and text with no size of its own.
        resources["ControlContentThemeFontSize"] = 14 * scale;

        // The side columns of words (the library's list, Settings' sections) widen with the text, by less than it grows,
        // so the page beside them keeps its room: 30% wider at 150%.
        var widen = 1 + ((scale - 1) * 0.6);
        resources["col-library-list"] = new GridLength(Math.Round(272 * widen));
        resources["col-settings-nav"] = new GridLength(Math.Round(248 * widen));
        resources["col-setup-nav"] = new GridLength(Math.Round(280 * widen));

        // Dialogs widen like the side columns, up to 30% so the widest (Properties, 880) still fits a 1280 window.
        foreach (var dialog in DialogWidths)
        {
            resources[$"dw-{dialog}"] = Math.Round(dialog * Math.Min(widen, 1.3));
        }

        // Table columns sized for their text (the Versions tab's when, PC, files and size) grow with it.
        foreach (var column in TextColumns)
        {
            resources[$"cw-{column}"] = new GridLength(Math.Round(column * scale));
        }
        return true;
    }
}
