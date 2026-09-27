using System.Globalization;
using System.Text;
using System.Text.Json;
using GameSync.Core.Model;
using GameSync.Core.Safety;

namespace GameSync.Core.Storage;

/// <summary>
/// A folder as the cloud: another drive, a NAS or a network share, and the stand-in for Drive in tests. A folder that
/// isn't reachable is Offline, never empty, so a disconnected drive can't look like a cloud that lost everything.
/// </summary>
public sealed class FolderCloud : ICloud
{
    private readonly string _root;

    public FolderCloud(string root, Action<StorageProblem>? onProblem = null)
    {
        _root = Path.GetFullPath(root);
        Blobs = new ReachableBlobs(this, new FolderBlobStore(_root));
        Log = new ReachableLog(this, new FolderVersionLog(_root, onProblem));
    }

    public IBlobStore Blobs { get; }

    public IVersionLog Log { get; }

    public Task<CloudInfo> GetInfoAsync(CancellationToken ct)
    {
        EnsureReachable();
        return Task.FromResult(new CloudInfo(_root, null, null, null, null));
    }

    public Task<IReadOnlyList<GameId>> ListGamesAsync(CancellationToken ct)
    {
        EnsureReachable();
        var folder = Path.Combine(_root, "games");
        IReadOnlyList<GameId> games = Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Select(d => GameId.TryParse(Path.GetFileName(d), out var id) ? id : (GameId?)null).OfType<GameId>().ToList()
            : [];
        return Task.FromResult(games);
    }

    public Task<IReadOnlyList<DeviceRecord>> ListDevicesAsync(CancellationToken ct)
    {
        EnsureReachable();
        var folder = Path.Combine(_root, "devices");
        var devices = new List<DeviceRecord>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<DeviceRecord>(File.ReadAllBytes(file), Json.Options) is { } device &&
                        device.Id.Value == Path.GetFileNameWithoutExtension(file))
                    {
                        devices.Add(device);
                    }
                }
                catch (JsonException)
                {
                    // Skipped; the PC rewrites its own record on its next sync.
                }
            }
        }

        return Task.FromResult<IReadOnlyList<DeviceRecord>>(devices);
    }

    public Task SaveDeviceAsync(DeviceRecord device, CancellationToken ct)
    {
        EnsureReachable();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(device, Json.Options);
        return AtomicFile.WriteAllBytesAsync(Path.Combine(_root, "devices", device.Id.Value + ".json"), bytes, overwrite: true, ct);
    }

    public async Task WriteLatestAsync(GameId game, VersionRecord version, Func<FileEntry, CancellationToken, Task<Stream>> open, CancellationToken ct)
    {
        EnsureReachable();
        var latest = Path.Combine(_root, "games", game.Value, "latest");
        var wanted = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in version.Files)
        {
            var (rootKey, relative) = RestorePathGuard.Split(file.Path);
            wanted[RestorePathGuard.Resolve(Path.Combine(latest, rootKey), relative)] = file;
        }

        if (Directory.Exists(latest))
        {
            foreach (var existing in Directory.EnumerateFiles(latest, "*", SearchOption.AllDirectories))
            {
                if (!wanted.ContainsKey(existing) && !existing.Equals(Path.Combine(latest, "VERSION.txt"), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(existing);
                }
            }
        }

        foreach (var (target, file) in wanted)
        {
            if (File.Exists(target) && new FileInfo(target).Length == file.Size && File.GetLastWriteTimeUtc(target) == file.ModifiedUtc)
            {
                continue;
            }

            await using (var content = await open(file, ct))
            {
                await AtomicFile.WriteAsync(target, s => content.CopyToAsync(s, ct), overwrite: true, ct);
            }

            File.SetLastWriteTimeUtc(target, file.ModifiedUtc);
        }

        await AtomicFile.WriteAllBytesAsync(Path.Combine(latest, "VERSION.txt"), Encoding.UTF8.GetBytes(Describe(version)), overwrite: true, ct);
    }

    public async Task WriteRestoreKitAsync(CancellationToken ct)
    {
        EnsureReachable();
        foreach (var (name, bytes) in RestoreKit.Files)
        {
            var path = Path.Combine(_root, name);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                await AtomicFile.WriteAllBytesAsync(path, bytes, overwrite: true, ct);
            }
        }
    }

    /// <summary>The text of <c>latest/VERSION.txt</c>: which version the plain copy holds.</summary>
    public static string Describe(VersionRecord version) =>
        $"This folder holds version {version.Id}, saved on {version.Device.Name} at " +
        $"{version.CreatedUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}.{Environment.NewLine}";

    private void EnsureReachable()
    {
        if (!Directory.Exists(_root))
        {
            throw new CloudException(CloudErrorKind.Offline, $"The cloud folder {_root} isn't reachable.");
        }
    }

    private sealed class ReachableBlobs(FolderCloud cloud, FolderBlobStore inner) : IBlobStore
    {
        public Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct) => Checked(() => inner.ExistsAsync(game, id, ct));

        public Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct) => Checked(() => inner.PutAsync(game, id, content, ct));

        public Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct) => Checked(() => inner.GetAsync(game, id, ct));

        public Task TrashAsync(GameId game, BlobId id, CancellationToken ct) => Checked(() => inner.TrashAsync(game, id, ct));

        private T Checked<T>(Func<T> action)
        {
            cloud.EnsureReachable();
            return action();
        }
    }

    private sealed class ReachableLog(FolderCloud cloud, FolderVersionLog inner) : IVersionLog
    {
        public Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct) => Checked(() => inner.ListAsync(game, ct));

        public Task<IReadOnlySet<VersionId>> ListIdsAsync(GameId game, CancellationToken ct) => Checked(() => inner.ListIdsAsync(game, ct));

        public Task<VersionRecord?> GetAsync(GameId game, VersionId id, CancellationToken ct) => Checked(() => inner.GetAsync(game, id, ct));

        public Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct) => Checked(() => inner.AppendAsync(game, version, ct));

        public Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct) => Checked(() => inner.ListPinsAsync(game, ct));

        public Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct) => Checked(() => inner.SetPinAsync(game, pin, ct));

        public Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct) => Checked(() => inner.RemovePinAsync(game, version, ct));

        public Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct) => Checked(() => inner.MarkThinnedAsync(game, version, ct));

        public Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct) => Checked(() => inner.ListThinnedAsync(game, ct));

        public Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct) => Checked(() => inner.GetMarkerAsync(game, ct));

        public Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct) => Checked(() => inner.SetMarkerAsync(game, marker, ct));

        private T Checked<T>(Func<T> action)
        {
            cloud.EnsureReachable();
            return action();
        }
    }
}
