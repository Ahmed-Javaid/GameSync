using System.Text.Json;
using System.Text.Json.Serialization;
using GameSync.Core.Games;
using GameSync.Core.Model;

namespace GameSync.Host;

/// <summary>
/// <c>games.json</c> in the data folder: where the cloud is and which games to sync. Save folders can start with
/// placeholders such as <c>&lt;documents&gt;</c>, so the same file works on every PC. Until detection arrives in
/// Milestone 3, games are added with <c>gamesync add-game</c> or by editing this file.
/// </summary>
public sealed record AppConfig
{
    /// <summary>"drive" for Google Drive, or a full folder path standing in for it, such as another drive or a NAS.</summary>
    public required string Remote { get; init; }

    public IReadOnlyList<GameDefinition> Games { get; init; } = [];

    [JsonIgnore]
    public bool UsesDrive => Remote.Equals("drive", StringComparison.OrdinalIgnoreCase);

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
