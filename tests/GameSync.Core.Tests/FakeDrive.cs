using System.Security.Cryptography;
using GameSync.Core.Storage;
using GameSync.Storage.Drive;

namespace GameSync.Core.Tests;

/// <summary>
/// An in-memory Google Drive with Drive's habits: every item has an id and one parent, names may repeat in a folder,
/// app properties, MD5s and a trash. Tests can inject failures by call name, flag files as malware, or corrupt uploads.
/// </summary>
public sealed class FakeDrive : IDriveClient
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Node> _nodes = [];
    private int _nextId;
    private DateTime _clock = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    public sealed class Node
    {
        public required string Id { get; init; }

        public required string Name { get; set; }

        public required string Parent { get; set; }

        public bool IsFolder { get; init; }

        public byte[] Content { get; set; } = [];

        public Dictionary<string, string> AppProperties { get; set; } = [];

        public DateTime CreatedUtc { get; init; }

        public DateTime? ModifiedUtc { get; set; }

        public bool Trashed { get; set; }

        /// <summary>Google flagged it as malware; downloads fail (CLOUD-06).</summary>
        public bool Flagged { get; set; }

        /// <summary>Put there by another app, such as a Drive sync client: GameSync can't see it, so Drive won't let it trash the folder.</summary>
        public bool Hidden { get; init; }
    }

    /// <summary>Given a call's name ("list", "find", "createFolder", "createFile", "updateFile", "download", "trash", "move", "about"), the error to throw.</summary>
    public Func<string, CloudException?>? Fault { get; set; }

    /// <summary>Makes the MD5 Drive reports for an upload differ from what was sent.</summary>
    public bool CorruptUploads { get; set; }

    public string? Email { get; set; } = "owner@example.com";

    public int Calls { get; private set; }

    public IReadOnlyList<Node> Items
    {
        get
        {
            lock (_gate)
            {
                return _nodes.Values.ToList();
            }
        }
    }

    /// <summary>A live item under <paramref name="parentId"/> by name.</summary>
    public Node? Child(string parentId, string name) => Items.FirstOrDefault(n => n.Parent == parentId && n.Name == name && !n.Trashed);

    /// <summary>The live item at a path of names from the top of My Drive, such as "GameSync/games".</summary>
    public Node? At(string path)
    {
        var parent = "root";
        Node? node = null;
        foreach (var name in path.Split('/'))
        {
            node = Items.Where(n => n.Parent == parent && !n.Trashed && (n.Name == name || n.Name.StartsWith(name + " ", StringComparison.Ordinal)))
                .OrderBy(n => n.CreatedUtc)
                .FirstOrDefault();
            if (node is null)
            {
                return null;
            }

            parent = node.Id;
        }

        return node;
    }

    public Node AddFolder(string parent, string name, Dictionary<string, string>? properties = null)
    {
        lock (_gate)
        {
            return Add(parent, name, isFolder: true, [], properties);
        }
    }

    /// <summary>A file another app put in the folder, like a sync client's desktop.ini: invisible to GameSync.</summary>
    public void AddHiddenFile(string parent, string name)
    {
        lock (_gate)
        {
            _clock = _clock.AddSeconds(1);
            var node = new Node { Id = $"f{++_nextId}", Name = name, Parent = parent, CreatedUtc = _clock, Hidden = true };
            _nodes[node.Id] = node;
        }
    }

    public Task<IReadOnlyList<DriveItem>> ListChildrenAsync(string folderId, CancellationToken ct) =>
        Call("list", () => (IReadOnlyList<DriveItem>)_nodes.Values.Where(n => n.Parent == folderId && !n.Trashed && !n.Hidden).Select(ToItem).ToList());

    public Task<IReadOnlyList<DriveItem>> FindFoldersAsync(string appPropertyKey, string value, CancellationToken ct) =>
        Call("find", () => (IReadOnlyList<DriveItem>)_nodes.Values
            .Where(n => n.IsFolder && !n.Trashed && !n.Hidden && n.AppProperties.GetValueOrDefault(appPropertyKey) == value)
            .Select(ToItem)
            .ToList());

    public Task<DriveItem> CreateFolderAsync(string? parentId, string name, IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct) =>
        Call("createFolder", () => ToItem(Add(parentId ?? "root", name, isFolder: true, [], appProperties)));

    public Task<DriveItem> CreateFileAsync(string parentId, string name, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct)
    {
        var bytes = ReadAll(content);
        return Call("createFile", () =>
        {
            var node = Add(parentId, name, isFolder: false, bytes, appProperties);
            node.ModifiedUtc = modifiedUtc;
            return ToItem(node);
        });
    }

    public Task<DriveItem> UpdateFileAsync(string fileId, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct)
    {
        var bytes = ReadAll(content);
        return Call("updateFile", () =>
        {
            var node = _nodes[fileId];
            node.Content = bytes;
            node.ModifiedUtc = modifiedUtc ?? node.ModifiedUtc;
            foreach (var (key, value) in appProperties ?? new Dictionary<string, string>())
            {
                node.AppProperties[key] = value;
            }

            return ToItem(node);
        });
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken ct)
    {
        var bytes = await Call("download", () =>
        {
            var node = _nodes[fileId];
            return node.Flagged
                ? throw new CloudException(CloudErrorKind.Flagged, $"Google flagged {node.Name} as malware, so GameSync never downloads it.")
                : node.Content;
        });
        destination.SetLength(0);
        await destination.WriteAsync(bytes, ct);
    }

    public Task TrashAsync(string fileId, CancellationToken ct) => Call("trash", () =>
    {
        if (HasHiddenDescendant(fileId))
        {
            throw new CloudException(CloudErrorKind.Other,
                $"Google Drive refused the request: The user may not have granted the app write access to all of the children of file {fileId}.");
        }

        return _nodes[fileId].Trashed = true;
    });

    public Task UpdateMetadataAsync(string itemId, string name, IReadOnlyDictionary<string, string> appProperties, CancellationToken ct) =>
        Call("updateMetadata", () =>
        {
            var node = _nodes[itemId];
            node.Name = name;
            foreach (var (key, value) in appProperties)
            {
                node.AppProperties[key] = value;
            }

            return true;
        });

    private bool HasHiddenDescendant(string folderId) =>
        _nodes.Values.Where(n => n.Parent == folderId && !n.Trashed).Any(n => n.Hidden || (n.IsFolder && HasHiddenDescendant(n.Id)));

    public Task MoveAsync(string itemId, string fromParentId, string toParentId, CancellationToken ct) => Call("move", () =>
    {
        var node = _nodes[itemId];
        if (node.Parent != fromParentId)
        {
            throw new CloudException(CloudErrorKind.Other, "Not in that folder.");
        }

        node.Parent = toParentId;
        return true;
    });

    /// <summary>Server time is set by the test; the storage figures are left out, like an unlimited account.</summary>
    public Task<DriveAbout> AboutAsync(CancellationToken ct) => Call("about", () => new DriveAbout(Email, null, null, null));

    public string FolderLink(string folderId) => $"https://drive.google.com/drive/folders/{folderId}";

    private Node Add(string parent, string name, bool isFolder, byte[] content, IReadOnlyDictionary<string, string>? properties)
    {
        _clock = _clock.AddSeconds(1);
        var node = new Node
        {
            Id = $"f{++_nextId}",
            Name = name,
            Parent = parent,
            IsFolder = isFolder,
            Content = content,
            AppProperties = properties?.ToDictionary() ?? [],
            CreatedUtc = _clock,
        };
        _nodes[node.Id] = node;
        return node;
    }

    private DriveItem ToItem(Node node) => new(
        node.Id,
        node.Name,
        node.IsFolder,
        node.Content.Length,
        node.IsFolder ? null : CorruptUploads ? new string('0', 32) : Convert.ToHexStringLower(MD5.HashData(node.Content)),
        node.CreatedUtc,
        new Dictionary<string, string>(node.AppProperties));

    private Task<T> Call<T>(string name, Func<T> action)
    {
        lock (_gate)
        {
            Calls++;
            if (Fault?.Invoke(name) is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(action());
        }
    }

    private static byte[] ReadAll(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }
}
