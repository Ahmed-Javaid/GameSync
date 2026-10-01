using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameSync.UI.Theming;

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

    /// <summary>
    /// A picture for a cover's 2:3 slot (a tile, a list row): portrait art as it is, as Steam's covers are; a square or
    /// wide picture, such as a console game's icon from its own folder (KAN-59), whole in the middle of a cover made
    /// from a blurred copy of itself (<see cref="CoverArt"/>), so its logo is never cut. Made once, like any decoded picture.
    /// </summary>
    public static Bitmap? LoadCover(string? path, int width)
    {
        if (Load(path, width) is not { } picture || CoverArt.IsCover(picture.PixelSize.Width, picture.PixelSize.Height))
        {
            return Load(path, width);
        }

        try
        {
            var file = new FileInfo(path!);
            var key = (file.FullName.ToUpperInvariant(), -width);
            lock (Decoded)
            {
                if (Decoded.TryGetValue(key, out var known) && known.Written == file.LastWriteTimeUtc && known.Length == file.Length &&
                    known.Picture.TryGetTarget(out var made))
                {
                    return made;
                }
            }

            using var stream = file.OpenRead();
            using var decoded = WriteableBitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            byte[] pixels;
            int w, h;
            using (var frame = decoded.Lock())
            {
                (w, h) = (frame.Size.Width, frame.Size.Height);
                pixels = new byte[w * h * 4];
                for (var y = 0; y < h; y++)
                {
                    Marshal.Copy(frame.Address + y * frame.RowBytes, pixels, y * w * 4, w * 4);
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
                    return picture;
                }
            }

            var cover = CoverArt.Compose(pixels, w, h);
            var pinned = GCHandle.Alloc(cover, GCHandleType.Pinned);
            Bitmap composed;
            try
            {
                composed = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pinned.AddrOfPinnedObject(), new PixelSize(w, CoverArt.HeightFor(w)), new Vector(96, 96), w * 4);
            }
            finally
            {
                pinned.Free();
            }

            lock (Decoded)
            {
                Decoded[key] = (file.LastWriteTimeUtc, file.Length, new WeakReference<Bitmap>(composed));
            }

            return composed;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return picture;
        }
    }

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
