using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GameSync.UI.Theming;

/// <summary>
/// Glossy's backdrop (LOOK-17, LOOK-18): a game's art made once into a small blurred picture (the design's 64px blur over
/// a full window, saturated a little), then darkened from top to bottom by its strength's scrim, and further on any row
/// where the art is bright, until every text colour keeps 4.5:1, and controls 3:1, on every surface laid over it. In
/// light mode (design system version 35) the scrim is light: it lightens the art, further on any row where the art is
/// dark, until the dark text keeps its contrast. The pixel work needs no display, so the contrast is tested over
/// worst-case pictures.
/// </summary>
public static class Backdrop
{
    public const int Width = 160;
    public const int Height = 100;

    /// <summary>Three box passes of this half-width make a Gaussian of 8px here: the design's 64px on a 1280px window.</summary>
    private const int BlurRadius = 8;
    private const double Saturation = 1.3;

    /// <summary>The design's 140px past each edge of a 1280 × 800 window, in these pixels.</summary>
    private const int Overscan = 18;

    /// <summary>A row's extra darkening is found to within 1/4,096 of what's left.</summary>
    private const int SearchSteps = 12;

    /// <summary>
    /// How colourful the art may stay, as OKLab chroma before the scrim: below <see cref="ChromaKnee"/> it's as the design
    /// saturates it; above, it eases towards <see cref="ChromaLimit"/>, so one vivid colour filling the art (Counter-Strike
    /// 2's orange) doesn't flood the app while muted art keeps its colour (the owner, 1 Oct 2026).
    /// </summary>
    public const double ChromaKnee = 0.1;

    public const double ChromaLimit = 0.15;

    /// <summary>The finished picture for a game's art, or null when the file can't be read, and the page stays Solid.</summary>
    public static Bitmap? Make(string artPath, GlassSurface glass, IReadOnlyDictionary<string, string> theme) =>
        Read(artPath) is { } art ? Finish(Tame(Soften(art.Pixels, art.Width, art.Height)), glass, theme) : null;

    /// <summary>Softened art under its strength's scrim, laid on thicker wherever text would lose contrast, as a bitmap.</summary>
    public static Bitmap Finish(double[] rgb, GlassSurface glass, IReadOnlyDictionary<string, string> theme) =>
        ToBitmap(Bake(rgb, glass, ScrimAlphas(rgb, glass, theme)));

    /// <summary>A picture's pixels as BGRA, decoded as the backdrop reads them, or null when it can't be read.</summary>
    public static (byte[] Pixels, int Width, int Height)? Pixels(string path) => Read(path);

    /// <summary>
    /// Eases each pixel's colourfulness (OKLab chroma) above <see cref="ChromaKnee"/> towards <see cref="ChromaLimit"/>,
    /// keeping its lightness and hue: RGB from 0 to 255 in, changed in place, and returned.
    /// </summary>
    public static double[] Tame(double[] rgb, double knee = ChromaKnee, double limit = ChromaLimit)
    {
        var room = limit - knee;
        for (var i = 0; i < rgb.Length; i += 3)
        {
            var (l, a, b) = OkLab.From(rgb[i], rgb[i + 1], rgb[i + 2]);
            var chroma = Math.Sqrt(a * a + b * b);
            if (chroma <= knee)
            {
                continue;
            }

            var eased = knee + room * Math.Tanh((chroma - knee) / room);
            (rgb[i], rgb[i + 1], rgb[i + 2]) = OkLab.To(l, a * eased / chroma, b * eased / chroma);
        }

        return rgb;
    }

    /// <summary>The average and the highest OKLab chroma of softened pixels, for measuring art.</summary>
    public static (double Average, double Highest) Chroma(double[] rgb)
    {
        double sum = 0, highest = 0;
        for (var i = 0; i < rgb.Length; i += 3)
        {
            var (_, a, b) = OkLab.From(rgb[i], rgb[i + 1], rgb[i + 2]);
            var chroma = Math.Sqrt(a * a + b * b);
            (sum, highest) = (sum + chroma, Math.Max(highest, chroma));
        }

        return (sum / (rgb.Length / 3), highest);
    }

    /// <summary>
    /// BGRA pixels as the window's backdrop, blurred and saturated, as RGB from 0 to 255. As in the design, the art fills a
    /// box reaching <see cref="Overscan"/> past the window on every side (140px on 1280 × 800), so the blur near the
    /// window's edges takes in the art beyond them, and the window shows the middle.
    /// </summary>
    public static double[] Soften(byte[] bgra, int width, int height)
    {
        const int boxWidth = Width + 2 * Overscan, boxHeight = Height + 2 * Overscan;
        var box = new double[boxWidth * boxHeight * 3];
        var scale = Math.Max((double)boxWidth / width, (double)boxHeight / height);
        double offsetX = (width - boxWidth / scale) / 2, offsetY = (height - boxHeight / scale) / 2;
        for (var ty = 0; ty < boxHeight; ty++)
        {
            var y0 = Math.Clamp((int)Math.Floor(offsetY + ty / scale), 0, height - 1);
            var y1 = Math.Clamp((int)Math.Floor(offsetY + (ty + 1) / scale), y0 + 1, height);
            for (var tx = 0; tx < boxWidth; tx++)
            {
                var x0 = Math.Clamp((int)Math.Floor(offsetX + tx / scale), 0, width - 1);
                var x1 = Math.Clamp((int)Math.Floor(offsetX + (tx + 1) / scale), x0 + 1, width);
                double r = 0, g = 0, b = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = (y * width + x) * 4;
                        (b, g, r) = (b + bgra[i], g + bgra[i + 1], r + bgra[i + 2]);
                    }
                }

                var n = (double)(y1 - y0) * (x1 - x0);
                var o = (ty * boxWidth + tx) * 3;
                (box[o], box[o + 1], box[o + 2]) = (r / n, g / n, b / n);
            }
        }

        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlur(box, boxWidth, boxHeight, horizontal: true);
            BoxBlur(box, boxWidth, boxHeight, horizontal: false);
        }

        // The window's part, saturated as CSS saturate(1.3) does.
        const double s = Saturation;
        var rgb = new double[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = ((y + Overscan) * boxWidth + x + Overscan) * 3;
                var (r, g, b) = (box[i], box[i + 1], box[i + 2]);
                var o = (y * Width + x) * 3;
                rgb[o] = Math.Clamp((0.213 + 0.787 * s) * r + (0.715 - 0.715 * s) * g + (0.072 - 0.072 * s) * b, 0, 255);
                rgb[o + 1] = Math.Clamp((0.213 - 0.213 * s) * r + (0.715 + 0.285 * s) * g + (0.072 - 0.072 * s) * b, 0, 255);
                rgb[o + 2] = Math.Clamp((0.213 - 0.213 * s) * r + (0.715 - 0.715 * s) * g + (0.072 + 0.928 * s) * b, 0, 255);
            }
        }

        return rgb;
    }

    /// <summary>
    /// The scrim's alpha for each row: the strength's stops at least, and on a row where the art is bright (dark, in light
    /// mode), just enough more that every text colour and control on every surface keeps its contrast over every pixel of
    /// that row.
    /// </summary>
    public static double[] ScrimAlphas(double[] rgb, GlassSurface glass, IReadOnlyDictionary<string, string> theme)
    {
        var scrim = Rgb.Parse(glass.Scrim);
        var checks = Checks(glass, theme);
        var floor = new double[Height];
        var extra = new double[Height];
        for (var y = 0; y < Height; y++)
        {
            floor[y] = Floor(glass.Stops, (y + 0.5) / Height);
            var firstToFail = FirstToFail(rgb, y, glass.Lightens);
            if (RowPasses(rgb, firstToFail, scrim, floor[y], checks))
            {
                continue;
            }

            double lo = floor[y], hi = 1;
            for (var step = 0; step < SearchSteps; step++)
            {
                var mid = (lo + hi) / 2;
                (lo, hi) = RowPasses(rgb, firstToFail, scrim, mid, checks) ? (lo, mid) : (mid, hi);
            }

            extra[y] = hi - floor[y];
        }

        // Only what bright art adds (dark art, in light mode) is smoothed, and never below any row's need: the widest of its
        // neighbours, then an average over fewer. Dark art keeps the design's gradient exactly (bright art, in light mode).
        var widest = new double[Height];
        for (var y = 0; y < Height; y++)
        {
            widest[y] = extra[Math.Max(0, y - 4)..Math.Min(Height, y + 5)].Max();
        }

        var alphas = new double[Height];
        for (var y = 0; y < Height; y++)
        {
            alphas[y] = Math.Min(1, floor[y] + widest[Math.Max(0, y - 3)..Math.Min(Height, y + 4)].Average());

            // Checked again as it will be baked, and covered a little more until it reads: a row the search found just at
            // its threshold can fall a hair short once the sums round to whole channels, and one the smoothing raised can
            // land in a dip, as a colour with channels either side of the scrim's (cyan over light mode's grey) first
            // gets darker, then lighter, as it's covered.
            var firstToFail = FirstToFail(rgb, y, glass.Lightens);
            while (alphas[y] < 1 && !RowPasses(rgb, firstToFail, scrim, alphas[y], checks))
            {
                alphas[y] = Math.Min(1, alphas[y] + 1.0 / 1024);
            }
        }

        return alphas;
    }

    /// <summary>The finished pixels: the softened art under the scrim at each row's alpha, as opaque BGRA.</summary>
    public static byte[] Bake(double[] rgb, GlassSurface glass, double[] alphas)
    {
        var scrim = Rgb.Parse(glass.Scrim);
        var bgra = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            var a = alphas[y];
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 3;
                var o = (y * Width + x) * 4;
                bgra[o] = (byte)Math.Round(a * scrim.B + (1 - a) * rgb[i + 2]);
                bgra[o + 1] = (byte)Math.Round(a * scrim.G + (1 - a) * rgb[i + 1]);
                bgra[o + 2] = (byte)Math.Round(a * scrim.R + (1 - a) * rgb[i]);
                bgra[o + 3] = 255;
            }
        }

        return bgra;
    }

    /// <summary>LOOK-18's check on finished pixels: the lowest contrast found divided by what it needs, 1 or more when everything reads.</summary>
    public static double WorstMargin(byte[] bakedBgra, GlassSurface glass, IReadOnlyDictionary<string, string> theme)
    {
        var worst = double.MaxValue;
        var checks = Checks(glass, theme);
        for (var o = 0; o < bakedBgra.Length; o += 4)
        {
            var ground = new Rgb(bakedBgra[o + 2], bakedBgra[o + 1], bakedBgra[o]);
            foreach (var check in checks)
            {
                worst = Math.Min(worst, check.Contrast(ground) / check.Min);
            }
        }

        return worst;
    }

    /// <summary>
    /// What must read over the backdrop: captions on the page, cards and consoles, the rail's icons, text on controls and
    /// on their hover, the selection, each status on its soft ground where Glossy makes it see-through, and control borders.
    /// </summary>
    private static IReadOnlyList<Check> Checks(GlassSurface glass, IReadOnlyDictionary<string, string> theme)
    {
        Rgb Ink(string token) => Rgb.Parse(theme[token]);
        Layer? Surface(string token) => glass.Tokens.TryGetValue(token, out var value) ? Layer.Parse(value) : null;
        var checks = new List<Check>
        {
            new(Ink("ink-faint"), null, 4.5),
            new(Ink("ink-faint"), Surface("bg-000"), 4.5),
            new(Ink("ink-faint"), Surface("bg-100"), 3),
            new(Ink("ink-faint"), Surface("bg-200"), 4.5),
            new(Ink("ink-faint"), Surface("surface-dialog"), 4.5),
            new(Ink("ink-muted"), Surface("bg-300"), 4.5),
            new(Ink("ink"), Surface("bg-400"), 4.5),
            new(Ink("ink"), Surface("secondary-soft"), 4.5),
            new(Ink("secondary"), Surface("secondary-soft"), 3),
            new(Ink("line-200"), null, 3),
            new(Ink("line-200"), Surface("bg-200"), 3),
        };
        foreach (var status in new[] { "ok", "warn", "play", "danger" })
        {
            if (Surface(status + "-soft") is { } soft)
            {
                checks.Add(new Check(Ink(status), soft, 4.5));
            }
        }

        return checks;
    }

    /// <summary>
    /// The pixels of row <paramref name="y"/> that can fail first, as offsets into <paramref name="rgb"/>. In dark mode every
    /// text colour is lighter than anything the scrim leaves, so it only loses contrast as what's under it gets brighter: a
    /// pixel no brighter than another in any channel stays readable whenever that one is, and isn't checked. In light mode
    /// (<paramref name="lightens"/>) the text is darker than the scrim, so it's the darkest pixels that fail first.
    /// </summary>
    private static List<int> FirstToFail(double[] rgb, int y, bool lightens)
    {
        var kept = new List<int>();
        var pixels = Enumerable.Range(y * Width, Width).Select(p => p * 3);
        foreach (var i in lightens ? pixels.OrderBy(i => rgb[i] + rgb[i + 1] + rgb[i + 2]) : pixels.OrderByDescending(i => rgb[i] + rgb[i + 1] + rgb[i + 2]))
        {
            if (!kept.Exists(k => lightens
                    ? rgb[k] <= rgb[i] && rgb[k + 1] <= rgb[i + 1] && rgb[k + 2] <= rgb[i + 2]
                    : rgb[k] >= rgb[i] && rgb[k + 1] >= rgb[i + 1] && rgb[k + 2] >= rgb[i + 2]))
            {
                kept.Add(i);
            }
        }

        return kept;
    }

    private static bool RowPasses(double[] rgb, List<int> pixels, Rgb scrim, double alpha, IReadOnlyList<Check> checks)
    {
        foreach (var i in pixels)
        {
            var ground = new Rgb(
                Math.Round(alpha * scrim.R + (1 - alpha) * rgb[i]),
                Math.Round(alpha * scrim.G + (1 - alpha) * rgb[i + 1]),
                Math.Round(alpha * scrim.B + (1 - alpha) * rgb[i + 2]));
            foreach (var check in checks)
            {
                if (check.Contrast(ground) < check.Min)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The scrim's alpha at <paramref name="at"/> (0 top, 1 bottom), between the stops.</summary>
    private static double Floor(IReadOnlyList<(double At, double Alpha)> stops, double at)
    {
        if (at <= stops[0].At)
        {
            return stops[0].Alpha;
        }

        for (var i = 1; i < stops.Count; i++)
        {
            if (at <= stops[i].At)
            {
                var (a, b) = (stops[i - 1], stops[i]);
                return a.Alpha + (b.Alpha - a.Alpha) * (at - a.At) / (b.At - a.At);
            }
        }

        return stops[^1].Alpha;
    }

    private static void BoxBlur(double[] rgb, int width, int height, bool horizontal)
    {
        var (lines, length) = horizontal ? (height, width) : (width, height);
        var line = new double[length * 3];
        for (var l = 0; l < lines; l++)
        {
            int At(int p) => horizontal ? (l * width + p) * 3 : (p * width + l) * 3;
            for (var p = 0; p < length; p++)
            {
                Array.Copy(rgb, At(p), line, p * 3, 3);
            }

            for (var p = 0; p < length; p++)
            {
                double r = 0, g = 0, b = 0;
                for (var k = -BlurRadius; k <= BlurRadius; k++)
                {
                    var q = Math.Clamp(p + k, 0, length - 1) * 3;
                    (r, g, b) = (r + line[q], g + line[q + 1], b + line[q + 2]);
                }

                var o = At(p);
                const double n = 2 * BlurRadius + 1;
                (rgb[o], rgb[o + 1], rgb[o + 2]) = (r / n, g / n, b / n);
            }
        }
    }

    private static (byte[] Pixels, int Width, int Height)? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var bitmap = WriteableBitmap.DecodeToWidth(stream, Width * 3, BitmapInterpolationMode.MediumQuality);
            using var frame = bitmap.Lock();
            var (width, height) = (frame.Size.Width, frame.Size.Height);
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(frame.Address + y * frame.RowBytes, pixels, y * width * 4, width * 4);
            }

            if (frame.Format == PixelFormat.Rgba8888)
            {
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                }
            }
            else if (frame.Format != PixelFormat.Bgra8888)
            {
                return null;
            }

            return (pixels, width, height);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static Bitmap ToBitmap(byte[] bgra)
    {
        var pinned = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pinned.AddrOfPinnedObject(), new PixelSize(Width, Height), new Vector(96, 96), Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Björn Ottosson's OKLab, from and to sRGB from 0 to 255, for changing colourfulness without shifting lightness or hue.</summary>
    private static class OkLab
    {
        public static (double L, double A, double B) From(double r, double g, double b)
        {
            var (lr, lg, lb) = (ToLinear(r / 255), ToLinear(g / 255), ToLinear(b / 255));
            var l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
            var m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
            var s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
            return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        public static (double R, double G, double B) To(double lightness, double a, double b)
        {
            var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
            return (255 * FromLinear(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                255 * FromLinear(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                255 * FromLinear(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
        }

        private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        private static double FromLinear(double c)
        {
            c = Math.Clamp(c, 0, 1);
            return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        }
    }

    /// <summary>A text colour that must keep <see cref="Min"/> on a surface (or the bare backdrop) over a backdrop pixel.</summary>
    private sealed record Check(Rgb Text, Layer? Surface, double Min)
    {
        private readonly double _text = Text.Luminance;

        public double Contrast(Rgb ground)
        {
            var under = (Surface is { } s ? s.Over(ground) : ground).Luminance;
            return (Math.Max(_text, under) + 0.05) / (Math.Min(_text, under) + 0.05);
        }
    }

    /// <summary>A surface: a colour at an opacity, laid over the backdrop the way the screen blends it.</summary>
    private sealed record Layer(Rgb Color, double Alpha)
    {
        public static Layer Parse(string value) => value.StartsWith("rgba(", StringComparison.Ordinal)
            ? new Layer(Rgb.Parse(value), double.Parse(value[5..^1].Split(',')[3].Trim(), CultureInfo.InvariantCulture))
            : new Layer(Rgb.Parse(value), 1);

        public Rgb Over(Rgb ground) => new(
            Math.Round(Alpha * Color.R + (1 - Alpha) * ground.R),
            Math.Round(Alpha * Color.G + (1 - Alpha) * ground.G),
            Math.Round(Alpha * Color.B + (1 - Alpha) * ground.B));
    }

    private readonly record struct Rgb(double R, double G, double B)
    {
        /// <summary><c>#rrggbb</c>, or the colour of <c>rgba(r, g, b, a)</c>.</summary>
        public static Rgb Parse(string value)
        {
            if (value.StartsWith("rgba(", StringComparison.Ordinal))
            {
                var parts = value[5..^1].Split(',').Select(p => double.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return new Rgb(parts[0], parts[1], parts[2]);
            }

            var c = ThemeEngine.HexToRgb(value) ?? throw new ArgumentException($"'{value}' isn't a colour.", nameof(value));
            return new Rgb(c.R, c.G, c.B);
        }

        /// <summary>Every channel here is a whole number from 0 to 255, so its linear value comes from a table.</summary>
        private static readonly double[] Linear = Enumerable.Range(0, 256)
            .Select(i => i / 255.0 <= 0.03928 ? i / 255.0 / 12.92 : Math.Pow((i / 255.0 + 0.055) / 1.055, 2.4)).ToArray();

        public double Luminance => 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);

        private static double Channel(double value) => Linear[Math.Clamp((int)Math.Round(value), 0, 255)];
    }
}
