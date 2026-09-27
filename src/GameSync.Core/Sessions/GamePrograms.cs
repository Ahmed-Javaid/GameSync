using System.Text.RegularExpressions;
using GameSync.Core.Model;

namespace GameSync.Core.Sessions;

/// <summary>
/// The programs whose running means a game is being played: those in its install folder, by file name, except
/// launchers, installers, updaters, crash reporters and anti-cheat services, which can stay open after the game closes.
/// A process counts only when it runs from <see cref="InstallDir"/>.
/// </summary>
public sealed partial record GamePrograms(GameId Game, string InstallDir, IReadOnlySet<string> Names)
{
    private const int Depth = 4;

    [GeneratedRegex(@"unins|setup|install|redist|vc_?redist|dxsetup|directx|dotnet|prereq|crash|report|launcher|updater|update|helper|webhelper|cefprocess|easyanticheat|battleye|beservice|eaanticheat|xigncode|gameguard",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotPlaying();

    public static GamePrograms For(GameId game, string installDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(installDir))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = Depth, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var exe in Directory.EnumerateFiles(installDir, "*.exe", options))
            {
                var name = Path.GetFileName(exe);
                if (!NotPlaying().IsMatch(name))
                {
                    names.Add(name);
                }
            }
        }

        return new GamePrograms(game, Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)), names);
    }

    /// <summary>Whether a running program's full path is one of this game's.</summary>
    public bool Owns(string processPath) =>
        Names.Contains(Path.GetFileName(processPath)) &&
        processPath.StartsWith(InstallDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
