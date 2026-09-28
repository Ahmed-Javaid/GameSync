using GameSync.UI.Branding;

namespace GameSync.Snapshots;

/// <summary>
/// The app's icon file, and a sheet of the tray icon in every state at each size Windows uses, on a dark and a light
/// taskbar, drawn four times larger pixel for pixel so each pixel can be checked.
/// </summary>
internal static class Icons
{
    private static readonly int[] TraySizes = [16, 20, 24, 32];
    private static readonly TrayBadge[] Badges = Enum.GetValues<TrayBadge>();

    public static IEnumerable<string> Write(string output)
    {
        var ico = Path.Combine(output, "gamesync.ico");
        File.WriteAllBytes(ico, MarkArt.Ico(MarkArt.IconSizes, MarkArt.AppIcon));
        yield return ico;

        var sheet = Path.Combine(output, "tray-icons.png");
        File.WriteAllBytes(sheet, Sheet());
        yield return sheet;
    }

    private static byte[] Sheet()
    {
        const int zoom = 4;
        const int cell = 40 * zoom;
        var width = Badges.Length * cell * 2;
        var height = TraySizes.Length * cell;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < TraySizes.Length; row++)
        {
            var size = TraySizes[row];
            for (var col = 0; col < Badges.Length * 2; col++)
            {
                var light = col >= Badges.Length;
                var ground = light ? (0xEE, 0xEE, 0xEE) : (0x20, 0x20, 0x20);
                var icon = MarkArt.Tray(size, Badges[col % Badges.Length], light);
                var left = col * cell + (cell - size * zoom) / 2;
                var top = row * cell + (cell - size * zoom) / 2;
                for (var y = row * cell; y < (row + 1) * cell; y++)
                {
                    for (var x = col * cell; x < (col + 1) * cell; x++)
                    {
                        var (r, g, b) = ground;
                        var (ix, iy) = ((x - left) / zoom, (y - top) / zoom);
                        if (x >= left && y >= top && ix < size && iy < size)
                        {
                            var i = (iy * size + ix) * 4;
                            var a = icon[i + 3] / 255.0;
                            (r, g, b) = ((int)Math.Round(icon[i + 2] * a + r * (1 - a)), (int)Math.Round(icon[i + 1] * a + g * (1 - a)), (int)Math.Round(icon[i] * a + b * (1 - a)));
                        }

                        var o = (y * width + x) * 4;
                        (pixels[o], pixels[o + 1], pixels[o + 2], pixels[o + 3]) = ((byte)b, (byte)g, (byte)r, 255);
                    }
                }
            }
        }

        return MarkArt.Png(width, height, pixels);
    }
}
