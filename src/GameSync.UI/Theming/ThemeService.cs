using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GameSync.UI.Theming;

/// <summary>
/// Puts a theme's colours into the app's resources, so every screen repaints at once with no restart (LOOK-07). Each
/// token is a brush under its design-system name (<c>bg-200</c>, <c>primary</c>, <c>warn-soft</c>); the brushes are kept
/// and recoloured, so everything bound to them follows. Fluent's own controls get the matching light or dark variant
/// and the primary as their accent. A Glossy page lays its strength's tokens over these in its own scope (LOOK-17).
/// </summary>
public static class ThemeService
{
    public static IReadOnlyDictionary<string, string> Apply(Application app, ThemeChoice choice)
    {
        var tokens = ThemeEngine.Build(choice);
        Put(app.Resources, tokens);
        app.RequestedThemeVariant = choice.Mode == ThemeMode.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        var primary = ParseColor(tokens["primary"]);
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorLight1", "SystemAccentColorLight2" })
        {
            app.Resources[key] = primary;
        }

        foreach (var (fluent, token) in FluentColours)
        {
            app.Resources[fluent] = app.Resources[token];
        }

        // The title bar's minimize and maximize buttons under the pointer: the theme's ink laid thinly over the page, as
        // Windows' own caption buttons do, so the art shows through in Glossy.
        var ink = ParseColor(tokens["ink"]);
        SetBrush(app.Resources, "caption-hover", Color.FromArgb(28, ink.R, ink.G, ink.B));
        SetBrush(app.Resources, "caption-press", Color.FromArgb(16, ink.R, ink.G, ink.B));

        app.Resources["TextControlBorderThemeThickness"] = new Thickness(1.5);
        app.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(2);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(10);
        app.Resources["MenuFlyoutPresenterThemePadding"] = new Thickness(6);
        app.Resources["MenuFlyoutItemThemePadding"] = new Thickness(10, 8, 12, 8);
        return tokens;
    }

    /// <summary>
    /// Fluent's own text fields and menus take the design system's colours (design system → SearchField, Menu): a field
    /// is <c>surface-field</c> inside a <c>line-200</c> border that turns <c>primary</c> with the cursor in it, and a menu
    /// floats on the dialog's surface with rows that go <c>bg-300</c> under the pointer, and so does a small ask from a
    /// button (Swap's question, a named save's Rename). The same brushes, so they recolour with the theme.
    /// </summary>
    private static readonly (string Fluent, string Token)[] FluentColours =
    [
        ("TextControlBackground", "surface-field"),
        ("TextControlBackgroundPointerOver", "surface-field"),
        ("TextControlBackgroundFocused", "surface-field"),
        ("TextControlBorderBrush", "line-200"),
        ("TextControlBorderBrushPointerOver", "line-200"),
        ("TextControlBorderBrushFocused", "primary"),
        ("TextControlForeground", "ink"),
        ("TextControlForegroundPointerOver", "ink"),
        ("TextControlForegroundFocused", "ink"),
        ("TextControlPlaceholderForeground", "ink-faint"),
        ("TextControlPlaceholderForegroundPointerOver", "ink-faint"),
        ("TextControlPlaceholderForegroundFocused", "ink-faint"),
        ("TextControlSelectionHighlightColor", "primary-soft"),
        ("TextControlButtonForeground", "ink-muted"),
        ("TextControlButtonForegroundPointerOver", "ink"),
        ("TextControlButtonForegroundPressed", "ink"),
        ("TextControlButtonBackgroundPointerOver", "bg-400"),
        ("TextControlButtonBackgroundPressed", "bg-400"),
        ("FlyoutPresenterBackground", "surface-dialog"),
        ("FlyoutBorderThemeBrush", "line-100"),
        ("MenuFlyoutPresenterBackground", "surface-dialog"),
        ("MenuFlyoutPresenterBorderBrush", "line-100"),
        ("MenuFlyoutItemBackgroundPointerOver", "bg-300"),
        ("MenuFlyoutItemBackgroundPressed", "bg-400"),
        ("MenuFlyoutItemForeground", "ink"),
        ("MenuFlyoutItemForegroundPointerOver", "ink"),
        ("MenuFlyoutItemForegroundPressed", "ink"),
        ("MenuFlyoutItemKeyboardAcceleratorTextForeground", "ink-faint"),
        ("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "ink-muted"),
        ("MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "ink-muted"),
        ("MenuFlyoutSubItemChevron", "ink-muted"),
        ("MenuFlyoutSubItemChevronPointerOver", "ink"),
    ];

    /// <summary>
    /// Glossy (LOOK-17): lays a strength's tokens over the theme for everything inside <paramref name="scope"/>'s
    /// resources, such as the window's frame and its page; null takes them away, back to Solid.
    /// </summary>
    public static void Scope(IResourceDictionary scope, IReadOnlyDictionary<string, string>? tokens)
    {
        if (tokens is null)
        {
            scope.Clear();
            if (TokenColors.TryGetValue(scope, out var colors))
            {
                scope.MergedDictionaries.Remove(colors);
                TokenColors.Remove(scope);
            }

            return;
        }

        // In place (KAN-124): a brush already here is recoloured, so everything bound to it repaints without looking it up
        // again; clearing first made the whole window look every token up twice, which a change of mode in Glossy felt.
        foreach (var key in scope.Keys.OfType<string>().ToList())
        {
            if (!tokens.ContainsKey(key))
            {
                scope.Remove(key);
            }
        }

        Put(scope, tokens);
    }

    /// <summary><c>#rrggbb</c> or <c>rgba(r, g, b, a)</c>.</summary>
    public static Color ParseColor(string value)
    {
        if (value.StartsWith("rgba(", StringComparison.Ordinal))
        {
            var parts = value[5..^1].Split(',').Select(p => double.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
            return Color.FromArgb((byte)Math.Round(parts[3] * 255), (byte)parts[0], (byte)parts[1], (byte)parts[2]);
        }

        return Color.Parse(value);
    }

    /// <summary>A CSS box shadow such as <c>0 24px 64px rgba(0, 0, 0, 0.6)</c> or <c>inset 0 1px 0 rgba(255, 255, 255, 0.05)</c>; <c>none</c> is none.</summary>
    public static BoxShadows ParseShadow(string value)
    {
        var text = value.Trim();
        if (text == "none")
        {
            return default;
        }

        var inset = text.StartsWith("inset ", StringComparison.Ordinal);
        if (inset)
        {
            text = text[6..];
        }

        var colorAt = text.IndexOf("rgba", StringComparison.Ordinal);
        var lengths = text[..colorAt].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture)).ToArray();
        return new BoxShadows(new BoxShadow
        {
            IsInset = inset,
            OffsetX = lengths[0],
            OffsetY = lengths[1],
            Blur = lengths.Length > 2 ? lengths[2] : 0,
            Spread = lengths.Length > 3 ? lengths[3] : 0,
            Color = ParseColor(text[colorAt..].Trim()),
        });
    }

    private static void Put(IResourceDictionary resources, IReadOnlyDictionary<string, string> tokens)
    {
        // Each token's colour (key-color) goes in a dictionary of its own, swapped in whole: written one by one, every one
        // told the whole window that resources had changed, about 150 times for a change of mode (KAN-124).
        var colors = new ResourceDictionary();
        foreach (var (key, value) in tokens)
        {
            if (key is "shadow-dialog" or "lift-card")
            {
                resources[key] = ParseShadow(value);
                continue;
            }

            var color = ParseColor(value);
            colors[key + "-color"] = color;
            SetBrush(resources, key, color);
        }

        if (TokenColors.TryGetValue(resources, out var before) && resources.MergedDictionaries.IndexOf(before) is var at and >= 0)
        {
            resources.MergedDictionaries[at] = colors;
        }
        else
        {
            resources.MergedDictionaries.Add(colors);
        }

        TokenColors.AddOrUpdate(resources, colors);
    }

    /// <summary>Each dictionary's token colours, as last swapped in.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IResourceDictionary, ResourceDictionary> TokenColors = new();

    /// <summary>Recolours the brush under <paramref name="key"/>, or makes it: everything bound to it follows.</summary>
    private static void SetBrush(IResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }
}
