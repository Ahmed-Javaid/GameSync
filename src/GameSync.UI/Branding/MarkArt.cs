using System.Buffers.Binary;
using System.IO.Compression;
using GameSync.UI.Theming;

namespace GameSync.UI.Branding;

/// <summary>What the tray icon's badge says (BG-07); none when every game is synced.</summary>
public enum TrayBadge
{
    None,
    Working,
    NeedsYou,
    Offline,
    Playing,
}

/// <summary>
/// GameSync's mark (design/system/assets/Logos: two overlapping rounded squares, one per PC, a 2px stroke on a 24px
/// grid) drawn to pixels with no display: the tray icon, and the app's icon on its dark tile. The tray icon follows the
/// owner's choice of 28 Sep: the mark in the taskbar's own ink, white or black like Windows' icons, with a badge in the
/// fixed status colours carrying anything that isn't "synced", its symbol cut out so the taskbar shows through. Shapes
/// are anti-aliased by their distance to each pixel, so every size is sharp. Pixels are BGRA, straight alpha, top row first.
/// </summary>
public static class MarkArt
{
    /// <summary>The app icon's sizes, as Windows asks for them from 100% to 400% scale.</summary>
    public static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    private static readonly Rgb WhiteInk = Rgb.Parse("#ffffff");
    private static readonly Rgb BlackInk = Rgb.Parse("#1a1a1a");

    /// <summary>The tray icon for one state, for a light or a dark taskbar.</summary>
    public static byte[] Tray(int size, TrayBadge badge, bool lightTaskbar)
    {
        var status = lightTaskbar ? ThemeEngine.Build(new ThemeChoice(Mode: ThemeMode.Light)) : ThemeEngine.Build(new ThemeChoice());
        var ink = badge == TrayBadge.Offline ? Rgb.Parse(status["neutral"]) : lightTaskbar ? BlackInk : WhiteInk;
        var canvas = new Canvas(size);
        canvas.Fill(Mark, ink);
        if (badge != TrayBadge.None)
        {
            var color = Rgb.Parse(status[badge switch
            {
                TrayBadge.Working => "ok",
                TrayBadge.NeedsYou => "warn",
                TrayBadge.Playing => "play",
                _ => "neutral",
            }]);
            canvas.Erase((x, y) => Circle(x, y, 18, 18, 7.0));
            canvas.Fill((x, y) => Circle(x, y, 18, 18, 5.4), color);
            canvas.Erase(badge switch
            {
                TrayBadge.Working => (x, y) => Math.Min(Segment(x, y, 18, 21, 18, 15.6, 0.75), Math.Min(Segment(x, y, 15.9, 17.6, 18, 15.5, 0.75), Segment(x, y, 18, 15.5, 20.1, 17.6, 0.75))),
                TrayBadge.NeedsYou => (x, y) => Math.Min(Segment(x, y, 18, 15.2, 18, 18.6, 0.8), Circle(x, y, 18, 20.7, 0.95)),
                TrayBadge.Playing => (x, y) => Triangle(x, y, (16.6, 15.5), (16.6, 20.5), (20.6, 18)),
                _ => (x, y) => Segment(x, y, 15.4, 18, 20.6, 18, 0.8),
            });
        }

        return canvas.ToBgra();
    }

    /// <summary>The app's icon: the mark in Arcade's cyan on a dark rounded tile, so it reads on light and dark grounds alike.</summary>
    public static byte[] AppIcon(int size)
    {
        var dark = ThemeEngine.Build(new ThemeChoice());
        var canvas = new Canvas(size);
        canvas.Fill((x, y) => RoundedBox(x, y, 12, 12, 11.5, 11.5, 5.5), Rgb.Parse(dark["bg-100"]));

        // Small icons keep the mark bigger, so its strokes stay about a pixel wide.
        var k = size <= 20 ? 0.78 : 0.7;
        canvas.Fill((x, y) => k * Mark(12 + (x - 12) / k, 12 + (y - 12) / k), Rgb.Parse("#7fe6f2"));
        return canvas.ToBgra();
    }

    /// <summary>An .ico file holding each size: 32-bit bitmaps, and PNG for 256 pixels, as Windows' own icons are made.</summary>
    public static byte[] Ico(IReadOnlyList<int> sizes, Func<int, byte[]> draw)
    {
        var images = sizes.Select(s => (Size: s, Data: s >= 256 ? Png(s, draw(s)) : Dib(s, draw(s)))).ToList();
        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            w.Write(data);
        }

        w.Flush();
        return file.ToArray();
    }

    /// <summary>A PNG file of square BGRA pixels.</summary>
    public static byte[] Png(int size, byte[] bgra) => Png(size, size, bgra);

    /// <summary>A PNG file of BGRA pixels.</summary>
    public static byte[] Png(int width, int height, byte[] bgra)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            for (var x = 0; x < width; x++)
            {
                var from = (y * width + x) * 4;
                var to = row + 1 + x * 4;
                raw[to] = bgra[from + 2];
                raw[to + 1] = bgra[from + 1];
                raw[to + 2] = bgra[from];
                raw[to + 3] = bgra[from + 3];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The mark: two rounded squares, (3,3) and (9,9), 12 wide with 2 round corners, stroked 2 wide.</summary>
    private static double Mark(double x, double y) =>
        Math.Min(Math.Abs(RoundedBox(x, y, 9, 9, 6, 6, 2)) - 1, Math.Abs(RoundedBox(x, y, 15, 15, 6, 6, 2)) - 1);

    private static double RoundedBox(double x, double y, double cx, double cy, double halfWidth, double halfHeight, double radius)
    {
        var qx = Math.Abs(x - cx) - halfWidth + radius;
        var qy = Math.Abs(y - cy) - halfHeight + radius;
        return Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    private static double Circle(double x, double y, double cx, double cy, double radius) => Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius;

    /// <summary>A line with round ends, <paramref name="radius"/> being half its width.</summary>
    private static double Segment(double x, double y, double ax, double ay, double bx, double by, double radius)
    {
        var (px, py, dx, dy) = (x - ax, y - ay, bx - ax, by - ay);
        var t = Math.Clamp((px * dx + py * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(px - dx * t, 2) + Math.Pow(py - dy * t, 2)) - radius;
    }

    /// <summary>A filled triangle, by the farthest of its three edges (exact enough at icon sizes).</summary>
    private static double Triangle(double x, double y, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double Edge((double X, double Y) from, (double X, double Y) to)
        {
            var (ex, ey) = (to.X - from.X, to.Y - from.Y);
            var length = Math.Sqrt(ex * ex + ey * ey);
            return ((x - from.X) * ey - (y - from.Y) * ex) / length;
        }

        // Wound so that inside is negative for both orientations.
        var d = Math.Max(Edge(a, b), Math.Max(Edge(b, c), Edge(c, a)));
        var e = Math.Max(-Edge(a, b), Math.Max(-Edge(b, c), -Edge(c, a)));
        return Math.Min(d, e);
    }

    /// <summary>A 32-bit bitmap as an .ico stores it: bottom row first, twice the height for the empty mask after it.</summary>
    private static byte[] Dib(int size, byte[] bgra)
    {
        var maskRow = (size + 31) / 32 * 4;
        var data = new byte[40 + bgra.Length + maskRow * size];
        BinaryPrimitives.WriteInt32LittleEndian(data, 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), size * 2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 32);
        for (var y = 0; y < size; y++)
        {
            Array.Copy(bgra, (size - 1 - y) * size * 4, data, 40 + y * size * 4, size * 4);
        }

        return data;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        png.Write(number);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb Parse(string hex) => new(
            Convert.ToInt32(hex[1..3], 16) / 255.0, Convert.ToInt32(hex[3..5], 16) / 255.0, Convert.ToInt32(hex[5..7], 16) / 255.0);
    }

    /// <summary>A square of premultiplied pixels, drawn in the mark's 24-unit grid.</summary>
    private sealed class Canvas(int size)
    {
        private readonly double[] _pixels = new double[size * size * 4];
        private readonly double _scale = size / 24.0;

        /// <summary>Paints where <paramref name="shape"/> is inside (its distance below zero), softened over a pixel at its edge.</summary>
        public void Fill(Func<double, double, double> shape, Rgb color)
        {
            Each(shape, (i, cover) =>
            {
                _pixels[i] = color.B * cover + _pixels[i] * (1 - cover);
                _pixels[i + 1] = color.G * cover + _pixels[i + 1] * (1 - cover);
                _pixels[i + 2] = color.R * cover + _pixels[i + 2] * (1 - cover);
                _pixels[i + 3] = cover + _pixels[i + 3] * (1 - cover);
            });
        }

        /// <summary>Cuts <paramref name="shape"/> out, so whatever is behind the icon shows through.</summary>
        public void Erase(Func<double, double, double> shape)
        {
            Each(shape, (i, cover) =>
            {
                for (var c = 0; c < 4; c++)
                {
                    _pixels[i + c] *= 1 - cover;
                }
            });
        }

        public byte[] ToBgra()
        {
            var bytes = new byte[_pixels.Length];
            for (var i = 0; i < _pixels.Length; i += 4)
            {
                var alpha = _pixels[i + 3];
                if (alpha <= 0)
                {
                    continue;
                }

                for (var c = 0; c < 3; c++)
                {
                    bytes[i + c] = (byte)Math.Round(Math.Clamp(_pixels[i + c] / alpha, 0, 1) * 255);
                }

                bytes[i + 3] = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
            }

            return bytes;
        }

        private void Each(Func<double, double, double> shape, Action<int, double> paint)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = shape((x + 0.5) / _scale, (y + 0.5) / _scale) * _scale;
                    var cover = Math.Clamp(0.5 - distance, 0, 1);
                    if (cover > 0)
                    {
                        paint((y * size + x) * 4, cover);
                    }
                }
            }
        }
    }
}
