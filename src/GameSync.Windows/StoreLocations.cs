using GameSync.Core.Discovery;
using Microsoft.Win32;

namespace GameSync.Windows;

/// <summary>Where this PC's stores keep their records (LIB-01 to LIB-03), read from the registry and never written.</summary>
public static class StoreLocations
{
    public static StoreSources ForThisPc(IReadOnlyList<string> gameFolders) => new()
    {
        SteamRoot = SteamRoot(),
        EpicManifests = EpicManifests(),
        EaFolders = EaFolders(),
        GameFolders = gameFolders,
    };

    /// <summary>Steam's own folder, which holds <c>steamapps</c> and <c>userdata</c>; null without Steam.</summary>
    public static string? SteamRoot()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return string.IsNullOrWhiteSpace(path) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string EpicManifests()
    {
        var data = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher", "AppDataPath", null) as string;
        return Path.Combine(
            string.IsNullOrWhiteSpace(data) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data") : data,
            "Manifests");
    }

    /// <summary>EA's default games folder, plus each EA game's install folder from the registry, wherever it is.</summary>
    public static IReadOnlyList<string> EaFolders()
    {
        var folders = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games") };
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\EA Games", @"SOFTWARE\EA Games" })
        {
            using var games = Registry.LocalMachine.OpenSubKey(path);
            foreach (var name in games?.GetSubKeyNames() ?? [])
            {
                using var game = games!.OpenSubKey(name);
                if (game?.GetValue("Install Dir") is string dir && !string.IsNullOrWhiteSpace(dir))
                {
                    folders.Add(Path.TrimEndingDirectorySeparator(dir));
                }
            }
        }

        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
