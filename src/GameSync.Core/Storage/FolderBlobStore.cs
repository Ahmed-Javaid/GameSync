using System.IO.Compression;
using System.Security.Cryptography;
using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>Blobs as gzip files under <c>games/&lt;game&gt;/blobs/ab/&lt;hash&gt;.gz</c>, the layout the Drive backend will use.</summary>
public sealed class FolderBlobStore(string root) : IBlobStore
{
    public Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct) => Task.FromResult(File.Exists(PathFor(game, id)));

    public async Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct)
    {
        var path = PathFor(game, id);
        if (File.Exists(path))
        {
            return;
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await AtomicFile.WriteAsync(path, async file =>
            {
                await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await gzip.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                await gzip.FlushAsync(ct);
                var actual = BlobId.FromHash(sha.GetHashAndReset());
                if (actual != id)
                {
                    // Thrown before the rename, so the mismatched file never lands under the wrong name.
                    throw new BlobMismatchException($"The file changed while it was being read (expected {id}, got {actual}).");
                }
            }, overwrite: false, ct);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another writer stored the same content first; blobs are content-addressed, so that's fine.
        }
    }

    public Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct)
    {
        var file = new FileStream(PathFor(game, id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream>(new GZipStream(file, CompressionMode.Decompress));
    }

    public Task TrashAsync(GameId game, BlobId id, CancellationToken ct)
    {
        var path = PathFor(game, id);
        if (File.Exists(path))
        {
            var trash = Path.Combine(root, "games", game.Value, "trash", "blobs", id.Value + ".gz");
            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            File.Move(path, trash, overwrite: true);
        }

        return Task.CompletedTask;
    }

    private string PathFor(GameId game, BlobId id) =>
        Path.Combine(root, "games", game.Value, "blobs", id.Value[..2], id.Value + ".gz");
}
