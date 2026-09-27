using System.Text.Json.Serialization;
using GameSync.Core.Model;

namespace GameSync.Core.Games;

[JsonConverter(typeof(JsonStringEnumConverter<GameMode>))]
public enum GameMode
{
    /// <summary>Two-way sync between PCs.</summary>
    Sync,

    /// <summary>A store's cloud already syncs it: GameSync keeps history but never downloads by itself.</summary>
    BackupOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConflictPolicy>))]
public enum ConflictPolicy
{
    NewestWins,
    AlwaysAsk,
    ThisPcWins,
}

/// <summary>A set of save files: a root folder (by key, so each PC can put it somewhere else) and patterns below it.</summary>
public sealed record SaveRule
{
    public required string Root { get; init; }

    public string Include { get; init; } = "**";

    public IReadOnlyList<string> Exclude { get; init; } = [];

    public SaveCategory Category { get; init; } = SaveCategory.Save;

    /// <summary>Skip logs, crash dumps, shader and web caches (FIND-11).</summary>
    public bool UseDefaultExcludes { get; init; } = true;
}

public sealed record GameDefinition
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    public GameMode Mode { get; init; } = GameMode.Sync;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.NewestWins;

    /// <summary>Root key to this PC's folder. The key is what versions store, which keeps paths portable between PCs.</summary>
    public required IReadOnlyDictionary<string, string> Roots { get; init; }

    public required IReadOnlyList<SaveRule> Rules { get; init; }

    /// <summary>Sync <see cref="SaveCategory.Config"/> files between PCs instead of backing them up per PC.</summary>
    public bool SyncConfig { get; init; }

    public bool IncludeScreenshots { get; init; }
}
