using Avalonia.Media.Imaging;

namespace GameSync.UI;

/// <summary>
/// Cached art as pictures, decoded at the size they're shown so a library of covers stays small in memory (PERF-01).
/// A file that won't decode is treated as missing, and the slot shows its title cover.
/// </summary>
public static class ArtImages
{
    public static Bitmap? Load(string? path, int width)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
