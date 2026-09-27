using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>How much of the history's file contents this PC keeps (BAK-17, FOLD-06). Every record is kept regardless.</summary>
public sealed record HistoryLimits(int VersionsPerGame = 10, long MaxBytes = 2L * 1024 * 1024 * 1024)
{
    public static HistoryLimits Everything { get; } = new(int.MaxValue, long.MaxValue);

    public bool KeepsEverything => VersionsPerGame == int.MaxValue && MaxBytes == long.MaxValue;
}

/// <summary>
/// The backup folder on this PC (FOLD-02): every version record this PC knows, the files of recent versions, and an
/// outbox of versions and pins not uploaded yet. It has the folder cloud's layout, plus <c>outbox/</c> per game and
/// <c>devices/</c> at the top.
/// </summary>
public sealed class LocalHistory
{
    private const string Versions = "versions";
    private const string Pins = "pins";
    private const string Abandoned = "abandoned";

    public LocalHistory(string folder, Action<StorageProblem>? onProblem = null)
    {
        Folder = Path.GetFullPath(folder);
        Blobs = new FolderBlobStore(Folder);
        Log = new FolderVersionLog(Folder, onProblem);
    }

    public string Folder { get; }

    public FolderBlobStore Blobs { get; }

    public FolderVersionLog Log { get; }

    /// <summary>FOLD-05: a backup folder on a drive that isn't connected pauses backups; it never reads as empty.</summary>
    public string? UnavailableReason()
    {
        var drive = Path.GetPathRoot(Folder);
        return drive is not null && !Directory.Exists(drive)
            ? $"The backup folder's drive {drive.TrimEnd('\\')} isn't connected, so backups are paused. Nothing was deleted."
            : null;
    }

    /// <summary>Every game with a folder here; a folder that isn't a game's is left alone.</summary>
    public IEnumerable<GameId> Games()
    {
        var games = Path.Combine(Folder, "games");
        return Directory.Exists(games)
            ? Directory.EnumerateDirectories(games)
                .Select(d => GameId.TryParse(Path.GetFileName(d), out var id) ? id : (GameId?)null)
                .OfType<GameId>()
            : [];
    }

    // ---- outbox: what still has to reach the cloud ----

    public IReadOnlyList<VersionId> PendingVersions(GameId game) => Markers(game, Versions);

    public Task AddPendingVersionAsync(GameId game, VersionId id, CancellationToken ct) => AddMarkerAsync(game, Versions, id, ct);

    public void RemovePendingVersion(GameId game, VersionId id) => File.Delete(MarkerPath(game, Versions, id));

    public IReadOnlyList<VersionId> PendingPins(GameId game) => Markers(game, Pins);

    public Task AddPendingPinAsync(GameId game, VersionId version, CancellationToken ct) => AddMarkerAsync(game, Pins, version, ct);

    public void RemovePendingPin(GameId game, VersionId version) => File.Delete(MarkerPath(game, Pins, version));

    public bool HasPending(GameId game) => PendingVersions(game).Count > 0 || PendingPins(game).Count > 0;

    /// <summary>Versions the cloud lost whose files are gone from this PC too; they're reported once, not retried forever.</summary>
    public IReadOnlyList<VersionId> AbandonedVersions(GameId game) => Markers(game, Abandoned);

    public Task AbandonAsync(GameId game, VersionId id, CancellationToken ct)
    {
        RemovePendingVersion(game, id);
        return AddMarkerAsync(game, Abandoned, id, ct);
    }

    // ---- devices, mirrored so names show offline ----

    public async Task SaveDevicesAsync(IEnumerable<DeviceRecord> devices, CancellationToken ct)
    {
        foreach (var device in devices)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(device, Json.Options);
            await AtomicFile.WriteAllBytesAsync(Path.Combine(Folder, "devices", device.Id.Value + ".json"), bytes, overwrite: true, ct);
        }
    }

    public IReadOnlyList<DeviceRecord> LoadDevices()
    {
        var folder = Path.Combine(Folder, "devices");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var devices = new List<DeviceRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<DeviceRecord>(File.ReadAllBytes(file), Json.Options) is { } device)
                {
                    devices.Add(device);
                }
            }
            catch (JsonException)
            {
                // A damaged copy is refreshed on the next pull.
            }
        }

        return devices;
    }

    // ---- keeping the folder within its limits (BAK-17) ----

    /// <summary>
    /// Deletes file contents beyond <paramref name="limits"/>: per game, everything outside the newest versions and the
    /// ones in <paramref name="prefer"/> (named and pinned saves), then the oldest of those across games until the folder
    /// fits. Never touches versions in <paramref name="keep"/> (current, base, held) or anything still in the outbox.
    /// Returns the bytes freed.
    /// </summary>
    public async Task<long> PruneAsync(IReadOnlyDictionary<GameId, IReadOnlySet<VersionId>> keep, HistoryLimits limits, CancellationToken ct,
        IReadOnlyDictionary<GameId, IReadOnlySet<VersionId>>? prefer = null)
    {
        if (limits.KeepsEverything)
        {
            return 0;
        }

        var games = new List<GameFiles>();
        foreach (var game in Games())
        {
            var versions = await Log.ListAsync(game, ct);
            var hard = new HashSet<VersionId>(keep.TryGetValue(game, out var kept) ? kept : new HashSet<VersionId>());
            hard.UnionWith(PendingVersions(game));
            var files = new GameFiles(game, Blobs.List(game).ToDictionary(b => b.Id, b => b.StoredBytes));
            files.Hard.UnionWith(versions.Where(v => hard.Contains(v.Id)).SelectMany(v => v.Files).Select(f => f.Hash));
            // The newest versions count toward the limit whether or not they're also kept for another reason. Named and
            // pinned saves stay too, so they restore offline, as long as the size limit allows.
            var preferred = prefer?.GetValueOrDefault(game) ?? new HashSet<VersionId>();
            var soft = versions.OrderByDescending(v => v.CreatedUtc).Take(limits.VersionsPerGame)
                .Concat(versions.Where(v => preferred.Contains(v.Id)))
                .DistinctBy(v => v.Id)
                .Where(v => !hard.Contains(v.Id));
            foreach (var version in soft)
            {
                files.AddSoft(version);
            }

            games.Add(files);
        }

        long freed = 0;
        foreach (var files in games)
        {
            foreach (var blob in files.Sizes.Keys.Where(b => !files.IsNeeded(b)).ToList())
            {
                freed += files.Delete(Blobs, blob);
            }
        }

        var total = games.Sum(g => g.Sizes.Values.Sum());
        var oldestFirst = games.SelectMany(g => g.Soft.Select(v => (Files: g, Version: v))).OrderBy(x => x.Version.CreatedUtc).ToList();
        foreach (var (files, version) in oldestFirst)
        {
            if (total <= limits.MaxBytes)
            {
                break;
            }

            foreach (var blob in files.RemoveSoft(version))
            {
                var bytes = files.Delete(Blobs, blob);
                freed += bytes;
                total -= bytes;
            }
        }

        return freed;
    }

    // ---- moving the folder (FOLD-03) ----

    /// <summary>
    /// Copies everything to <paramref name="target"/> and checks every copied file, blobs against their own hash. If
    /// anything fails, the copy is removed and this folder stays as it was. Returns the number of damaged blobs left
    /// behind (the cloud still has them).
    /// </summary>
    public int CopyTo(string target)
    {
        target = Path.GetFullPath(target);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new InvalidOperationException($"{target} isn't empty. Choose an empty or new folder.");
        }

        var skipped = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            {
                if (file.Contains(".tmp-", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(Folder, file);
                var isBlob = file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && relative.Contains($"{Path.DirectorySeparatorChar}blobs{Path.DirectorySeparatorChar}");
                if (isBlob && !BlobIntact(file))
                {
                    skipped++;
                    continue;
                }

                var copy = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
                var intact = isBlob ? BlobIntact(copy) : Hash(copy).SequenceEqual(Hash(file));
                if (!intact)
                {
                    throw new IOException($"{relative} didn't copy correctly.");
                }
            }
        }
        catch
        {
            TryDelete(target);
            throw;
        }

        return skipped;
    }

    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The drive went away or a file is open; what's left is harmless and the old folder stays in use.
        }
    }

    private static bool BlobIntact(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var hash = BlobId.FromHash(SHA256.HashData(gzip));
            return hash.Value == Path.GetFileNameWithoutExtension(path);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] Hash(string path)
    {
        using var file = File.OpenRead(path);
        return SHA256.HashData(file);
    }

    private IReadOnlyList<VersionId> Markers(GameId game, string kind)
    {
        var folder = Path.Combine(Folder, "games", game.Value, "outbox", kind);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .Where(n => n is not null && !n.Contains(".tmp-", StringComparison.Ordinal))
            .Select(n => VersionId.TryParse(n, out var id) ? id : (VersionId?)null)
            .OfType<VersionId>()
            .Order()
            .ToList();
    }

    private Task AddMarkerAsync(GameId game, string kind, VersionId id, CancellationToken ct) =>
        AtomicFile.WriteAllBytesAsync(MarkerPath(game, kind, id), [], overwrite: true, ct);

    private string MarkerPath(GameId game, string kind, VersionId id) => Path.Combine(Folder, "games", game.Value, "outbox", kind, id.Value);

    /// <summary>One game's blobs during pruning: which are needed no matter what, and which only by recent versions.</summary>
    private sealed class GameFiles(GameId game, Dictionary<BlobId, long> sizes)
    {
        private readonly Dictionary<BlobId, int> _softUses = [];

        public Dictionary<BlobId, long> Sizes { get; } = sizes;

        public HashSet<BlobId> Hard { get; } = [];

        public List<VersionRecord> Soft { get; } = [];

        public void AddSoft(VersionRecord version)
        {
            Soft.Add(version);
            foreach (var hash in version.Files.Select(f => f.Hash).Distinct())
            {
                _softUses[hash] = _softUses.GetValueOrDefault(hash) + 1;
            }
        }

        /// <summary>Drops a recent version from what's kept and returns the blobs nothing kept needs any more.</summary>
        public IReadOnlyList<BlobId> RemoveSoft(VersionRecord version)
        {
            var released = new List<BlobId>();
            foreach (var hash in version.Files.Select(f => f.Hash).Distinct())
            {
                var uses = _softUses[hash] - 1;
                _softUses[hash] = uses;
                if (uses == 0 && !Hard.Contains(hash) && Sizes.ContainsKey(hash))
                {
                    released.Add(hash);
                }
            }

            return released;
        }

        public bool IsNeeded(BlobId blob) => Hard.Contains(blob) || _softUses.GetValueOrDefault(blob) > 0;

        public long Delete(FolderBlobStore blobs, BlobId blob)
        {
            blobs.Delete(game, blob);
            return Sizes.Remove(blob, out var bytes) ? bytes : 0;
        }
    }
}
