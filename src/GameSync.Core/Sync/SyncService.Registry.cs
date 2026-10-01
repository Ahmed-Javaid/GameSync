using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;

namespace GameSync.Core.Sync;

/// <summary>
/// FIND-10: registry saves. Each key a game keeps saves in is exported to a JSON file in a folder of this PC's own,
/// which joins the game as one more root, so versions, uploads and crash-safe restores treat it like any save file.
/// After a restore, the exports are written back, only under the game's own keys (R7).
/// </summary>
public sealed partial class SyncService
{
    private string RegistryFolder(GameId game) => Path.Combine(SensitivePathGuard.RegistryExports(_dataDir), game.Value);

    private static string RegistryPending(GameId game) => $"registry.pending.{game}";

    private static bool IsRegistryExport(FileEntry file) => file.Path.StartsWith($"{GameDefinition.RegistryRoot}/", StringComparison.Ordinal);

    /// <summary>
    /// Writes each of the game's registry keys to its export file before a scan. The file takes the key's own
    /// last-change time, so a change made during play counts as made during play. An unchanged key leaves its file as it
    /// is, and a key that's gone leaves its last export in place: missing isn't deleted.
    /// </summary>
    private void ExportRegistry(GameDefinition game)
    {
        if (game.Registry.Count == 0 || _options.Registry is not { } registry)
        {
            return;
        }

        // A restore that was cut off before its keys were written back finishes first, or this would undo it.
        ApplyPendingRegistry(game);
        var folder = RegistryFolder(game.Id);
        foreach (var rule in game.Registry)
        {
            if (RegistryGuard.Check(rule.Key, [rule.Key]) is not null || registry.Export(rule.Key) is not { } export)
            {
                continue;
            }

            var path = Path.Combine(folder, RegistryFile.FileName(rule.Key));
            var bytes = RegistryFile.Write(rule.Key, export.Node);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            Directory.CreateDirectory(folder);
            var temp = $"{path}.tmp-{Guid.NewGuid():N}";
            File.WriteAllBytes(temp, bytes);
            File.SetLastWriteTimeUtc(temp, export.ChangedUtc);
            File.Move(temp, path, overwrite: true);
        }
    }

    /// <summary>R7, before anything is written: a restored export must stand for one of this game's own keys, and name that key.</summary>
    private static void CheckRegistryExport(GameDefinition game, FileEntry file, string staged)
    {
        var name = file.Path[(GameDefinition.RegistryRoot.Length + 1)..];
        var rule = game.Registry.FirstOrDefault(r => RegistryFile.FileName(r.Key) == name)
            ?? throw new BlockedException($"'{file.Path}' isn't one of {game.Title}'s registry keys. Nothing was restored.");
        string key;
        try
        {
            key = RegistryFile.Read(File.ReadAllBytes(staged)).Key;
        }
        catch (FormatException e)
        {
            throw new BlockedException($"'{file.Path}' isn't a registry export GameSync can read ({e.Message}). Nothing was restored.");
        }

        if (!RegistryGuard.SameKey(key, rule.Key) || RegistryGuard.Check(key, [rule.Key]) is not null)
        {
            throw new BlockedException($"'{file.Path}' would write to {key}, which isn't {game.Title}'s key {rule.Key}. Nothing was restored.");
        }
    }

    /// <summary>Writes a restore's exports back into the registry; each one was checked before the restore began.</summary>
    private void ApplyPendingRegistry(GameDefinition game)
    {
        if (_options.Registry is not { } registry || _state.GetSetting(RegistryPending(game.Id)) != "1")
        {
            return;
        }

        var folder = RegistryFolder(game.Id);
        foreach (var rule in game.Registry)
        {
            var path = Path.Combine(folder, RegistryFile.FileName(rule.Key));
            if (!File.Exists(path))
            {
                continue;
            }

            var (key, node) = RegistryFile.Read(File.ReadAllBytes(path));
            if (RegistryGuard.SameKey(key, rule.Key) && RegistryGuard.Check(key, [rule.Key]) is null)
            {
                registry.Import(rule.Key, node);
            }
        }

        _state.SetSetting(RegistryPending(game.Id), "0");
        _state.Log(game.Id, "info", "Wrote the restored registry keys back.", EventTags.Restore);
    }
}
