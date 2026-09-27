using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Model;
using GameSync.Core.Storage;

namespace GameSync.Core.Tests;

public class StorageTests
{
    private static readonly GameId G = GameId.Parse("game");

    [Fact]
    public async Task BAK_02_a_file_shared_by_two_versions_is_stored_once()
    {
        using var world = new TestWorld();
        var blobs = new FolderBlobStore(world.Cloud);
        var world1 = Encoding.UTF8.GetBytes("an unchanged Terraria world");

        await blobs.PutAsync(G, Hash(world1), new MemoryStream(world1), CancellationToken.None);
        await blobs.PutAsync(G, Hash(world1), new MemoryStream(world1), CancellationToken.None);

        Assert.Single(Directory.EnumerateFiles(Path.Combine(world.Cloud, "games", "game", "blobs"), "*", SearchOption.AllDirectories));
        await using var back = await blobs.GetAsync(G, Hash(world1), CancellationToken.None);
        using var reader = new StreamReader(back);
        Assert.Equal("an unchanged Terraria world", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task A_blob_whose_contents_dont_match_its_hash_is_never_stored()
    {
        using var world = new TestWorld();
        var blobs = new FolderBlobStore(world.Cloud);
        var claimed = Hash("what the file said it was"u8.ToArray());

        await Assert.ThrowsAsync<BlobMismatchException>(() =>
            blobs.PutAsync(G, claimed, new MemoryStream("what it really is"u8.ToArray()), CancellationToken.None));

        Assert.False(await blobs.ExistsAsync(G, claimed, CancellationToken.None));
        Assert.False(Directory.EnumerateFiles(world.Cloud, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task BAK_02_versions_are_immutable()
    {
        using var world = new TestWorld();
        var log = new FolderVersionLog(world.Cloud);
        var version = new VersionRecord
        {
            Id = VersionId.Parse("v1"),
            Game = G,
            Kind = VersionKind.Normal,
            Origin = VersionOrigin.FirstBackup,
            Device = new DeviceInfo(DeviceId.Parse("d-desktop"), "DESKTOP"),
            CreatedUtc = DateTime.UtcNow,
            Files = [],
        };

        await log.AppendAsync(G, version, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => log.AppendAsync(G, version with { Label = "changed" }, CancellationToken.None));
        Assert.Null(Assert.Single(await log.ListAsync(G, CancellationToken.None)).Label);
    }

    [Fact]
    public async Task R8_a_tampered_record_is_skipped_and_reported()
    {
        using var world = new TestWorld();
        var problems = new List<StorageProblem>();
        var log = new FolderVersionLog(world.Cloud, problems.Add);
        var folder = Directory.CreateDirectory(Path.Combine(world.Cloud, "games", "game", "versions")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "v9.json"), "{ not json");

        Assert.Empty(await log.ListAsync(G, CancellationToken.None));
        Assert.Single(problems);
    }

    private static BlobId Hash(byte[] bytes) => BlobId.FromHash(SHA256.HashData(bytes));
}
