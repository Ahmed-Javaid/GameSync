using GameSync.UI.Theming;

namespace GameSync.Core.Tests;

/// <summary>
/// The owner, 18 Oct 2026: "The achievements are squares inside the viewer, They should be smoothed out or filled".
/// Phasmophobia, Ready or Not, Valheim and others draw a square frame into their achievement icons; the badge shows only
/// the picture inside it, and an icon with no frame stays whole.
/// </summary>
public class IconFrameTests
{
    private const int Size = 64;

    [Fact]
    public void ACH_03_a_frame_drawn_into_an_icon_is_found_and_the_picture_inside_it_kept()
    {
        // A grey square line five pixels in from each edge, on black, around a glyph, as Phasmophobia draws its icons.
        var framed = Icon((x, y) => Ring(x, y, 5) || Ring(x, y, 6) ? 150 : Glyph(x, y) ? 120 : 15);
        // Past the line, two pixels thick at five and six in, and no further than it needs.
        Assert.InRange(IconFrame.Inset(framed, Size, Size), 7, 9);

        // Hand-drawn, wobbling a pixel in and out along each side, and darker than the picture: found too.
        var wobbly = Icon((x, y) => Ring(x, y, (x + y) % 9 < 4 ? 7 : 8) ? 10 : 200);
        Assert.InRange(IconFrame.Inset(wobbly, Size, Size), 9, 12);

        // Red first, as some pictures decode: the same frame.
        Assert.Equal(IconFrame.Inset(framed, Size, Size), IconFrame.Inset(framed, Size, Size, bgra: false));
    }

    [Fact]
    public void ACH_03_an_icon_with_no_frame_or_one_at_its_very_edge_stays_whole()
    {
        // Art to the edge: a soft gradient, a round coin, noise.
        Assert.Equal(0, IconFrame.Inset(Icon((x, y) => (x * 3) + y), Size, Size));
        Assert.Equal(0, IconFrame.Inset(Icon((x, y) => ((x - 32) * (x - 32)) + ((y - 32) * (y - 32)) < 600 ? 220 : 30), Size, Size));
        var random = new Random(7);
        Assert.Equal(0, IconFrame.Inset(Icon((_, _) => random.Next(256)), Size, Size));

        // A line at the edge itself meets the badge's ring, so nothing is cut; nor from a picture too small or not square.
        Assert.Equal(0, IconFrame.Inset(Icon((x, y) => Ring(x, y, 0) ? 200 : 20), Size, Size));
        Assert.Equal(0, IconFrame.Inset(new byte[16 * 16 * 4], 16, 16));
        Assert.Equal(0, IconFrame.Inset(new byte[64 * 48 * 4], 64, 48));
    }

    private static bool Ring(int x, int y, int k) =>
        (x == k || x == Size - 1 - k || y == k || y == Size - 1 - k) && x >= k && x <= Size - 1 - k && y >= k && y <= Size - 1 - k;

    private static bool Glyph(int x, int y) => Math.Abs(x - y) < 4 && x > 16 && x < 48;

    /// <summary>A grey picture, blue first, 4 bytes a pixel, from each pixel's lightness.</summary>
    private static byte[] Icon(Func<int, int, int> lightness)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var v = (byte)Math.Clamp(lightness(x, y), 0, 255);
                var i = ((y * Size) + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (v, v, v, 255);
            }
        }

        return pixels;
    }
}
