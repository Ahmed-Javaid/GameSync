using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>
/// The window's title bar in the theme's colours: on Windows 11 it takes the app's ground and ink, so the frame and the
/// page read as one; on Windows 10 it's Windows' own dark or light bar.
/// </summary>
public static class WindowFrame
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    /// <param name="ground">The title bar's colour, as 0xRRGGBB.</param>
    /// <param name="ink">Its text colour, as 0xRRGGBB.</param>
    public static void Paint(IntPtr window, bool dark, int ground, int ink)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref on, sizeof(int));
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                var caption = ColorRef(ground);
                var text = ColorRef(ink);
                DwmSetWindowAttribute(window, CaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(window, BorderColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(window, TextColor, ref text, sizeof(int));
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // An older Windows keeps its own title bar.
        }
    }

    /// <summary>Windows wants 0x00BBGGRR.</summary>
    private static int ColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
