using Avalonia.Media.Imaging;

namespace GameSync.UI;

/// <summary>
/// Cached art as pictures, decoded at the size they're shown so a library of covers stays small in memory (PERF-01).
/// A file that won't decode is treated as missing, and the slot shows its title cover.
/// </summary>
public static class ArtImages
{
    // A picture still on screen is found again rather than decoded again when the pages refresh; one nothing shows any
    // more is let go with its page. A changed file (a new cover from Steam) is decoded afresh.
    private static readonly Dictionary<(string Path, int Width), (DateTime Written, long Length, WeakReference<Bitmap> Picture)> Decoded = [];

    public static Bitmap? Load(string? path, int width)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            var key = (file.FullName.ToUpperInvariant(), width);
            lock (Decoded)
            {
                if (Decoded.TryGetValue(key, out var known) && known.Written == file.LastWriteTimeUtc && known.Length == file.Length &&
                    known.Picture.TryGetTarget(out var picture))
                {
                    return picture;
                }
            }

            using var stream = file.OpenRead();
            var decoded = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            lock (Decoded)
            {
                if (Decoded.Count > 2000)
                {
                    foreach (var gone in Decoded.Where(d => !d.Value.Picture.TryGetTarget(out _)).Select(d => d.Key).ToList())
                    {
                        Decoded.Remove(gone);
                    }
                }

                Decoded[key] = (file.LastWriteTimeUtc, file.Length, new WeakReference<Bitmap>(decoded));
            }

            return decoded;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Art is untrusted (ART-08): whatever a picture that won't decode throws, it's treated as missing.
            return null;
        }
    }
}
