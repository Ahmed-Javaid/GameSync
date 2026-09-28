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

        return tokens;
    }

    /// <summary>
    /// Glossy (LOOK-17): lays a strength's tokens over the theme for everything inside <paramref name="scope"/>'s
    /// resources, such as the window's frame and its page; null takes them away, back to Solid.
    /// </summary>
    public static void Scope(IResourceDictionary scope, IReadOnlyDictionary<string, string>? tokens)
    {
        scope.Clear();
        if (tokens is not null)
        {
            Put(scope, tokens);
        }
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
        foreach (var (key, value) in tokens)
        {
            if (key is "shadow-dialog" or "lift-card")
            {
                resources[key] = ParseShadow(value);
                continue;
            }

            var color = ParseColor(value);
            resources[key + "-color"] = color;
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
}
