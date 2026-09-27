using System.Text.Json;
using GameSync.Core.Games;

namespace GameSync.Core.Safety;

/// <summary>
/// FOLD-04: the backup folder can't go inside a game's folders, a folder another sync tool manages, Program Files or
/// Windows. Each refusal is one line that says why.
/// </summary>
/// <param name="syncedFolders">Folders other sync tools manage, with the tool's name.</param>
/// <param name="systemFolders">Folders Windows protects.</param>
public sealed class BackupFolderGuard(IReadOnlyList<(string Folder, string Tool)> syncedFolders, IReadOnlyList<string> systemFolders)
{
    public static BackupFolderGuard ForThisPc()
    {
        var synced = new List<(string, string)>();
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } folder)
            {
                synced.Add((folder, "OneDrive"));
            }
        }

        synced.AddRange(DropboxFolders().Select(f => (f, "Dropbox")));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        synced.Add((Path.Combine(home, "My Drive"), "Google Drive"));
        synced.Add((Path.Combine(home, "iCloudDrive"), "iCloud"));
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.VolumeLabel.Equals("Google Drive", StringComparison.OrdinalIgnoreCase))
                {
                    synced.Add((drive.RootDirectory.FullName, "Google Drive"));
                }
            }
            catch (IOException)
            {
                // A drive that vanished while looking is no sync tool.
            }
        }

        var system = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }.Where(f => f.Length > 0).ToList();
        return new BackupFolderGuard(synced, system);
    }

    /// <summary>Why <paramref name="folder"/> can't hold backups, or null when it can.</summary>
    /// <param name="games">Games with their folders resolved for this PC.</param>
    /// <param name="installDirs">Where games are installed on this PC.</param>
    public string? Check(string folder, IEnumerable<GameDefinition> games, IEnumerable<string> installDirs)
    {
        if (!Path.IsPathFullyQualified(folder))
        {
            return "Use a full path, like E:\\Backups\\GameSync.";
        }

        var full = Normalize(folder);
        foreach (var system in systemFolders)
        {
            if (IsInside(full, system))
            {
                return $"{folder} is inside {system}, which Windows protects. Pick a folder of your own.";
            }
        }

        foreach (var (synced, tool) in syncedFolders)
        {
            if (IsInside(full, synced))
            {
                return $"{tool} already syncs this folder, and two sync tools fight over files. Pick a folder outside {tool}.";
            }
        }

        foreach (var install in installDirs)
        {
            if (IsInside(full, install))
            {
                return $"{folder} is inside a game's install folder ({install}), which updates and reinstalls may wipe.";
            }
        }

        foreach (var game in games)
        {
            foreach (var root in game.Roots.Values.Where(r => !RootResolver.IsUnresolved(r)))
            {
                if (IsInside(full, root))
                {
                    return $"{folder} is inside {game.Title}'s save folder ({root}), so backups would back themselves up.";
                }
            }
        }

        return null;
    }

    private static bool IsInside(string folder, string parent)
    {
        if (!Path.IsPathFullyQualified(parent))
        {
            return false;
        }

        // A drive root keeps its separator ("G:\"), so only add one when it isn't there.
        var root = Normalize(parent);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return folder.Equals(root, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static IEnumerable<string> DropboxFolders()
    {
        foreach (var baseFolder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var info = Path.Combine(Environment.GetFolderPath(baseFolder), "Dropbox", "info.json");
            if (!File.Exists(info))
            {
                continue;
            }

            var folders = new List<string>();
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllBytes(info));
                foreach (var account in json.RootElement.EnumerateObject())
                {
                    if (account.Value.ValueKind == JsonValueKind.Object && account.Value.TryGetProperty("path", out var path) && path.GetString() is { Length: > 0 } p)
                    {
                        folders.Add(p);
                    }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
            {
                // Unreadable: nothing to add.
            }

            foreach (var folder in folders)
            {
                yield return folder;
            }
        }
    }
}
