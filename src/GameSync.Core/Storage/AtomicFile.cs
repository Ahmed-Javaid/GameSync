namespace GameSync.Core.Storage;

/// <summary>Writes go to a temporary file in the same folder and are renamed into place, so a crash never leaves half a file.</summary>
internal static class AtomicFile
{
    public static async Task WriteAsync(string path, Func<Stream, Task> write, bool overwrite, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await write(stream);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static Task WriteAllBytesAsync(string path, byte[] bytes, bool overwrite, CancellationToken ct) =>
        WriteAsync(path, s => s.WriteAsync(bytes, ct).AsTask(), overwrite, ct);
}
