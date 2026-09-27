using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Storage;

namespace GameSync.Storage.Drive;

/// <summary>
/// Google Drive as the cloud (CLOUD-01). Everything lives in one GameSync folder with the layout in design.md →
/// Storage backends, except that blobs sit flat in each game's <c>blobs</c> folder. Folders are found by app
/// properties, so the GameSync folder can be renamed or moved. When two PCs create the same folder at once, the older
/// one wins and the other's contents move into it.
/// </summary>
public sealed class DriveCloud : ICloud
{
    internal const string MarkKey = "gamesync";
    internal const string GameKey = "gamesyncGame";
    internal const string HashKey = "sha256";
    private const string RootMark = "root";
    private const string VersionFile = "VERSION.txt";

    private readonly IDriveClient _drive;
    private readonly Func<GameId, string?> _titleOf;
    private readonly Action<StorageProblem>? _onProblem;
    private readonly string _rootName;
    private readonly string _rootMark;
    private readonly SemaphoreSlim _layout = new(1, 1);
    private readonly Dictionary<string, List<DriveItem>> _children = [];
    private readonly Dictionary<GameId, string> _gameFolders = [];
    private readonly ConcurrentDictionary<GameId, Lazy<Task<ConcurrentDictionary<BlobId, string>>>> _blobIndex = new();
    private readonly ConcurrentDictionary<GameId, IReadOnlyDictionary<VersionId, string>> _versionFiles = new();
    private string? _root;

    /// <param name="rootName">The GameSync folder's name when GameSync creates it.</param>
    /// <param name="rootMark">What marks the folder as GameSync's; a test gets its own mark, so it never touches the real folder.</param>
    public DriveCloud(IDriveClient drive, Func<GameId, string?>? titleOf = null, Action<StorageProblem>? onProblem = null,
        string rootName = "GameSync", string rootMark = RootMark)
    {
        _drive = drive;
        _titleOf = titleOf ?? (_ => null);
        _onProblem = onProblem;
        _rootName = rootName;
        _rootMark = rootMark;
        Blobs = new DriveBlobs(this);
        Log = new DriveLog(this);
    }

    public IBlobStore Blobs { get; }

    public IVersionLog Log { get; }

    public async Task<CloudInfo> GetInfoAsync(CancellationToken ct)
    {
        var root = await FolderAsync(null, null, ct);
        var about = await _drive.AboutAsync(ct);
        return new CloudInfo(_drive.FolderLink(root), about.Email, about.UsedBytes, about.LimitBytes, about.ServerTimeUtc);
    }

    public async Task<IReadOnlyList<GameId>> ListGamesAsync(CancellationToken ct)
    {
        var games = await FolderAsync(null, "games", ct);
        await _layout.WaitAsync(ct);
        try
        {
            return (await ChildrenAsync(games, ct))
                .Where(c => c.IsFolder)
                .Select(c => GameId.TryParse(c.AppProperties.GetValueOrDefault(GameKey), out var id) ? id : (GameId?)null)
                .OfType<GameId>()
                .Distinct()
                .ToList();
        }
        finally
        {
            _layout.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceRecord>> ListDevicesAsync(CancellationToken ct)
    {
        var folder = await FolderAsync(null, "devices", ct);
        var devices = new List<DeviceRecord>();
        foreach (var item in await _drive.ListChildrenAsync(folder, ct))
        {
            if (!item.IsFolder && item.Name.EndsWith(".json", StringComparison.Ordinal) &&
                await ReadJsonAsync<DeviceRecord>(null, item, ct) is { } device && item.Name == device.Id.Value + ".json" &&
                !devices.Any(d => d.Id == device.Id))
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    public async Task SaveDeviceAsync(DeviceRecord device, CancellationToken ct)
    {
        var folder = await FolderAsync(null, "devices", ct);
        await PutSmallFileAsync(folder, device.Id.Value + ".json", "application/json", JsonSerializer.SerializeToUtf8Bytes(device, Json.Options), ct);
    }

    public async Task WriteLatestAsync(GameId game, VersionRecord version, Func<FileEntry, CancellationToken, Task<Stream>> open, CancellationToken ct)
    {
        var latest = await FolderAsync(game, "latest", ct);
        var files = new Dictionary<string, DriveItem>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = latest };
        await WalkAsync(latest, "", files, folders, ct);

        var wanted = version.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, item) in files)
        {
            if (!wanted.Contains(path) && path != VersionFile)
            {
                await _drive.TrashAsync(item.Id, ct);
            }
        }

        foreach (var file in version.Files)
        {
            RestorePathGuard.Split(file.Path);
            var existing = files.GetValueOrDefault(file.Path);
            if (existing?.AppProperties.GetValueOrDefault(HashKey) == file.Hash.Value)
            {
                continue;
            }

            var slash = file.Path.LastIndexOf('/');
            var name = file.Path[(slash + 1)..];
            var folder = await EnsurePathAsync(folders, file.Path[..slash], ct);
            await using var plain = TempFile();
            await using (var content = await open(file, ct))
            {
                await content.CopyToAsync(plain, ct);
            }

            plain.Position = 0;
            var properties = new Dictionary<string, string> { [HashKey] = file.Hash.Value };
            if (existing is null)
            {
                await _drive.CreateFileAsync(folder, name, "application/octet-stream", plain, file.ModifiedUtc, properties, ct);
            }
            else
            {
                await _drive.UpdateFileAsync(existing.Id, "application/octet-stream", plain, file.ModifiedUtc, properties, ct);
            }
        }

        await PutSmallFileAsync(latest, VersionFile, "text/plain", Encoding.UTF8.GetBytes(FolderCloud.Describe(version)), ct);
    }

    public async Task WriteRestoreKitAsync(CancellationToken ct)
    {
        var root = await FolderAsync(null, null, ct);
        var children = await _drive.ListChildrenAsync(root, ct);
        foreach (var (name, bytes) in RestoreKit.Files)
        {
            var existing = children.FirstOrDefault(c => !c.IsFolder && c.Name == name);
            if (existing?.Md5 is { } md5 && md5.Equals(Convert.ToHexStringLower(MD5.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var content = new MemoryStream(bytes);
            if (existing is null)
            {
                await _drive.CreateFileAsync(root, name, "text/plain", content, null, null, ct);
            }
            else
            {
                await _drive.UpdateFileAsync(existing.Id, "text/plain", content, null, null, ct);
            }
        }
    }

    // ---- the folder layout ----

    /// <summary>The GameSync folder, <c>devices</c>, a game's folder, or one of the game's folders.</summary>
    private async Task<string> FolderAsync(GameId? game, string? sub, CancellationToken ct)
    {
        await _layout.WaitAsync(ct);
        try
        {
            var root = await RootAsync(ct);
            var parent = game is { } id ? await GameFolderAsync(root, id, ct) : root;
            return sub is null ? parent : await ChildFolderAsync(parent, sub, ct);
        }
        finally
        {
            _layout.Release();
        }
    }

    private async Task<string> RootAsync(CancellationToken ct)
    {
        if (_root is not null)
        {
            return _root;
        }

        var found = (await _drive.FindFoldersAsync(MarkKey, _rootMark, ct)).ToList();
        if (found.Count == 0)
        {
            var created = await _drive.CreateFolderAsync(null, _rootName, new Dictionary<string, string> { [MarkKey] = _rootMark }, ct);

            // Another PC may have made one at the same moment, and search can lag behind a create.
            found = (await _drive.FindFoldersAsync(MarkKey, _rootMark, ct)).ToList();
            if (!found.Any(f => f.Id == created.Id))
            {
                found.Add(created);
            }
        }

        return _root = await KeepOldestAsync(null, found, ct);
    }

    private async Task<string> GameFolderAsync(string root, GameId game, CancellationToken ct)
    {
        if (_gameFolders.TryGetValue(game, out var cached))
        {
            return cached;
        }

        var games = await ChildFolderAsync(root, "games", ct);
        var found = (await ChildrenAsync(games, ct)).Where(c => c.IsFolder && c.AppProperties.GetValueOrDefault(GameKey) == game.Value).ToList();
        if (found.Count == 0)
        {
            found.Add(await CreateFolderAsync(games, GameFolderName(game), new Dictionary<string, string> { [GameKey] = game.Value }, ct));
        }

        return _gameFolders[game] = await KeepOldestAsync(games, found, ct);
    }

    private async Task<string> ChildFolderAsync(string parent, string name, CancellationToken ct)
    {
        var found = (await ChildrenAsync(parent, ct)).Where(c => c.IsFolder && c.Name == name).ToList();
        if (found.Count == 0)
        {
            found.Add(await CreateFolderAsync(parent, name, null, ct));
        }

        return await KeepOldestAsync(parent, found, ct);
    }

    /// <summary>
    /// Of several copies of one folder, keeps the oldest and moves everything in the others into it. An emptied copy is
    /// renamed and unmarked first, so it's never picked again, then trashed. Drive refuses the trash when another app
    /// left something in it that GameSync can't see; the copy then just stays, empty as far as GameSync knows.
    /// </summary>
    private async Task<string> KeepOldestAsync(string? parent, List<DriveItem> copies, CancellationToken ct)
    {
        var keep = copies.OrderBy(c => c.CreatedUtc).ThenBy(c => c.Id, StringComparer.Ordinal).First();
        foreach (var extra in copies.Where(c => c.Id != keep.Id).DistinctBy(c => c.Id))
        {
            foreach (var child in await _drive.ListChildrenAsync(extra.Id, ct))
            {
                await _drive.MoveAsync(child.Id, extra.Id, keep.Id, ct);
            }

            var retired = extra.AppProperties.Keys.ToDictionary(key => key, _ => "merged");
            await _drive.UpdateMetadataAsync(extra.Id, $"{extra.Name} (merged)", retired, ct);
            try
            {
                await _drive.TrashAsync(extra.Id, ct);
            }
            catch (CloudException e) when (e.Kind == CloudErrorKind.Other)
            {
                // Something another app put there keeps it out of the trash; renamed and unmarked, it's harmless.
            }

            _children.Remove(keep.Id);
            _children.Remove(extra.Id);
            if (parent is not null && _children.TryGetValue(parent, out var siblings))
            {
                siblings.RemoveAll(s => s.Id == extra.Id);
            }
        }

        return keep.Id;
    }

    private async Task<List<DriveItem>> ChildrenAsync(string folder, CancellationToken ct)
    {
        if (!_children.TryGetValue(folder, out var children))
        {
            children = (await _drive.ListChildrenAsync(folder, ct)).ToList();
            _children[folder] = children;
        }

        return children;
    }

    private async Task<DriveItem> CreateFolderAsync(string parent, string name, IReadOnlyDictionary<string, string>? properties, CancellationToken ct)
    {
        var siblings = await ChildrenAsync(parent, ct);
        var folder = await _drive.CreateFolderAsync(parent, name, properties, ct);
        if (!siblings.Any(s => s.Id == folder.Id))
        {
            siblings.Add(folder);
        }

        _children[folder.Id] = [];
        return folder;
    }

    private string GameFolderName(GameId game)
    {
        var title = _titleOf(game);
        if (string.IsNullOrWhiteSpace(title) || title == game.Value)
        {
            return game.Value;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return $"{game.Value} {new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim()}";
    }

    private async Task WalkAsync(string folder, string prefix, Dictionary<string, DriveItem> files, Dictionary<string, string> folders, CancellationToken ct)
    {
        foreach (var item in await _drive.ListChildrenAsync(folder, ct))
        {
            var path = prefix.Length == 0 ? item.Name : $"{prefix}/{item.Name}";
            if (item.IsFolder)
            {
                if (folders.TryAdd(path, item.Id))
                {
                    await WalkAsync(item.Id, path, files, folders, ct);
                }
            }
            else
            {
                files.TryAdd(path, item);
            }
        }
    }

    private async Task<string> EnsurePathAsync(Dictionary<string, string> folders, string path, CancellationToken ct)
    {
        if (folders.TryGetValue(path, out var id))
        {
            return id;
        }

        var slash = path.LastIndexOf('/');
        var parent = slash < 0 ? folders[""] : await EnsurePathAsync(folders, path[..slash], ct);
        var created = await _drive.CreateFolderAsync(parent, slash < 0 ? path : path[(slash + 1)..], null, ct);
        return folders[path] = created.Id;
    }

    // ---- files ----

    /// <summary>Creates or replaces a small file by name, such as a device record or a pin.</summary>
    private async Task PutSmallFileAsync(string folder, string name, string mimeType, byte[] bytes, CancellationToken ct)
    {
        var existing = (await _drive.ListChildrenAsync(folder, ct)).FirstOrDefault(c => !c.IsFolder && c.Name == name);
        using var content = new MemoryStream(bytes);
        if (existing is null)
        {
            await _drive.CreateFileAsync(folder, name, mimeType, content, null, null, ct);
        }
        else
        {
            await _drive.UpdateFileAsync(existing.Id, mimeType, content, null, null, ct);
        }
    }

    private async Task<T?> ReadJsonAsync<T>(GameId? game, DriveItem item, CancellationToken ct) where T : class
    {
        using var buffer = new MemoryStream();
        await _drive.DownloadAsync(item.Id, buffer, ct);
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), Json.Options);
        }
        catch (JsonException e)
        {
            if (game is { } id)
            {
                _onProblem?.Invoke(new StorageProblem(id, item.Name, $"Unreadable: {e.Message}"));
            }

            return null;
        }
    }

    private static FileStream TempFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GameSync");
        Directory.CreateDirectory(folder);
        return new FileStream(Path.Combine(folder, $"{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    }

    // ---- blobs ----

    private async Task<ConcurrentDictionary<BlobId, string>> BlobIndexAsync(GameId game, CancellationToken ct)
    {
        var lazy = _blobIndex.GetOrAdd(game, g => new Lazy<Task<ConcurrentDictionary<BlobId, string>>>(() => LoadBlobIndexAsync(g, ct)));
        try
        {
            return await lazy.Value;
        }
        catch
        {
            // A failed listing is tried again by the next call, not remembered.
            _blobIndex.TryRemove(new KeyValuePair<GameId, Lazy<Task<ConcurrentDictionary<BlobId, string>>>>(game, lazy));
            throw;
        }
    }

    private async Task<ConcurrentDictionary<BlobId, string>> LoadBlobIndexAsync(GameId game, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "blobs", ct);
        var index = new ConcurrentDictionary<BlobId, string>();
        foreach (var item in await _drive.ListChildrenAsync(folder, ct))
        {
            if (!item.IsFolder && item.Name.EndsWith(".gz", StringComparison.Ordinal) && BlobId.TryParse(item.Name[..^3], out var id))
            {
                index.TryAdd(id, item.Id);
            }
        }

        return index;
    }

    private async Task PutBlobAsync(GameId game, BlobId id, Stream content, CancellationToken ct)
    {
        var index = await BlobIndexAsync(game, ct);
        if (index.ContainsKey(id))
        {
            return;
        }

        var folder = await FolderAsync(game, "blobs", ct);
        await using var packed = TempFile();
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await gzip.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            var actual = BlobId.FromHash(sha.GetHashAndReset());
            if (actual != id)
            {
                throw new BlobMismatchException($"The file changed while it was being read (expected {id}, got {actual}).");
            }
        }

        packed.Position = 0;
        var md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(packed, ct));
        packed.Position = 0;
        var item = await _drive.CreateFileAsync(folder, $"{id.Value}.gz", "application/gzip", packed, null,
            new Dictionary<string, string> { [HashKey] = id.Value }, ct);
        if (item.Md5 is { } stored && !stored.Equals(md5, StringComparison.OrdinalIgnoreCase))
        {
            await _drive.TrashAsync(item.Id, ct);
            throw new CloudException(CloudErrorKind.Other, "Google Drive stored something other than what was sent; the upload is tried again next sync.");
        }

        index[id] = item.Id;
    }

    private async Task<Stream> GetBlobAsync(GameId game, BlobId id, CancellationToken ct)
    {
        var index = await BlobIndexAsync(game, ct);
        if (!index.TryGetValue(id, out var fileId))
        {
            throw new FileNotFoundException($"The cloud doesn't have the file {id.Value[..12]}… any more.");
        }

        var temp = TempFile();
        try
        {
            await _drive.DownloadAsync(fileId, temp, ct);
            temp.Position = 0;
            return new GZipStream(temp, CompressionMode.Decompress);
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }

    private async Task TrashBlobAsync(GameId game, BlobId id, CancellationToken ct)
    {
        var index = await BlobIndexAsync(game, ct);
        if (index.TryRemove(id, out var fileId))
        {
            await _drive.TrashAsync(fileId, ct);
        }
    }

    // ---- records ----

    private async Task<IReadOnlySet<VersionId>> ListVersionIdsAsync(GameId game, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "versions", ct);
        var files = new Dictionary<VersionId, string>();
        foreach (var item in await _drive.ListChildrenAsync(folder, ct))
        {
            if (item.IsFolder || !item.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            if (VersionId.TryParse(item.Name[..^5], out var id))
            {
                files.TryAdd(id, item.Id);
            }
            else
            {
                _onProblem?.Invoke(new StorageProblem(game, item.Name, "Not a version record's name."));
            }
        }

        _versionFiles[game] = files;
        return files.Keys.ToHashSet();
    }

    private async Task<VersionRecord?> GetVersionAsync(GameId game, VersionId id, CancellationToken ct)
    {
        if (!_versionFiles.TryGetValue(game, out var files) || !files.ContainsKey(id))
        {
            await ListVersionIdsAsync(game, ct);
            files = _versionFiles[game];
        }

        if (!files.TryGetValue(id, out var fileId))
        {
            return null;
        }

        var record = await ReadJsonAsync<VersionRecord>(game, new DriveItem(fileId, id.Value + ".json", false, 0, null, default, new Dictionary<string, string>()), ct);
        if (record is not null && (record.Id != id || record.Game != game))
        {
            _onProblem?.Invoke(new StorageProblem(game, id.Value + ".json", "The version's name doesn't match its contents."));
            return null;
        }

        return record;
    }

    private async Task AppendVersionAsync(GameId game, VersionRecord version, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "versions", ct);
        using var content = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(version, Json.Options));
        await _drive.CreateFileAsync(folder, version.Id.Value + ".json", "application/json", content, null, null, ct);
    }

    private async Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "pins", ct);
        var pins = new List<PinRecord>();
        foreach (var item in await _drive.ListChildrenAsync(folder, ct))
        {
            if (!item.IsFolder && item.Name.EndsWith(".json", StringComparison.Ordinal) &&
                await ReadJsonAsync<PinRecord>(game, item, ct) is { } pin && item.Name == pin.Version.Value + ".json" &&
                !pins.Any(p => p.Version == pin.Version))
            {
                pins.Add(pin);
            }
        }

        return pins;
    }

    private async Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "pins", ct);
        await PutSmallFileAsync(folder, pin.Version.Value + ".json", "application/json", JsonSerializer.SerializeToUtf8Bytes(pin, Json.Options), ct);
    }

    private async Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "pins", ct);
        foreach (var item in (await _drive.ListChildrenAsync(folder, ct)).Where(i => i.Name == version.Value + ".json"))
        {
            await _drive.TrashAsync(item.Id, ct);
        }
    }

    private async Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "thinned", ct);
        return (await _drive.ListChildrenAsync(folder, ct))
            .Select(i => VersionId.TryParse(i.Name, out var id) ? id : (VersionId?)null)
            .OfType<VersionId>()
            .ToHashSet();
    }

    private async Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct)
    {
        var folder = await FolderAsync(game, "thinned", ct);
        if (!(await _drive.ListChildrenAsync(folder, ct)).Any(i => i.Name == version.Value))
        {
            using var empty = new MemoryStream();
            await _drive.CreateFileAsync(folder, version.Value, "application/octet-stream", empty, null, null, ct);
        }
    }

    private async Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct)
    {
        var folder = await FolderAsync(game, null, ct);
        var marker = (await _drive.ListChildrenAsync(folder, ct)).FirstOrDefault(i => !i.IsFolder && i.Name == "playing.json");
        return marker is null ? null : await ReadJsonAsync<SessionMarker>(game, marker, ct);
    }

    private async Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct)
    {
        var folder = await FolderAsync(game, null, ct);
        if (marker is not null)
        {
            await PutSmallFileAsync(folder, "playing.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(marker, Json.Options), ct);
            return;
        }

        foreach (var item in (await _drive.ListChildrenAsync(folder, ct)).Where(i => !i.IsFolder && i.Name == "playing.json"))
        {
            await _drive.TrashAsync(item.Id, ct);
        }
    }

    private sealed class DriveBlobs(DriveCloud cloud) : IBlobStore
    {
        public async Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct) => (await cloud.BlobIndexAsync(game, ct)).ContainsKey(id);

        public Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct) => cloud.PutBlobAsync(game, id, content, ct);

        public Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct) => cloud.GetBlobAsync(game, id, ct);

        public Task TrashAsync(GameId game, BlobId id, CancellationToken ct) => cloud.TrashBlobAsync(game, id, ct);
    }

    private sealed class DriveLog(DriveCloud cloud) : IVersionLog
    {
        public async Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct)
        {
            var records = new List<VersionRecord>();
            foreach (var id in (await cloud.ListVersionIdsAsync(game, ct)).Order())
            {
                if (await cloud.GetVersionAsync(game, id, ct) is { } record)
                {
                    records.Add(record);
                }
            }

            return records.OrderBy(r => r.CreatedUtc).ThenBy(r => r.Id).ToList();
        }

        public Task<IReadOnlySet<VersionId>> ListIdsAsync(GameId game, CancellationToken ct) => cloud.ListVersionIdsAsync(game, ct);

        public Task<VersionRecord?> GetAsync(GameId game, VersionId id, CancellationToken ct) => cloud.GetVersionAsync(game, id, ct);

        public Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct) => cloud.AppendVersionAsync(game, version, ct);

        public Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct) => cloud.ListPinsAsync(game, ct);

        public Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct) => cloud.SetPinAsync(game, pin, ct);

        public Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct) => cloud.RemovePinAsync(game, version, ct);

        public Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct) => cloud.MarkThinnedAsync(game, version, ct);

        public Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct) => cloud.ListThinnedAsync(game, ct);

        public Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct) => cloud.GetMarkerAsync(game, ct);

        public Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct) => cloud.SetMarkerAsync(game, marker, ct);
    }
}
