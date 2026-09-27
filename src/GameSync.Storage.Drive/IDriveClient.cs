using GameSync.Core.Storage;

namespace GameSync.Storage.Drive;

/// <summary>A file or folder in Drive, with the fields GameSync asks for.</summary>
public sealed record DriveItem(
    string Id,
    string Name,
    bool IsFolder,
    long Size,
    string? Md5,
    DateTime CreatedUtc,
    IReadOnlyDictionary<string, string> AppProperties);

/// <summary>The signed-in account, its storage, and Google's clock from the response's Date header (SYNC-08).</summary>
public sealed record DriveAbout(string? Email, long? UsedBytes, long? LimitBytes, DateTime? ServerTimeUtc);

/// <summary>
/// The few Drive calls GameSync makes, so the backend can be tested against an in-memory Drive. Failures the user
/// should see (offline, Drive full, rate limited, flagged as malware, signed out) are thrown as <see cref="CloudException"/>.
/// </summary>
public interface IDriveClient
{
    /// <summary>Every child of a folder that isn't in the trash.</summary>
    Task<IReadOnlyList<DriveItem>> ListChildrenAsync(string folderId, CancellationToken ct);

    /// <summary>Folders anywhere in the Drive, not in the trash, carrying this app property.</summary>
    Task<IReadOnlyList<DriveItem>> FindFoldersAsync(string appPropertyKey, string value, CancellationToken ct);

    /// <summary>A new folder; <paramref name="parentId"/> null means the top of My Drive.</summary>
    Task<DriveItem> CreateFolderAsync(string? parentId, string name, IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct);

    /// <summary>A new file. <paramref name="content"/> is seekable, so an upload can start over.</summary>
    Task<DriveItem> CreateFileAsync(string parentId, string name, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct);

    Task<DriveItem> UpdateFileAsync(string fileId, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct);

    /// <summary>Never acknowledges abuse: a file Google flags as malware fails with <see cref="CloudErrorKind.Flagged"/> (R4).</summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct);

    /// <summary>
    /// Can fail for a folder holding something another app put there, which GameSync can't see: Drive won't let an app
    /// trash what it can't see.
    /// </summary>
    Task TrashAsync(string fileId, CancellationToken ct);

    Task MoveAsync(string itemId, string fromParentId, string toParentId, CancellationToken ct);

    /// <summary>Renames an item and sets app properties on it, without touching its contents.</summary>
    Task UpdateMetadataAsync(string itemId, string name, IReadOnlyDictionary<string, string> appProperties, CancellationToken ct);

    Task<DriveAbout> AboutAsync(CancellationToken ct);

    /// <summary>A link that opens the folder in Drive in the browser (CLOUD-08).</summary>
    string FolderLink(string folderId);
}
