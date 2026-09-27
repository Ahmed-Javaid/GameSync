using System.Diagnostics;
using System.Globalization;

namespace GameSync.Core.Discovery;

/// <summary>
/// BAK-06: the build a game has installed now, to notice an update before the new build runs: Steam's <c>buildid</c>,
/// Epic's version, or a loose game's main program version and date. Null when there's no telling.
/// </summary>
public static class BuildReader
{
    public static string? Current(LibraryEntry entry, string? epicManifests)
    {
        if (!entry.Installed || entry.InstallDir is not { Length: > 0 } folder || !Directory.Exists(folder))
        {
            return null;
        }

        switch (entry.Store)
        {
            case StoreKind.Steam when entry.StoreId is { Length: > 0 } id:
                // <library>\steamapps\common\<game>, with the manifest in <library>\steamapps.
                var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)));
                return steamapps is null ? null : Vdf.TryReadRoot(Path.Combine(steamapps, $"appmanifest_{id}.acf"))?["buildid"];
            case StoreKind.Epic when entry.StoreId is { Length: > 0 } app && epicManifests is not null:
                return EpicReader.Read(epicManifests).FirstOrDefault(g => g.StoreId == app)?.Build;
            case StoreKind.Loose:
                if (Fingerprinter.MainExe(folder) is not { } exe)
                {
                    return null;
                }

                var version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
                var date = File.GetLastWriteTimeUtc(exe).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(version) ? date : $"{version.Trim()} of {date}";
            default:
                return null;
        }
    }
}
