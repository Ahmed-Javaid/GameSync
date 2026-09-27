using System.Text.Json;
using GameSync.Core.Games;
using GameSync.Core.Model;

namespace GameSync.App;

/// <summary>
/// <c>games.json</c> in the data folder: where the cloud is and which games to sync. Until detection arrives in
/// Milestone 3, games are added with <c>gamesync add-game</c> or by editing this file.
/// </summary>
public sealed record AppConfig
{
    /// <summary>A folder standing in for Google Drive until Milestone 2, for example on another drive or a network share.</summary>
    public required string Remote { get; init; }

    public IReadOnlyList<GameDefinition> Games { get; init; } = [];

    public static string PathIn(string dataDir) => Path.Combine(dataDir, "games.json");

    public static AppConfig? Load(string dataDir)
    {
        var path = PathIn(dataDir);
        return File.Exists(path) ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllBytes(path), Json.Options) : null;
    }

    public void Save(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var path = PathIn(dataDir);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, Json.Options));
        File.Move(temp, path, overwrite: true);
    }
}
