using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Discovery;

/// <summary>
/// Keeps the save list on this PC as a small index (<c>savelist.json.gz</c>), refreshed from the Ludusavi manifest on
/// GitHub (only when it changed) or from a local Ludusavi install. Whatever it reads is only data: every path it gives
/// still passes the safety checks (R5, R8) and your confirmation.
/// </summary>
public sealed class SaveListStore(string dataDir, HttpClient? http = null)
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

    /// <summary>A refresh this old or older is due.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private string IndexPath => Path.Combine(dataDir, "savelist.json.gz");

    private string ETagPath => Path.Combine(dataDir, "savelist.etag");

    /// <summary>The list saved on this PC, or null before the first refresh.</summary>
    public SaveList? Load()
    {
        if (!File.Exists(IndexPath))
        {
            return null;
        }

        try
        {
            using var file = File.OpenRead(IndexPath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<SaveList>(gzip, Json.Options);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The list a scan uses: the saved one, refreshed first when it's a week old or <paramref name="refresh"/> asks.
    /// When the download fails, the saved list stays in use, or, before the first download, Ludusavi's own copy at
    /// <paramref name="fallbackManifest"/>. Null only when there's none of those; the other layers still work then.
    /// </summary>
    public async Task<(SaveList? List, string? Note)> GetAsync(bool refresh, string? fallbackManifest, CancellationToken ct)
    {
        var saved = Load();
        if (saved is not null && !refresh && DateTime.UtcNow - saved.UpdatedUtc < MaxAge)
        {
            return (saved, null);
        }

        try
        {
            return (await UpdateFromWebAsync(ct), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or YamlDotNet.Core.YamlException)
        {
            var why = e is TaskCanceledException ? "it timed out" : e.Message;
            if (saved is not null)
            {
                return (saved, $"Couldn't refresh the save list ({why}), so the copy from {saved.UpdatedUtc.ToLocalTime():yyyy-MM-dd} is used.");
            }

            if (fallbackManifest is not null && File.Exists(fallbackManifest))
            {
                return (UpdateFromFile(fallbackManifest), $"Couldn't download the save list ({why}), so Ludusavi's copy is used.");
            }

            return (null, $"Couldn't download the save list ({why}). Engine rules and the name search still ran; run 'gamesync savelist update' later.");
        }
    }

    /// <summary>Reads a manifest file, such as Ludusavi's own <c>%APPDATA%\ludusavi\manifest.yaml</c>.</summary>
    public SaveList UpdateFromFile(string manifestPath)
    {
        using var reader = new StreamReader(manifestPath);
        var list = SaveListParser.Parse(reader, manifestPath, File.GetLastWriteTimeUtc(manifestPath));
        Save(list);
        return list;
    }

    /// <summary>Downloads the manifest when it changed since the last download; otherwise keeps the saved list.</summary>
    public async Task<SaveList> UpdateFromWebAsync(CancellationToken ct)
    {
        var client = http ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(2) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
            var saved = Load();
            if (saved is not null && File.Exists(ETagPath) && EntityTagHeaderValue.TryParse(File.ReadAllText(ETagPath), out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotModified && saved is not null)
            {
                var fresh = new SaveList(saved.Games, saved.Source, DateTime.UtcNow);
                Save(fresh);
                return fresh;
            }

            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(body);
            var list = SaveListParser.Parse(reader, ManifestUrl, DateTime.UtcNow);
            Save(list);
            if (response.Headers.ETag is { } etag)
            {
                File.WriteAllText(ETagPath, etag.ToString());
            }

            return list;
        }
        finally
        {
            if (http is null)
            {
                client.Dispose();
            }
        }
    }

    private void Save(SaveList list)
    {
        Directory.CreateDirectory(dataDir);
        var temp = $"{IndexPath}.tmp-{Guid.NewGuid():N}";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, list, Json.Options);
        }

        File.Move(temp, IndexPath, overwrite: true);
    }
}
