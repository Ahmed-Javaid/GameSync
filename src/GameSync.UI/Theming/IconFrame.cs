namespace GameSync.UI.Theming;

/// <summary>
/// A frame some games draw into their achievement icons (Phasmophobia's square line, a few pixels in from the edge),
/// found so the icon can fill its rounded badge without a square inside it (the owner, 18 Oct 2026: "The achievements
/// are squares inside the viewer, They should be smoothed out or filled"). An icon with no such line is left whole.
/// </summary>
public static class IconFrame
{
    /// <summary>How much brighter or darker than the picture beside it a line has to be, out of 255.</summary>
    private const double Contrast = 28;

    /// <summary>How far outside and inside a line the picture it stands out from is looked at, in pixels.</summary>
    private const int Reach = 3;

    /// <summary>
    /// How many pixels in from each edge the picture inside a drawn frame starts, past the line; 0 when the icon has none.
    /// A frame is a line that stands out the same way (brighter, or darker) from the pixels just outside and just inside
    /// it, at the same distance from every edge, along most of each side; one at the very edge is left, as it meets the
    /// badge's ring. Art that merely changes near its edge, or busy art, doesn't stand out one way all round.
    /// </summary>
    /// <param name="pixels">The picture, 4 bytes a pixel, rows one after another.</param>
    /// <param name="bgra">Blue first (Windows' usual order); false for red first.</param>
    public static int Inset(byte[] pixels, int width, int height, bool bgra = true)
    {
        if (width != height || width < 32 || pixels.Length < width * height * 4)
        {
            return 0;
        }

        var (red, blue) = bgra ? (2, 0) : (0, 2);
        double Luma(int x, int y)
        {
            var i = ((y * width) + x) * 4;
            return (0.2126 * pixels[i + red]) + (0.7152 * pixels[i + 1]) + (0.0722 * pixels[i + blue]);
        }

        var last = width / 6;
        for (var k = 2; k <= last; k++)
        {
            if (Lined(k, brighter: true) || Lined(k, brighter: false))
            {
                return Math.Min(k + Reach, width / 5);
            }
        }

        return 0;

        // A line at k on all four sides, brighter than the picture beside it all round, or darker all round: most points
        // along the middle of each side stand out that way, the line allowed one pixel of wobble (k or k + 1), as a
        // hand-drawn one has.
        bool Lined(int k, bool brighter)
        {
            for (var side = 0; side < 4; side++)
            {
                var (hits, seen) = (0, 0);
                for (var along = width / 10; along < width - (width / 10); along++)
                {
                    seen++;
                    if (StandsOut(side, along, k, brighter) || StandsOut(side, along, k + 1, brighter))
                    {
                        hits++;
                    }
                }

                if (hits < seen * 0.7)
                {
                    return false;
                }
            }

            return true;
        }

        bool StandsOut(int side, int along, int depth, bool brighter)
        {
            var outside = Math.Max(0, depth - Reach);
            var inside = depth + Reach;
            var (line, outer, inner) = side switch
            {
                0 => (Luma(along, depth), Luma(along, outside), Luma(along, inside)),
                1 => (Luma(along, height - 1 - depth), Luma(along, height - 1 - outside), Luma(along, height - 1 - inside)),
                2 => (Luma(depth, along), Luma(outside, along), Luma(inside, along)),
                _ => (Luma(width - 1 - depth, along), Luma(width - 1 - outside, along), Luma(width - 1 - inside, along)),
            };
            return brighter
                ? line - outer > Contrast && line - inner > Contrast
                : outer - line > Contrast && inner - line > Contrast;
        }
    }
}
