using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GameSync.Core.Discovery;

/// <summary>
/// What a game's install folder says about it (FIND-02, LIB-09): its engine, the names it goes by, where that engine
/// keeps saves (as portable paths), and whether it ships an anti-cheat.
/// </summary>
public sealed record Fingerprint(
    string Engine,
    string? Detail,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> PredictedFolders,
    IReadOnlyList<string> PredictedRegistry,
    string? MainExe,
    string? AntiCheat);

public static partial class Fingerprinter
{
    [GeneratedRegex(@"unins|setup|redist|crash|report|easyanticheat|battleye|beservice|dxsetup|prereq|helper|updater|webhelper|cefprocess", RegexOptions.IgnoreCase)]
    private static partial Regex ExeNoise();

    [GeneratedRegex(@"[\\/](_?CommonRedist|redist|DirectX|Support|Engine[\\/]Binaries)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex RedistFolder();

    [GeneratedRegex(@"^(.+)-(Win64|WinGDK)-Shipping\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex UnrealShipping();

    /// <summary>Files and folders anti-cheat systems install next to a game.</summary>
    [GeneratedRegex(@"^(EasyAntiCheat.*|start_protected_game\.exe|BattlEye|BEService.*|BEClient.*|GameGuard|EAAntiCheat.*|XIGNCODE.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AntiCheatName();

    public static Fingerprint Read(string installDir)
    {
        var names = new List<string> { Path.GetFileName(Path.TrimEndingDirectorySeparator(installDir)) };
        var exe = MainExe(installDir);
        if (exe is not null)
        {
            names.Add(Path.GetFileNameWithoutExtension(exe));
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                names.Add(info.ProductName ?? "");
                names.Add(info.CompanyName ?? "");
            }
            catch (FileNotFoundException)
            {
                // Gone since the scan started.
            }
        }

        var folders = new List<string>();
        var registry = new List<string>();
        var engine = "unknown";
        string? detail = null;

        var appInfo = Find(installDir, "app.info", 2).FirstOrDefault(f => Path.GetFileName(Path.GetDirectoryName(f))!.EndsWith("_Data", StringComparison.OrdinalIgnoreCase));
        var shipping = Find(installDir, "*-Shipping.exe", 4).FirstOrDefault(f => UnrealShipping().IsMatch(Path.GetFileName(f)));
        var pck = Find(installDir, "*.pck", 1).FirstOrDefault();

        if (appInfo is not null && ReadLines(appInfo, 2) is [var company, var product, ..])
        {
            // Unity keeps saves in LocalLow\<company>\<product>, or PlayerPrefs in the registry, and swaps characters
            // Windows forbids in folder names for '_' ("Stick Fight: The Game" becomes "Stick Fight_ The Game").
            engine = "Unity";
            detail = $"{company} / {product}";
            names.Add(company);
            names.Add(product);
            folders.Add($"<localLow>/{SafeName(company)}/{SafeName(product)}");
            registry.Add($"HKEY_CURRENT_USER/Software/{company}/{product}");
        }
        else if (shipping is not null)
        {
            var project = UnrealShipping().Match(Path.GetFileName(shipping)).Groups[1].Value;
            var projectDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(shipping)))!;
            engine = "Unreal";
            detail = $"project {project}";
            names.Add(project);
            folders.Add($"<localAppData>/{project}/Saved/SaveGames");
            var relative = Path.GetRelativePath(installDir, projectDir).Replace('\\', '/');
            folders.Add(relative == "." ? "<installDir>/Saved/SaveGames" : $"<installDir>/{relative}/Saved/SaveGames");
        }
        else if (pck is not null)
        {
            engine = "Godot";
            names.Add(Path.GetFileNameWithoutExtension(pck));
            folders.Add($"<roaming>/Godot/app_userdata/{Path.GetFileNameWithoutExtension(pck)}");
        }
        else if (File.Exists(Path.Combine(installDir, "data.win")))
        {
            engine = "GameMaker";
            if (exe is not null)
            {
                folders.Add($"<localAppData>/{Path.GetFileNameWithoutExtension(exe)}");
            }
        }
        else if (Directory.Exists(Path.Combine(installDir, "renpy")))
        {
            engine = "Ren'Py";
            folders.Add("<installDir>/game/saves");
        }
        else if (File.Exists(Path.Combine(installDir, "www", "js", "rpg_core.js")))
        {
            engine = "RPG Maker MV";
            folders.Add("<installDir>/www/save");
        }
        else if (File.Exists(Path.Combine(installDir, "js", "rmmz_core.js")))
        {
            engine = "RPG Maker MZ";
            folders.Add("<installDir>/save");
        }
        else if (Find(installDir, "UnityPlayer.dll", 2).Any())
        {
            engine = "Unity";
        }
        else if (Directory.Exists(Path.Combine(installDir, "Engine", "Binaries")))
        {
            engine = "Unreal";
        }

        var antiCheat = Entries(installDir, 3).Select(Path.GetFileName).FirstOrDefault(n => n is not null && AntiCheatName().IsMatch(n));
        return new Fingerprint(
            engine,
            detail,
            names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            folders,
            registry,
            exe,
            antiCheat);
    }

    /// <summary>The biggest program that isn't an installer, crash reporter or anti-cheat: usually the game.</summary>
    public static string? MainExe(string installDir) =>
        Find(installDir, "*.exe", 3)
            .Where(f => !ExeNoise().IsMatch(Path.GetFileName(f)) && !RedistFolder().IsMatch(f))
            .Select(f => (Path: f, Size: SafeLength(f)))
            .OrderByDescending(f => f.Size)
            .Select(f => f.Path)
            .FirstOrDefault();

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static IEnumerable<string> Find(string folder, string pattern, int depth) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern, Options(depth))
            : [];

    private static IEnumerable<string> Entries(string folder, int depth) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder, "*", Options(depth))
            : [];

    private static EnumerationOptions Options(int depth) => new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = depth,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static string[] ReadLines(string file, int count)
    {
        try
        {
            return File.ReadLines(file).Take(count).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}

/// <summary>LIB-05: the game folders you add, such as <c>E:\Games</c>: each folder in them with a game program is a game.</summary>
public static partial class LooseScanner
{
    /// <summary>Folders people keep beside their games that hold programs but aren't games.</summary>
    [GeneratedRegex(@"^(downloads?|tools|utilities|utils|installers?|setups?|drivers|programs|apps|software|backups?|temp|tmp|redist|.*\blauncher)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotAGame();

    /// <param name="skip">Folders that belong to a store (Steam libraries, Epic and EA folders), left to their own readers.</param>
    public static IReadOnlyList<InstalledGame> Scan(IEnumerable<string> folders, IReadOnlyCollection<string> skip)
    {
        var skipped = skip.Select(s => Path.TrimEndingDirectorySeparator(Path.GetFullPath(s))).ToList();
        var games = new List<InstalledGame>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var candidate in Directory.EnumerateDirectories(folder))
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(full);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System) || attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    skipped.Any(s => full.Equals(s, StringComparison.OrdinalIgnoreCase) || full.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (NotAGame().IsMatch(Path.GetFileName(full)) || Fingerprinter.MainExe(full) is null)
                {
                    continue;
                }

                games.Add(new InstalledGame { Store = StoreKind.Loose, Title = Path.GetFileName(full), InstallDir = full });
            }
        }

        return games;
    }
}
