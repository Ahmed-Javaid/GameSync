namespace GameSync.UI.Theming;

/// <summary>
/// A 2:3 cover made from a picture that isn't one (KAN-59), such as a console game's square icon: the picture whole, the
/// width of the cover, in its middle, over a blurred and darkened copy of itself filling the rest, so the logo on it is
/// never cut. Pixels are BGRA, opaque; the work is done once, as the picture is decoded.
/// </summary>
public static class CoverArt
{
    /// <summary>Taller than this against its width, a picture is already a cover and is shown as it is.</summary>
    public const double PortraitAt = 1.2;

    /// <summary>The blurred copy is worked out this many times smaller than the cover, then drawn back up smoothly.</summary>
    private const int Shrink = 8;

    /// <summary>What's left of the blurred copy's light, so the picture in the middle stands out.</summary>
    private const double Dim = 0.45;

    public static bool IsCover(int width, int height) => height >= width * PortraitAt;

    /// <summary>The cover's height for a width: 2:3.</summary>
    public static int HeightFor(int width) => (int)Math.Round(width * 1.5);

    /// <param name="bgra">The picture, <paramref name="width"/> wide (the cover's width) and <paramref name="height"/> high.</param>
    /// <returns>The cover's pixels: <paramref name="width"/> × <see cref="HeightFor"/>.</returns>
    public static byte[] Compose(byte[] bgra, int width, int height)
    {
        var coverHeight = HeightFor(width);
        var cover = new byte[width * coverHeight * 4];

        // The blurred copy: the picture filling the cover (its sides cut, as a cover crop does), averaged into a small
        // image, blurred there, then drawn back up to the cover's size and dimmed.
        int smallWidth = Math.Max(2, width / Shrink), smallHeight = Math.Max(2, coverHeight / Shrink);
        var small = Fill(bgra, width, height, smallWidth, smallHeight);
        for (var pass = 0; pass < 2; pass++)
        {
            Blur(small, smallWidth, smallHeight, horizontal: true);
            Blur(small, smallWidth, smallHeight, horizontal: false);
        }

        for (var y = 0; y < coverHeight; y++)
        {
            var fy = Math.Clamp((y + 0.5) * smallHeight / coverHeight - 0.5, 0, smallHeight - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, smallHeight - 1);
            var ty = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Clamp((x + 0.5) * smallWidth / width - 0.5, 0, smallWidth - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, smallWidth - 1);
                var tx = fx - x0;
                var o = (y * width + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var top = small[(y0 * smallWidth + x0) * 3 + c] * (1 - tx) + small[(y0 * smallWidth + x1) * 3 + c] * tx;
                    var bottom = small[(y1 * smallWidth + x0) * 3 + c] * (1 - tx) + small[(y1 * smallWidth + x1) * 3 + c] * tx;
                    cover[o + c] = (byte)Math.Clamp(Math.Round((top * (1 - ty) + bottom * ty) * Dim), 0, 255);
                }

                cover[o + 3] = 255;
            }
        }

        // The picture itself, whole, in the middle; a picture taller than the cover (it can't be, past PortraitAt) is cut
        // at the top and bottom.
        var shown = Math.Min(height, coverHeight);
        var from = (height - shown) / 2;
        var to = (coverHeight - shown) / 2;
        for (var row = 0; row < shown; row++)
        {
            Buffer.BlockCopy(bgra, (from + row) * width * 4, cover, (to + row) * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                cover[((to + row) * width + x) * 4 + 3] = 255;
            }
        }

        return cover;
    }

    /// <summary>The picture scaled to fill <paramref name="w"/> × <paramref name="h"/> (a cover crop), each pixel the average of what it covers, as RGB.</summary>
    private static double[] Fill(byte[] bgra, int width, int height, int w, int h)
    {
        var rgb = new double[w * h * 3];
        var scale = Math.Max((double)w / width, (double)h / height);
        double seenWidth = w / scale, seenHeight = h / scale;
        double offsetX = (width - seenWidth) / 2, offsetY = (height - seenHeight) / 2;
        for (var ty = 0; ty < h; ty++)
        {
            var y0 = Math.Clamp((int)Math.Floor(offsetY + ty * seenHeight / h), 0, height - 1);
            var y1 = Math.Clamp((int)Math.Floor(offsetY + (ty + 1) * seenHeight / h), y0 + 1, height);
            for (var tx = 0; tx < w; tx++)
            {
                var x0 = Math.Clamp((int)Math.Floor(offsetX + tx * seenWidth / w), 0, width - 1);
                var x1 = Math.Clamp((int)Math.Floor(offsetX + (tx + 1) * seenWidth / w), x0 + 1, width);
                double b = 0, g = 0, r = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = (y * width + x) * 4;
                        (b, g, r) = (b + bgra[i], g + bgra[i + 1], r + bgra[i + 2]);
                    }
                }

                var n = (double)(y1 - y0) * (x1 - x0);
                var o = (ty * w + tx) * 3;
                (rgb[o], rgb[o + 1], rgb[o + 2]) = (b / n, g / n, r / n);
            }
        }

        return rgb;
    }

    /// <summary>One box pass of half-width 2 along rows or columns, the edges held.</summary>
    private static void Blur(double[] rgb, int width, int height, bool horizontal)
    {
        const int radius = 2;
        var lines = horizontal ? height : width;
        var length = horizontal ? width : height;
        var line = new double[length * 3];
        for (var l = 0; l < lines; l++)
        {
            int At(int p) => (horizontal ? l * width + p : p * width + l) * 3;
            for (var p = 0; p < length; p++)
            {
                double b = 0, g = 0, r = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var i = At(Math.Clamp(p + k, 0, length - 1));
                    (b, g, r) = (b + rgb[i], g + rgb[i + 1], r + rgb[i + 2]);
                }

                const double n = 2 * radius + 1;
                (line[p * 3], line[p * 3 + 1], line[p * 3 + 2]) = (b / n, g / n, r / n);
            }

            for (var p = 0; p < length; p++)
            {
                var i = At(p);
                (rgb[i], rgb[i + 1], rgb[i + 2]) = (line[p * 3], line[p * 3 + 1], line[p * 3 + 2]);
            }
        }
    }
}
