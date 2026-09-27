using System.Globalization;

namespace GameSync.UI.Theming;

public enum ThemeMode
{
    Dark,
    Light,
}

/// <summary>
/// What a theme is built from (design/system/Theming.md): the mode, pure black (dark mode only), a preset, and the
/// person's own primary or secondary as a swatch id or a hex code. <see cref="Accent"/> is the Windows accent colour,
/// which the Windows accent preset follows.
/// </summary>
public sealed record ThemeChoice(
    string Preset = "arcade",
    ThemeMode Mode = ThemeMode.Dark,
    bool PureBlack = false,
    string? Primary = null,
    string? Secondary = null,
    string? Accent = null);

public sealed record Swatch(string Id, string Name, string Hex);

/// <summary>A preset: its primary and secondary swatches and its surface tint (hue, saturation); the Windows accent preset has none of these.</summary>
public sealed record ThemePreset(string Id, string Name, string? Primary, string? Secondary, (double Hue, double Saturation)? Tint, string Note)
{
    public bool FollowsAccent => Tint is null;
}

/// <summary>
/// The design system's theme engine (<c>GameSync.theme.build()</c> in design/system/components/bundle.js), ported
/// step for step: every colour token comes from the four choices, and each one is moved until it meets its contrast
/// on every ground it sits on (LOOK-09). Status and cover-art colours never change with the theme (LOOK-08).
/// A test checks the result against the design system's own engine.
/// </summary>
public static class ThemeEngine
{
    private const string DefaultAccent = "#0078d4";

    public static IReadOnlyList<Swatch> Swatches { get; } =
    [
        new("cyan", "Cyan", "#7fe6f2"),
        new("aqua", "Aqua", "#7fe3cf"),
        new("sky", "Sky", "#9fd3ff"),
        new("blue", "Blue", "#8fb4ff"),
        new("green", "Green", "#8ee3a0"),
        new("mint", "Mint", "#a6ecc9"),
        new("lime", "Lime", "#cde77a"),
        new("pink", "Pink", "#ff9fcb"),
        new("steel", "Steel", "#aebfd3"),
        new("grey", "Grey", "#a9aeb4"),
        new("white", "White", "#e6e9ed"),
    ];

    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        new("arcade", "Arcade", "cyan", "steel", (214, 12), "The default: cyan on cool greys."),
        new("moss", "Moss", "green", "sky", (150, 10), "Green with a sky-blue second colour."),
        new("tidal", "Tidal", "blue", "aqua", (222, 16), "Blue with aqua, on navy greys."),
        new("sakura", "Sakura", "pink", "mint", (330, 9), "Pink with mint."),
        new("citrus", "Citrus", "lime", "cyan", (80, 7), "Lime with cyan."),
        new("mono", "Mono", "white", "grey", (0, 0), "White and grey, no colour at all."),
        new("windows", "Windows accent", null, null, null, "Takes its primary from your Windows accent colour."),
    ];

    private static readonly Dictionary<string, string> StatusDark = new()
    {
        ["ok"] = "#7fe6f2", ["ok-soft"] = "#0f3035", ["warn"] = "#f2b544", ["warn-soft"] = "#2a2213", ["danger"] = "#ff8a7d",
        ["danger-soft"] = "#2e1917", ["play"] = "#b9a3ff", ["play-soft"] = "#231d38", ["neutral"] = "#9aa1a9",
    };

    private static readonly Dictionary<string, string> StatusLight = new()
    {
        ["ok"] = "#006b77", ["ok-soft"] = "#d8f3f6", ["warn"] = "#855600", ["warn-soft"] = "#fbeed3", ["danger"] = "#b3261e",
        ["danger-soft"] = "#fde4e1", ["play"] = "#6547d1", ["play-soft"] = "#ece7ff", ["neutral"] = "#58616b",
    };

    private static readonly Dictionary<string, string> Fixed = new()
    {
        ["on-art"] = "#f5f7f9", ["art-scrim"] = "rgba(5, 6, 8, 0.72)", ["glass"] = "rgba(16, 18, 21, 0.72)",
    };

    public static ThemePreset Preset(string id) => Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];

    public static Swatch? Swatch(string id) => Swatches.FirstOrDefault(s => s.Id == id);

    /// <summary>Every colour token, keyed by its name in the design system, as <c>#rrggbb</c> or <c>rgba(…)</c>.</summary>
    public static IReadOnlyDictionary<string, string> Build(ThemeChoice choice)
    {
        var preset = Preset(choice.Preset);
        var light = choice.Mode == ThemeMode.Light;
        var black = !light && choice.PureBlack;
        var accent = HexOf(choice.Accent) ?? DefaultAccent;
        var seedPrimary = HexOf(choice.Primary) ?? (preset.FollowsAccent ? accent : HexOf(preset.Primary)!);
        var seedSecondary = HexOf(choice.Secondary)
            ?? (preset.FollowsAccent ? Hsl(HueOf(accent), Math.Min(SaturationOf(accent), 100) * 0.3, 78) : HexOf(preset.Secondary)!);
        var th = preset.Tint?.Hue ?? HueOf(accent);
        var ts = preset.Tint?.Saturation ?? 10;
        var v = new Dictionary<string, string>();

        if (!light)
        {
            v["bg-000"] = black ? Hsl(th, ts, 3.2) : Hsl(th, ts * 1.2, 5.1);
            v["bg-100"] = black ? "#000000" : Hsl(th, ts * 1.1, 7.3);
            v["bg-200"] = black ? Hsl(th, ts, 5.8) : Hsl(th, ts, 9.8);
            v["bg-300"] = black ? Hsl(th, ts * 0.9, 10) : Hsl(th, ts * 0.85, 13.5);
            v["bg-400"] = black ? Hsl(th, ts * 0.85, 14.5) : Hsl(th, ts * 0.8, 18.2);
            v["line-100"] = black ? Hsl(th, ts, 11.5) : Hsl(th, ts, 15.2);
            v["primary"] = Reach(seedPrimary, [v["bg-200"], v["bg-300"]], 7.5, up: true);
            var pp = ToHsl(v["primary"]);
            v["primary-strong"] = Hsl(pp.H, pp.S, pp.L > 84 ? pp.L - 10 : Math.Min(pp.L + 9, 93));
            v["on-primary"] = Reach(Hsl(pp.H, Math.Min(pp.S, 70), 11), [v["primary"], v["primary-strong"]], 7, up: false);
            v["primary-soft"] = Reach(Hsl(pp.H, Math.Min(pp.S, 45), 14), [v["primary"]], 6, up: false);
            v["secondary"] = Reach(seedSecondary, [v["bg-200"], v["bg-300"]], 6.5, up: true);
            var sp = ToHsl(v["secondary"]);
            v["on-secondary"] = Reach(Hsl(sp.H, Math.Min(sp.S, 60), 11), [v["secondary"]], 7, up: false);
            v["secondary-soft"] = Hsl(sp.H, Math.Min(sp.S * 0.6, 34), black ? 13 : 17);
            string[] grounds = [v["bg-000"], v["bg-100"], v["bg-200"], v["bg-300"], v["secondary-soft"], v["primary-soft"]];
            v["ink"] = Hsl(th, ts * 0.8, 94.5);
            v["ink-muted"] = Reach(Hsl(th, ts * 0.7, 67), grounds, 6, up: true);
            v["ink-faint"] = Reach(Hsl(th, ts * 0.6, 54), grounds, 4.6, up: true);
            v["line-200"] = Reach(Hsl(th, ts * 0.6, 40), [v["bg-000"], v["bg-200"], v["bg-300"], v["secondary-soft"]], 3.05, up: true);
            Add(v, StatusDark);
            v["scrim"] = "rgba(5, 6, 8, 0.72)";
            v["shadow-dialog"] = "0 24px 64px rgba(0, 0, 0, 0.6)";
        }
        else
        {
            var tl = Math.Min(ts * 1.6, 28);
            v["bg-000"] = Hsl(th, tl, 92.6);
            v["bg-100"] = Hsl(th, tl, 95.6);
            v["bg-200"] = Hsl(th, tl * 0.6, 99.6);
            v["bg-300"] = Hsl(th, tl, 91.8);
            v["bg-400"] = Hsl(th, tl * 0.9, 86.8);
            v["line-100"] = Hsl(th, tl, 87.5);
            v["primary"] = Reach(LightStart(seedPrimary), [v["bg-300"], v["bg-000"], v["bg-100"]], 4.6, up: false);
            var lp = ToHsl(v["primary"]);
            v["primary-strong"] = Hsl(lp.H, lp.S, Math.Max(lp.L - 7, 6));
            v["on-primary"] = Contrast("#ffffff", v["primary"]) >= 4.5 ? "#ffffff" : Hsl(lp.H, 20, 8);
            v["primary-soft"] = Reach(Hsl(lp.H, Math.Min(lp.S, 80), 93.5), [v["primary"]], 4.5, up: true);
            v["secondary"] = Reach(LightStart(seedSecondary), [v["bg-300"], v["bg-000"], v["bg-100"]], 4.6, up: false);
            var ls = ToHsl(v["secondary"]);
            v["on-secondary"] = Contrast("#ffffff", v["secondary"]) >= 4.5 ? "#ffffff" : Hsl(ls.H, 20, 8);
            v["secondary-soft"] = Hsl(ls.H, Math.Min(ls.S, 55), 89.5);
            string[] grounds = [v["bg-000"], v["bg-100"], v["bg-200"], v["bg-300"], v["secondary-soft"], v["primary-soft"]];
            v["ink"] = Hsl(th, Math.Min(tl, 18), 10);
            v["ink-muted"] = Reach(Hsl(th, tl * 0.8, 34), grounds, 6, up: false);
            v["ink-faint"] = Reach(Hsl(th, tl * 0.6, 44), grounds, 4.6, up: false);
            v["line-200"] = Reach(Hsl(th, tl * 0.5, 62), [v["bg-000"], v["bg-200"], v["bg-300"], v["bg-100"], v["secondary-soft"]], 3.05, up: false);
            Add(v, StatusLight);
            v["scrim"] = "rgba(16, 20, 26, 0.45)";
            v["shadow-dialog"] = "0 24px 64px rgba(16, 24, 40, 0.18)";
        }

        Add(v, Fixed);
        return v;
    }

    /// <summary>WCAG 2 contrast ratio of two <c>#rrggbb</c> colours.</summary>
    public static double Contrast(string a, string b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    public static (int R, int G, int B)? HexToRgb(string hex)
    {
        var index = hex.IndexOf('#');
        var s = (index >= 0 ? hex.Remove(index, 1) : hex).Trim();
        if (s.Length == 3)
        {
            s = string.Concat(s.Select(c => new string(c, 2)));
        }

        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
        {
            return null;
        }

        var n = int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((n >> 16) & 255, (n >> 8) & 255, n & 255);
    }

    private static string? HexOf(string? value) => value is null ? null : Swatch(value)?.Hex ?? value;

    private static void Add(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    private static double Clamp(double x, double min, double max) => Math.Min(max, Math.Max(min, x));

    /// <summary>JavaScript's Math.round: halves go up.</summary>
    private static double JsRound(double x)
    {
        var floor = Math.Floor(x);
        return x - floor >= 0.5 ? floor + 1 : floor;
    }

    private static string RgbToHex(double r, double g, double b) =>
        "#" + string.Concat(new[] { r, g, b }.Select(c => ((int)JsRound(Clamp(c, 0, 255))).ToString("x2", CultureInfo.InvariantCulture)));

    private static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        r /= 255;
        g /= 255;
        b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), h = 0, s = 0, l = (max + min) / 2;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r)
            {
                h = (g - b) / d + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / d + 2;
            }
            else
            {
                h = (r - g) / d + 4;
            }

            h *= 60;
        }

        return (h, s * 100, l * 100);
    }

    /// <summary>A colour from hue (degrees), saturation and lightness (0 to 100), as <c>#rrggbb</c>.</summary>
    public static string Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Clamp(s, 0, 100) / 100;
        l = Clamp(l, 0, 100) / 100;
        var a = s * Math.Min(l, 1 - l);
        double F(double n)
        {
            var k = (n + h / 30) % 12;
            return l - a * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1)));
        }

        return RgbToHex(F(0) * 255, F(8) * 255, F(4) * 255);
    }

    private static (double H, double S, double L) ToHsl(string hex)
    {
        var c = HexToRgb(hex) ?? throw new ArgumentException($"'{hex}' isn't a colour like #7fe6f2.", nameof(hex));
        return RgbToHsl(c.R, c.G, c.B);
    }

    private static double HueOf(string hex) => ToHsl(hex).H;

    private static double SaturationOf(string hex) => ToHsl(hex).S;

    private static double Luminance(string hex)
    {
        var c = HexToRgb(hex) ?? throw new ArgumentException($"'{hex}' isn't a colour like #7fe6f2.", nameof(hex));
        static double Channel(int value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Moves a colour's lightness, keeping hue and saturation, until it reaches <paramref name="min"/> contrast on every ground.</summary>
    private static string Reach(string hex, string[] grounds, double min, bool up)
    {
        var p = ToHsl(hex);
        var l = p.L;
        var c = hex;
        for (var i = 0; i < 220; i++)
        {
            c = Hsl(p.H, p.S, l);
            if (grounds.All(g => Contrast(c, g) >= min))
            {
                return c;
            }

            l += up ? 0.5 : -0.5;
            if (l < 0 || l > 100)
            {
                break;
            }
        }

        return c;
    }

    /// <summary>Light mode starts from a calmer colour: pastel seeds are fully saturated in HSL terms, and near-greys go to a dark slate.</summary>
    private static string LightStart(string hex)
    {
        var p = ToHsl(hex);
        return p.S < 20 && p.L > 84 ? Hsl(p.H, p.S, 24) : Hsl(p.H, Math.Min(p.S, 72), p.L);
    }
}
