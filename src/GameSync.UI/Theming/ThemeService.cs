using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace GameSync.UI.Theming;

/// <summary>
/// Puts a theme's colours into the app's resources, so every screen repaints at once with no restart (LOOK-07). Each
/// token is a brush under its design-system name (<c>bg-200</c>, <c>primary</c>, <c>warn-soft</c>); the brushes are kept
/// and recoloured, so everything bound to them follows. Fluent's own controls get the matching light or dark variant
/// and the primary as their accent.
/// </summary>
public static class ThemeService
{
    public static IReadOnlyDictionary<string, string> Apply(Application app, ThemeChoice choice)
    {
        var tokens = ThemeEngine.Build(choice);
        foreach (var (key, value) in tokens)
        {
            if (key == "shadow-dialog")
            {
                app.Resources[key] = ParseShadow(value);
                continue;
            }

            var color = ParseColor(value);
            app.Resources[key + "-color"] = color;
            if (app.Resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                app.Resources[key] = new SolidColorBrush(color);
            }
        }

        app.RequestedThemeVariant = choice.Mode == ThemeMode.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        var primary = ParseColor(tokens["primary"]);
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorLight1", "SystemAccentColorLight2" })
        {
            app.Resources[key] = primary;
        }

        return tokens;
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

    /// <summary>A CSS box shadow such as <c>0 24px 64px rgba(0, 0, 0, 0.6)</c>.</summary>
    public static BoxShadows ParseShadow(string value)
    {
        var colorAt = value.IndexOf("rgba", StringComparison.Ordinal);
        var lengths = value[..colorAt].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture)).ToArray();
        return new BoxShadows(new BoxShadow { OffsetX = lengths[0], OffsetY = lengths[1], Blur = lengths[2], Color = ParseColor(value[colorAt..].Trim()) });
    }
}
