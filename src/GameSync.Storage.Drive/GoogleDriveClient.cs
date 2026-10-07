using System.Text.Json;
using GameSync.Core.Storage;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace GameSync.Storage.Drive;

/// <summary>The real Drive, through Google's official .NET client, signed in with <c>drive.file</c> only (R9).</summary>
public sealed class GoogleDriveClient : IDriveClient, IDisposable
{
    private const string Fields = "id,name,mimeType,size,md5Checksum,createdTime,appProperties";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string AboutUrl = "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress),storageQuota(limit,usage)";

    private readonly DriveService _service;

    public GoogleDriveClient(IConfigurableHttpClientInitializer credential)
    {
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GameSync",
            GZipEnabled = true,
        });
    }

    public Task<IReadOnlyList<DriveItem>> ListChildrenAsync(string folderId, CancellationToken ct) =>
        QueryAsync($"'{Escape(folderId)}' in parents and trashed = false", ct);

    public Task<IReadOnlyList<DriveItem>> FindFoldersAsync(string appPropertyKey, string value, CancellationToken ct) =>
        QueryAsync($"appProperties has {{ key='{Escape(appPropertyKey)}' and value='{Escape(value)}' }} and mimeType = '{FolderMime}' and trashed = false", ct);

    public Task<DriveItem> CreateFolderAsync(string? parentId, string name, IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct) =>
        DriveErrors.RetryAsync(async () =>
        {
            var request = _service.Files.Create(new DriveFile
            {
                Name = name,
                MimeType = FolderMime,
                Parents = parentId is null ? null : [parentId],
                AppProperties = appProperties?.ToDictionary(),
            });
            request.Fields = Fields;
            return ToItem(await request.ExecuteAsync(ct));
        }, ct);

    public Task<DriveItem> CreateFileAsync(string parentId, string name, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct) =>
        DriveErrors.RetryAsync(async () =>
        {
            var metadata = new DriveFile
            {
                Name = name,
                Parents = [parentId],
                AppProperties = appProperties?.ToDictionary(),
                ModifiedTimeDateTimeOffset = modifiedUtc is { } m ? new DateTimeOffset(m, TimeSpan.Zero) : null,
            };
            if (content.Length == 0)
            {
                var empty = _service.Files.Create(metadata);
                empty.Fields = Fields;
                return ToItem(await empty.ExecuteAsync(ct));
            }

            content.Position = 0;
            var request = _service.Files.Create(metadata, content, mimeType);
            request.Fields = Fields;

            // KAN-80: a big file goes in parts; the share sent so far counts toward its game's upload as it goes.
            if (TransferMeter.Part is { } part)
            {
                var length = content.Length;
                request.ProgressChanged += sent => part((double)sent.BytesSent / length);
            }

            var progress = await request.UploadAsync(ct);
            if (progress.Status != UploadStatus.Completed)
            {
                throw progress.Exception ?? new IOException($"The upload of {name} didn't finish.");
            }

            return ToItem(request.ResponseBody);
        }, ct);

    public Task<DriveItem> UpdateFileAsync(string fileId, string mimeType, Stream content, DateTime? modifiedUtc,
        IReadOnlyDictionary<string, string>? appProperties, CancellationToken ct) =>
        DriveErrors.RetryAsync(async () =>
        {
            var metadata = new DriveFile
            {
                AppProperties = appProperties?.ToDictionary(),
                ModifiedTimeDateTimeOffset = modifiedUtc is { } m ? new DateTimeOffset(m, TimeSpan.Zero) : null,
            };
            content.Position = 0;
            var request = _service.Files.Update(metadata, fileId, content, mimeType);
            request.Fields = Fields;
            var progress = await request.UploadAsync(ct);
            if (progress.Status != UploadStatus.Completed)
            {
                throw progress.Exception ?? new IOException("The upload didn't finish.");
            }

            return ToItem(request.ResponseBody);
        }, ct);

    public Task DownloadAsync(string fileId, Stream destination, CancellationToken ct) =>
        DriveErrors.RetryAsync(async () =>
        {
            destination.SetLength(0);
            var request = DownloadRequest(fileId);
            var progress = await request.DownloadAsync(destination, ct);
            if (progress.Status != DownloadStatus.Completed)
            {
                throw progress.Exception ?? new IOException("The download didn't finish.");
            }
        }, ct);

    /// <summary>
    /// R4: a file's download, never acknowledging abuse: Google refuses a file it flags as malware unless the asker says it
    /// knows, and GameSync never does, so such a file is never downloaded.
    /// </summary>
    internal FilesResource.GetRequest DownloadRequest(string fileId)
    {
        var request = _service.Files.Get(fileId);
        request.AcknowledgeAbuse = false;
        return request;
    }

    public Task TrashAsync(string fileId, CancellationToken ct) =>
        DriveErrors.RetryAsync(() => _service.Files.Update(new DriveFile { Trashed = true }, fileId).ExecuteAsync(ct), ct);

    public Task MoveAsync(string itemId, string fromParentId, string toParentId, CancellationToken ct) =>
        DriveErrors.RetryAsync(() =>
        {
            var request = _service.Files.Update(new DriveFile(), itemId);
            request.AddParents = toParentId;
            request.RemoveParents = fromParentId;
            return request.ExecuteAsync(ct);
        }, ct);

    public Task UpdateMetadataAsync(string itemId, string name, IReadOnlyDictionary<string, string> appProperties, CancellationToken ct) =>
        DriveErrors.RetryAsync(() => _service.Files.Update(new DriveFile { Name = name, AppProperties = appProperties.ToDictionary() }, itemId).ExecuteAsync(ct), ct);

    /// <summary>A plain request, because the typed call hides the response's Date header, which is Google's clock (SYNC-08).</summary>
    public Task<DriveAbout> AboutAsync(CancellationToken ct) =>
        DriveErrors.RetryAsync(async () =>
        {
            using var response = await _service.HttpClient.GetAsync(AboutUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body.Length > 0 ? body : "{}");
            if (!response.IsSuccessStatusCode)
            {
                var error = json.RootElement.TryGetProperty("error", out var e) ? e : default;
                var reason = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("errors", out var list) && list.GetArrayLength() > 0 &&
                    list[0].TryGetProperty("reason", out var r) ? r.GetString() : null;
                var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() : null;
                throw DriveErrors.FromApi((int)response.StatusCode, reason, message ?? response.ReasonPhrase ?? "unknown error");
            }

            var root = json.RootElement;
            var email = root.TryGetProperty("user", out var user) && user.TryGetProperty("emailAddress", out var address) ? address.GetString() : null;
            long? used = null, limit = null;
            if (root.TryGetProperty("storageQuota", out var quota))
            {
                used = quota.TryGetProperty("usage", out var u) && long.TryParse(u.GetString(), out var usage) ? usage : null;
                limit = quota.TryGetProperty("limit", out var l) && long.TryParse(l.GetString(), out var max) ? max : null;
            }

            return new DriveAbout(email, used, limit, response.Headers.Date?.UtcDateTime);
        }, ct);

    public string FolderLink(string folderId) => $"https://drive.google.com/drive/folders/{Uri.EscapeDataString(folderId)}";

    public void Dispose() => _service.Dispose();

    private Task<IReadOnlyList<DriveItem>> QueryAsync(string query, CancellationToken ct) =>
        DriveErrors.RetryAsync<IReadOnlyList<DriveItem>>(async () =>
        {
            var items = new List<DriveItem>();
            string? page = null;
            do
            {
                var request = _service.Files.List();
                request.Q = query;
                request.Spaces = "drive";
                request.PageSize = 1000;
                request.Fields = $"nextPageToken, files({Fields})";
                request.PageToken = page;
                var result = await request.ExecuteAsync(ct);
                items.AddRange(result.Files.Select(ToItem));
                page = result.NextPageToken;
            }
            while (page is not null);

            return items;
        }, ct);

    private static DriveItem ToItem(DriveFile file) => new(
        file.Id,
        file.Name,
        file.MimeType == FolderMime,
        file.Size ?? 0,
        file.Md5Checksum,
        file.CreatedTimeDateTimeOffset?.UtcDateTime ?? DateTime.MinValue,
        file.AppProperties is { } properties ? new Dictionary<string, string>(properties) : new Dictionary<string, string>());

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
