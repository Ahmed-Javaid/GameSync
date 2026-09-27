using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GameSync.Snapshots;

/// <summary>Stand-in cover art like the design system's previews: a dark ground, a pale sun and hills, never a real game's art.</summary>
public static class SampleArt
{
    private static readonly (string Ground, string Accent)[] CoverColours =
    [
        ("#2b2220", "#e0b36a"), ("#1d2a24", "#7fd1a6"), ("#221f33", "#b9a3ff"), ("#1d2630", "#7fe6f2"), ("#2c2519", "#f2b544"), ("#22262b", "#a4abb3"), ("#2a1f24", "#ff8a7d"),
    ];

    public static Bitmap Cover(int index)
    {
        var (ground, accent) = CoverColours[index % CoverColours.Length];
        return Draw(200, 300, context =>
        {
            context.FillRectangle(Brush(ground), new Rect(0, 0, 200, 300));
            context.DrawEllipse(Brush(accent, 0.5), null, new Point(140, 90), 38, 38);
            context.DrawGeometry(Brush(accent, 0.35), null, Geometry.Parse("M0 220 L70 160 L130 210 L200 150 V300 H0Z"));
        });
    }

    public static Bitmap Hero(string ground = "#1b2a33", string accent = "#7fe6f2") => Draw(1200, 500, context =>
    {
        context.FillRectangle(Brush(ground), new Rect(0, 0, 1200, 500));
        context.DrawEllipse(Brush(accent, 0.35), null, new Point(860, 150), 70, 70);
        context.DrawGeometry(Brush("#223640"), null, Geometry.Parse("M0 330 L180 200 L330 300 L520 150 L700 290 L880 190 L1200 320 V500 H0Z"));
        context.DrawGeometry(Brush("#182730"), null, Geometry.Parse("M0 400 L220 290 L420 380 L640 270 L860 370 L1040 300 L1200 360 V500 H0Z"));
        context.DrawGeometry(Brush("#10191f"), null, Geometry.Parse("M0 460 L300 390 L600 450 L900 380 L1200 440 V500 H0Z"));
    });

    private static IBrush Brush(string hex, double opacity = 1) => new SolidColorBrush(Color.Parse(hex), opacity);

    private static Bitmap Draw(int width, int height, Action<DrawingContext> draw)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = bitmap.CreateDrawingContext())
        {
            draw(context);
        }

        return bitmap;
    }
}
