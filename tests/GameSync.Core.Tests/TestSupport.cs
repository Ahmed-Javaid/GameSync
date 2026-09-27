using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Storage.Drive;

namespace GameSync.Core.Tests;

/// <summary>
/// A cloud that simulated PCs share: a temporary folder, or an in-memory Google Drive. Everything on disk lives under
/// %TEMP% and is deleted afterwards.
/// </summary>
public sealed class TestWorld : IDisposable
{
    private readonly Func<TestPc?, ICloud>? _cloud;

    /// <param name="drive">Share an in-memory Google Drive instead of the cloud folder.</param>
    /// <param name="cloud">Or any other cloud, such as the real Drive in a test folder.</param>
    public TestWorld(bool drive = false, Func<TestPc?, ICloud>? cloud = null)
    {
        Directory.CreateDirectory(Cloud);
        Drive = drive ? new FakeDrive() : null;
        _cloud = cloud;
    }

    /// <summary>The cloud as <paramref name="pc"/> sees it, or a plain view of it when null.</summary>
    public ICloud CloudFor(TestPc? pc) =>
        _cloud?.Invoke(pc)
        ?? (Drive is { } drive
            ? new DriveCloud(drive, pc is null ? null : id => pc.Games.FirstOrDefault(g => g.Id == id)?.Title)
            : new FolderCloud(Cloud));

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gamesync-tests", Guid.NewGuid().ToString("N")[..12]);

    public string Cloud => Path.Combine(Root, "cloud");

    /// <summary>Set when the PCs share an in-memory Google Drive instead of the cloud folder.</summary>
    public FakeDrive? Drive { get; }

    public TestPc Pc(string name) => new(this, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder isn't worth failing a test over.
        }
    }
}

/// <summary>One simulated PC: its own data folder, device, save folders and state database.</summary>
public sealed class TestPc : IDisposable
{
    private readonly TestWorld _world;
    private readonly List<GameDefinition> _games = [];

    public TestPc(TestWorld world, string name)
    {
        _world = world;
        Name = name;
        DataDir = Path.Combine(world.Root, name, "data");
        State = new StateStore(DataDir);
        State.GetOrCreateDevice(name);
        HistoryDir = Path.Combine(DataDir, "history");
        var home = Path.Combine(world.Root, name, "home");
        KnownFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<home>"] = home,
            ["<documents>"] = Path.Combine(home, "Documents"),
            ["<roaming>"] = Path.Combine(home, "AppData", "Roaming"),
            ["<localAppData>"] = Path.Combine(home, "AppData", "Local"),
            ["<localLow>"] = Path.Combine(home, "AppData", "LocalLow"),
            ["<savedGames>"] = Path.Combine(home, "Saved Games"),
        };
    }

    public string Name { get; }

    public string DataDir { get; }

    /// <summary>This PC's backup folder (FOLD-02); tests may move it, for example onto a drive that isn't there.</summary>
    public string HistoryDir { get; set; }

    public StateStore State { get; }

    /// <summary>Read each time, so a rename (PC-02) shows up.</summary>
    public DeviceInfo Device => State.GetOrCreateDevice(Name);

    /// <summary>This PC's own folders for the portable placeholders (FIND-08), all under the test's temp folder.</summary>
    public Dictionary<string, string> KnownFolders { get; }

    public Dictionary<string, string> Accounts { get; } = [];

    public Dictionary<GameId, string> InstallDirs { get; } = [];

    public FakeMalwareScanner Malware { get; } = new();

    /// <summary>This PC's registry, in memory.</summary>
    public FakeRegistry Registry { get; } = new();

    public SyncOptions Options { get; set; } = new() { AppVersion = "test" };

    /// <summary>Makes cloud calls fail: given the call's name ("blobs.put", "log.append", "info", ...), the error to throw.</summary>
    public Func<string, CloudException?>? Fault { get; set; }

    /// <summary>PC-05: every cloud call fails as if the network were down.</summary>
    public bool Offline
    {
        set => Fault = value ? _ => new CloudException(CloudErrorKind.Offline, "You're offline.") : null;
    }

    /// <summary>SYNC-08: this PC's clock minus the cloud's; null means the cloud reports no clock.</summary>
    public TimeSpan? ClockSkew { get; set; }

    /// <summary>CLOUD-05: what the cloud reports as used and total bytes.</summary>
    public (long Used, long Total)? Storage { get; set; }

    public IReadOnlyList<GameDefinition> Games => _games;

    /// <summary>The game's folder for <paramref name="root"/> on this PC: resolved if the game uses placeholders.</summary>
    public string Folder(string game, string root = "saves") =>
        _games.FirstOrDefault(g => g.Id.Value == game)?.Roots.GetValueOrDefault(root) is { } resolved && !RootResolver.IsUnresolved(resolved)
            ? resolved
            : Path.Combine(_world.Root, Name, "games", game, root);

    /// <summary>A game whose save folder is portable (FIND-08), resolved with this PC's folders, accounts and install folders.</summary>
    public GameDefinition AddPortableGame(string id, string portableRoot, ConflictPolicy policy = ConflictPolicy.NewestWins)
    {
        var portable = new GameDefinition
        {
            Id = GameId.Parse(id),
            Title = id,
            ConflictPolicy = policy,
            Roots = new Dictionary<string, string> { ["saves"] = portableRoot },
            Rules = [new SaveRule { Root = "saves" }],
        };
        var game = new RootResolver(KnownFolders, Accounts, InstallDirs).Resolve(portable);
        if (!RootResolver.IsUnresolved(game.Roots["saves"]))
        {
            Directory.CreateDirectory(game.Roots["saves"]);
        }

        _games.Add(game);
        return game;
    }

    /// <summary>A game a scan found and confirmed (FIND-06), resolved with the folders that scan used.</summary>
    public GameDefinition AddResolvedGame(GameDefinition portable, IReadOnlyDictionary<string, string> folders)
    {
        var game = new RootResolver(folders, Accounts, InstallDirs).Resolve(portable);
        _games.Add(game);
        return game;
    }

    public GameDefinition AddGame(
        string id,
        GameMode mode = GameMode.Sync,
        ConflictPolicy policy = ConflictPolicy.NewestWins,
        string include = "**",
        bool withConfig = false,
        string? savesFolder = null)
    {
        var roots = new Dictionary<string, string> { ["saves"] = savesFolder ?? Folder(id) };
        var rules = new List<SaveRule> { new() { Root = "saves", Include = include } };
        if (withConfig)
        {
            roots["config"] = Folder(id, "config");
            rules.Add(new SaveRule { Root = "config", Category = SaveCategory.Config });
        }

        foreach (var folder in roots.Values.Where(f => savesFolder is null || f != savesFolder))
        {
            Directory.CreateDirectory(folder);
        }

        var game = new GameDefinition { Id = GameId.Parse(id), Title = id, Mode = mode, ConflictPolicy = policy, Roots = roots, Rules = rules };
        _games.Add(game);
        return game;
    }

    public SyncService Service(string? cloud = null) => new(
        _games,
        new LocalHistory(HistoryDir),
        new FaultyCloud(cloud is null ? _world.CloudFor(this) : new FolderCloud(cloud), this),
        State,
        new SnapshotScanner(SensitivePathGuard.ForThisPc(DataDir), State),
        Malware,
        Device,
        DataDir,
        Options with { Registry = Options.Registry ?? Registry });

    public Task<IReadOnlyList<GameResult>> SyncAsync(string? cloud = null) => Service(cloud).SyncAsync(null, CancellationToken.None);

    public string Write(string game, string relative, string content, DateTime? modifiedUtc = null, string root = "saves") =>
        WriteBytes(game, relative, Encoding.UTF8.GetBytes(content), modifiedUtc, root);

    public string WriteBytes(string game, string relative, byte[] content, DateTime? modifiedUtc = null, string root = "saves")
    {
        var path = Path.Combine(Folder(game, root), relative.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, modifiedUtc ?? DateTime.UtcNow);
        return path;
    }

    public string Read(string game, string relative, string root = "saves") =>
        File.ReadAllText(Path.Combine(Folder(game, root), relative.Replace('/', '\\')));

    /// <summary>Every file under a save folder: relative path to SHA-256 and modified time.</summary>
    public SortedDictionary<string, (string Hash, DateTime Modified)> Tree(string game, string root = "saves")
    {
        var folder = Folder(game, root);
        var tree = new SortedDictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
        {
            return tree;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            tree[Path.GetRelativePath(folder, file).Replace('\\', '/')] =
                (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), File.GetLastWriteTimeUtc(file));
        }

        return tree;
    }

    /// <summary>Records a play session around now, so changes written now count as made during play.</summary>
    public void Played(string game) =>
        State.AddSession(GameId.Parse(game), new SessionInfo(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1)));

    public void Dispose() => State.Dispose();
}

/// <summary>A registry in memory: keys by name, and a count of every key written, so a test can tell nothing else changed.</summary>
public sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<string, (RegistryNode Node, DateTime ChangedUtc)> _keys = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Written { get; } = [];

    public void Set(string key, RegistryNode node, DateTime changedUtc) => _keys[Name(key)] = (Copy(node), changedUtc);

    public void Remove(string key) => _keys.Remove(Name(key));

    public RegistryNode? Get(string key) => _keys.TryGetValue(Name(key), out var entry) ? entry.Node : null;

    public IReadOnlyCollection<string> Keys => _keys.Keys;

    public (RegistryNode Node, DateTime ChangedUtc)? Export(string key) =>
        _keys.TryGetValue(Name(key), out var entry) ? (Copy(entry.Node), entry.ChangedUtc) : null;

    public void Import(string key, RegistryNode node)
    {
        Written.Add(Name(key));
        _keys[Name(key)] = (Copy(node), DateTime.UtcNow);
    }

    private static string Name(string key) => key.Replace('\\', '/').Replace("HKCU/", "HKEY_CURRENT_USER/", StringComparison.OrdinalIgnoreCase).Trim('/');

    private static RegistryNode Copy(RegistryNode node) => RegistryFile.Read(RegistryFile.Write("x", node)).Node;
}

/// <summary>A cloud a test can take offline, fill up, or give a clock and a quota, through its <see cref="TestPc"/>.</summary>
public sealed class FaultyCloud(ICloud inner, TestPc pc) : ICloud
{
    public IBlobStore Blobs { get; } = new FaultyBlobs(inner.Blobs, pc);

    public IVersionLog Log { get; } = new FaultyLog(inner.Log, pc);

    public async Task<CloudInfo> GetInfoAsync(CancellationToken ct)
    {
        Check(pc, "info");
        var info = await inner.GetInfoAsync(ct);
        return info with
        {
            ServerTimeUtc = pc.ClockSkew is { } skew ? DateTime.UtcNow - skew : null,
            UsedBytes = pc.Storage?.Used,
            TotalBytes = pc.Storage?.Total,
        };
    }

    public Task<IReadOnlyList<GameId>> ListGamesAsync(CancellationToken ct) => Check(pc, "games.list", () => inner.ListGamesAsync(ct));

    public Task<IReadOnlyList<DeviceRecord>> ListDevicesAsync(CancellationToken ct) => Check(pc, "devices.list", () => inner.ListDevicesAsync(ct));

    public Task SaveDeviceAsync(DeviceRecord device, CancellationToken ct) => Check(pc, "devices.save", () => inner.SaveDeviceAsync(device, ct));

    public Task WriteLatestAsync(GameId game, VersionRecord version, Func<FileEntry, CancellationToken, Task<Stream>> open, CancellationToken ct) =>
        Check(pc, "latest", () => inner.WriteLatestAsync(game, version, open, ct));

    public Task WriteRestoreKitAsync(CancellationToken ct) => Check(pc, "kit", () => inner.WriteRestoreKitAsync(ct));

    internal static void Check(TestPc pc, string call)
    {
        if (pc.Fault?.Invoke(call) is { } fault)
        {
            throw fault;
        }
    }

    internal static T Check<T>(TestPc pc, string call, Func<T> action)
    {
        Check(pc, call);
        return action();
    }

    private sealed class FaultyBlobs(IBlobStore inner, TestPc pc) : IBlobStore
    {
        public Task<bool> ExistsAsync(GameId game, BlobId id, CancellationToken ct) => Check(pc, "blobs.exists", () => inner.ExistsAsync(game, id, ct));

        public Task PutAsync(GameId game, BlobId id, Stream content, CancellationToken ct) => Check(pc, "blobs.put", () => inner.PutAsync(game, id, content, ct));

        public Task<Stream> GetAsync(GameId game, BlobId id, CancellationToken ct) => Check(pc, "blobs.get", () => inner.GetAsync(game, id, ct));

        public Task TrashAsync(GameId game, BlobId id, CancellationToken ct) => Check(pc, "blobs.trash", () => inner.TrashAsync(game, id, ct));
    }

    private sealed class FaultyLog(IVersionLog inner, TestPc pc) : IVersionLog
    {
        public Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct) => Check(pc, "log.list", () => inner.ListAsync(game, ct));

        public Task<IReadOnlySet<VersionId>> ListIdsAsync(GameId game, CancellationToken ct) => Check(pc, "log.ids", () => inner.ListIdsAsync(game, ct));

        public Task<VersionRecord?> GetAsync(GameId game, VersionId id, CancellationToken ct) => Check(pc, "log.get", () => inner.GetAsync(game, id, ct));

        public Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct) => Check(pc, "log.append", () => inner.AppendAsync(game, version, ct));

        public Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct) => Check(pc, "log.pins", () => inner.ListPinsAsync(game, ct));

        public Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct) => Check(pc, "log.pin", () => inner.SetPinAsync(game, pin, ct));

        public Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct) => Check(pc, "log.unpin", () => inner.RemovePinAsync(game, version, ct));

        public Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct) => Check(pc, "log.thin", () => inner.MarkThinnedAsync(game, version, ct));

        public Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct) => Check(pc, "log.thinned", () => inner.ListThinnedAsync(game, ct));

        public Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct) => Check(pc, "log.marker", () => inner.GetMarkerAsync(game, ct));

        public Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct) => Check(pc, "log.setmarker", () => inner.SetMarkerAsync(game, marker, ct));
    }
}

/// <summary>Stands in for AMSI: flags any file containing a marker, so tests never trip the real antivirus.</summary>
public sealed class FakeMalwareScanner : IMalwareScanner
{
    public const string Marker = "GAMESYNC-TEST-MALWARE-MARKER";

    public ScanVerdict ScanFile(string fullPath) =>
        File.ReadAllText(fullPath).Contains(Marker, StringComparison.Ordinal) ? ScanVerdict.Detected : ScanVerdict.Clean;
}

public static class Cloud
{
    public static async Task<IReadOnlyList<VersionRecord>> VersionsAsync(TestWorld world, string game, string? cloud = null) =>
        cloud is not null
            ? await new FolderVersionLog(cloud).ListAsync(GameId.Parse(game), CancellationToken.None)
            : await world.CloudFor(null).Log.ListAsync(GameId.Parse(game), CancellationToken.None);

    public static string VersionsFolder(string cloud, string game) => Path.Combine(cloud, "games", game, "versions");

    public static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
    }
}
