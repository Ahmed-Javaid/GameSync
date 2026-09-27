using System.Security.Cryptography;
using System.Text;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Core.Tests;

/// <summary>A temporary cloud folder that simulated PCs share. Everything lives under %TEMP% and is deleted afterwards.</summary>
public sealed class TestWorld : IDisposable
{
    public TestWorld()
    {
        Directory.CreateDirectory(Cloud);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gamesync-tests", Guid.NewGuid().ToString("N")[..12]);

    public string Cloud => Path.Combine(Root, "cloud");

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
        Device = State.GetOrCreateDevice(name);
    }

    public string Name { get; }

    public string DataDir { get; }

    public StateStore State { get; }

    public DeviceInfo Device { get; }

    public FakeMalwareScanner Malware { get; } = new();

    public IReadOnlyList<GameDefinition> Games => _games;

    public string Folder(string game, string root = "saves") => Path.Combine(_world.Root, Name, "games", game, root);

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
        new FolderBlobStore(cloud ?? _world.Cloud),
        new FolderVersionLog(cloud ?? _world.Cloud),
        State,
        new SnapshotScanner(SensitivePathGuard.ForThisPc(DataDir), State),
        Malware,
        Device,
        DataDir);

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
        await new FolderVersionLog(cloud ?? world.Cloud).ListAsync(GameId.Parse(game), CancellationToken.None);

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
