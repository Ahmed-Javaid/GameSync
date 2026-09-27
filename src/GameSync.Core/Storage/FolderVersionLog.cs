using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>
/// Versions as JSON files under <c>games/&lt;game&gt;/versions/</c>, pins under <c>pins/</c>. Stands in for Google Drive
/// until Milestone 2. Everything read back is untrusted: records that fail to parse are skipped and reported.
/// </summary>
public sealed class FolderVersionLog(string root, Action<StorageProblem>? onProblem = null) : IVersionLog
{
    public async Task<IReadOnlyList<VersionRecord>> ListAsync(GameId game, CancellationToken ct)
    {
        var folder = Folder(game, "versions");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var records = new List<VersionRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var record = await ReadAsync<VersionRecord>(game, file, ct);
            if (record is null)
            {
                continue;
            }

            if (record.Id.Value != Path.GetFileNameWithoutExtension(file) || record.Game != game)
            {
                onProblem?.Invoke(new StorageProblem(game, Path.GetFileName(file), "The version's name doesn't match its contents."));
                continue;
            }

            records.Add(record);
        }

        return records.OrderBy(r => r.CreatedUtc).ThenBy(r => r.Id).ToList();
    }

    public Task AppendAsync(GameId game, VersionRecord version, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(version, Json.Options);
        return AtomicFile.WriteAllBytesAsync(Path.Combine(Folder(game, "versions"), version.Id.Value + ".json"), bytes, overwrite: false, ct);
    }

    public async Task<IReadOnlyList<PinRecord>> ListPinsAsync(GameId game, CancellationToken ct)
    {
        var folder = Folder(game, "pins");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var pins = new List<PinRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (await ReadAsync<PinRecord>(game, file, ct) is { } pin && pin.Version.Value == Path.GetFileNameWithoutExtension(file))
            {
                pins.Add(pin);
            }
        }

        return pins;
    }

    public Task SetPinAsync(GameId game, PinRecord pin, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pin, Json.Options);
        return AtomicFile.WriteAllBytesAsync(Path.Combine(Folder(game, "pins"), pin.Version.Value + ".json"), bytes, overwrite: true, ct);
    }

    public Task RemovePinAsync(GameId game, VersionId version, CancellationToken ct)
    {
        File.Delete(Path.Combine(Folder(game, "pins"), version.Value + ".json"));
        return Task.CompletedTask;
    }

    public Task MarkThinnedAsync(GameId game, VersionId version, CancellationToken ct) =>
        AtomicFile.WriteAllBytesAsync(Path.Combine(Folder(game, "thinned"), version.Value), [], overwrite: true, ct);

    public Task<IReadOnlySet<VersionId>> ListThinnedAsync(GameId game, CancellationToken ct)
    {
        var folder = Folder(game, "thinned");
        IReadOnlySet<VersionId> thinned = !Directory.Exists(folder)
            ? new HashSet<VersionId>()
            : Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Where(name => name is not null && !name.Contains(".tmp-"))
                .Select(name => VersionId.Parse(name!))
                .ToHashSet();
        return Task.FromResult(thinned);
    }

    public async Task<SessionMarker?> GetMarkerAsync(GameId game, CancellationToken ct)
    {
        var path = Path.Combine(root, "games", game.Value, "playing.json");
        return File.Exists(path) ? await ReadAsync<SessionMarker>(game, path, ct) : null;
    }

    public Task SetMarkerAsync(GameId game, SessionMarker? marker, CancellationToken ct)
    {
        var path = Path.Combine(root, "games", game.Value, "playing.json");
        if (marker is null)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        return AtomicFile.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(marker, Json.Options), overwrite: true, ct);
    }

    private string Folder(GameId game, string name) => Path.Combine(root, "games", game.Value, name);

    private async Task<T?> ReadAsync<T>(GameId game, string file, CancellationToken ct) where T : class
    {
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json.Options, ct);
        }
        catch (JsonException e)
        {
            onProblem?.Invoke(new StorageProblem(game, Path.GetFileName(file), $"Unreadable: {e.Message}"));
            return null;
        }
    }
}
