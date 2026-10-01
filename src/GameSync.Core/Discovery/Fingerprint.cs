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
    string? AntiCheat)
{
    /// <summary>KAN-69: the GOG game ID a GOG install carries in its <c>goggame-&lt;id&gt;.info</c>.</summary>
    public long? GogId { get; init; }

    /// <summary>KAN-69: the Steam app ID in a <c>steam_appid.txt</c> in the folder, as games built on Steamworks carry.</summary>
    public long? SteamAppId { get; init; }
}

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
            if (VersionInfo(exe) is { } info)
            {
                // "Call of Duty(R): Black Ops II" is the list's "Call of Duty: Black Ops II"; Funkin.exe's description is
                // "Friday Night Funkin'" (KAN-65).
                names.Add(Plain(info.ProductName));
                names.Add(Plain(info.FileDescription));
                names.Add(info.CompanyName ?? "");
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
        var gog = Gog(installDir);
        if (gog?.Name is { } gogName)
        {
            names.Insert(0, gogName);
        }

        return new Fingerprint(
            engine,
            detail,
            names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            folders,
            registry,
            exe,
            antiCheat)
        {
            GogId = gog?.Id,
            SteamAppId = SteamAppIdIn(installDir),
        };
    }

    /// <summary>
    /// KAN-69: a GOG install's own record, <c>goggame-&lt;id&gt;.info</c> (JSON with its <c>gameId</c> and <c>name</c>),
    /// in the game's folder or one below it (a game kept in a folder of its own); null when there's none.
    /// </summary>
    public static (long Id, string? Name)? Gog(string folder)
    {
        foreach (var file in Find(folder, "goggame-*.info", 1).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (new FileInfo(file).Length > 1_000_000)
                {
                    continue;
                }

                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;
                var id = root.TryGetProperty("gameId", out var value) ? value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetInt64()
                    : long.TryParse(value.GetString(), out var parsed) ? parsed : 0 : 0;
                if (id > 0)
                {
                    return (id, root.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String ? Plain(name.GetString()) : null);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                // Not a record GameSync can read; the next, or none.
            }
        }

        return null;
    }

    /// <summary>KAN-69: the app ID in the first <c>steam_appid.txt</c> found within three levels, or null.</summary>
    public static long? SteamAppIdIn(string folder)
    {
        foreach (var file in Find(folder, "steam_appid.txt", 3))
        {
            try
            {
                if (new FileInfo(file).Length < 64 && long.TryParse(File.ReadAllText(file).Trim(), out var id) && id > 0)
                {
                    return id;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The next, or none.
            }
        }

        return null;
    }

    /// <summary>The biggest program that isn't an installer, crash reporter or anti-cheat: usually the game.</summary>
    public static string? MainExe(string installDir) =>
        Find(installDir, "*.exe", 3)
            .Where(f => !ExeNoise().IsMatch(Path.GetFileName(f)) && !RedistFolder().IsMatch(f))
            .Select(f => (Path: f, Size: SafeLength(f)))
            .OrderByDescending(f => f.Size)
            .Select(f => f.Path)
            .FirstOrDefault();

    /// <summary>A program is a game's when it isn't an installer, crash reporter, anti-cheat or redistributable.</summary>
    internal static bool IsGameProgram(string path) => !ExeNoise().IsMatch(Path.GetFileName(path)) && !RedistFolder().IsMatch(path);

    /// <summary>A program's own words about itself, or null when it has none or is gone.</summary>
    internal static FileVersionInfo? VersionInfo(string program)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(program);
        }
        catch (FileNotFoundException)
        {
            // Gone since the scan started.
            return null;
        }
    }

    /// <summary>A name without the marks and notes around it: "Call of Duty(R): Black Ops II" is "Call of Duty: Black Ops II".</summary>
    internal static string Plain(string? name) =>
        string.Join(' ', Bracketed().Replace(name ?? "", " ").Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal)
            .Replace("©", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(" :", ":", StringComparison.Ordinal);

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}")]
    private static partial Regex Bracketed();

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

/// <summary>
/// LIB-05, LIB-28: the game folders you add, such as <c>E:\Games</c>: each folder in them with a game's program is a game,
/// and a folder with none of its own holds the game or games in the folders inside it.
/// </summary>
public static partial class LooseScanner
{
    /// <summary>Folders people keep beside their games that hold programs but aren't games.</summary>
    [GeneratedRegex(@"^(downloads?|tools|utilities|utils|installers?|setups?|drivers|programs|apps|software|backups?|temp|tmp|redist|.*\blauncher|.*\bsave ?managers?|mods|trainers)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotAGame();

    /// <summary>Folders that come with a game but aren't one: redistributables, backups, soundtracks, extras, cracks.</summary>
    [GeneratedRegex(@"^(_.*|.*redist.*|directx|support|.*backup.*|.*soundtrack.*|osts?|artwork.*|bonus.*|extras?|manuals?|docs?|uninstall.*|.*crack.*|nodvd|steam_settings|thirdparty|reshade|saveedit|temp-.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Extra();

    /// <summary>Programs people keep with their games that aren't games: save managers, cloud tools, GameSync itself.</summary>
    [GeneratedRegex(@"^(ludusavi|rclone|playnite\..*|toolbox|cefsharp\..*|gamesync(\.tray)?)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex Tool();

    /// <summary>Words in a folder's name about the build, not the game: "funkin-windows-64bit", "t7_full_game".</summary>
    [GeneratedRegex(@"\b(windows|win(32|64)|x(86|64)|x86[ _-]?64|amd64|(32|64)[ _-]?bit|portable|repack|full[ _-]game|setup|installer|v?\d+(\.\d+)+[a-z]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BuildWords();

    /// <summary>A site's name at the end of a folder's name: "Call.of.Duty.Infinite Warfare.RexaGames.com".</summary>
    [GeneratedRegex(@"[\s._-]+[a-z0-9-]+\.(com|net|org|io|ru|to|cc|me|info)$", RegexOptions.IgnoreCase)]
    private static partial Regex Site();

    [GeneratedRegex(@"(?<=[\p{L}\p{N}])[._](?=[\p{L}\p{N}])")]
    private static partial Regex WordJoin();

    [GeneratedRegex(@"(?<=\p{Ll})(?=\p{Lu})|(?<=\p{L})(?=\p{N})|(?<=\p{N})(?=\p{L})")]
    private static partial Regex CamelCase();

    /// <summary>An ID, not a name: "me.funkin.fnf", "Plutonium.Updater.App".</summary>
    [GeneratedRegex(@"^[\w-]+(\.[\w-]+){2,}$")]
    private static partial Regex DottedId();

    [GeneratedRegex(@"\p{L}{3,}")]
    private static partial Regex Word();

    [GeneratedRegex(@"^(game|games|bin|binaries|app|application|program|main|client|data|files|release|shipping|launcher)$", RegexOptions.IgnoreCase)]
    private static partial Regex Generic();

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
                if (Usable(full, skipped) && !NotAGame().IsMatch(Path.GetFileName(full)))
                {
                    games.AddRange(GamesIn(full, skipped));
                }
            }
        }

        return games;
    }

    /// <summary>
    /// The games a folder in a folder of games holds. With a game's program of its own it's one game. With none, the
    /// folders in it that have one decide: one is the game, kept in its folder of its own but named by the folder inside
    /// or its program (<c>FNF\funkin-windows-64bit\Funkin.exe</c> is Friday Night Funkin', <c>OG\MAME</c> is MAME); several
    /// are a folder of games, each a game in its own folder (<c>COD\Call of Duty Black Ops II</c>, <c>COD\t7_full_game</c>).
    /// Failing both, a game's program anywhere in its first few levels still makes it one game, as before.
    /// </summary>
    private static IEnumerable<InstalledGame> GamesIn(string folder, List<string> skipped)
    {
        if (ProgramOf(folder) is { } own)
        {
            return own.Length == 0 ? [] : [Game(folder, NameOf(folder, own))];
        }

        var inside = Subfolders(folder)
            .Where(sub => Usable(sub, skipped) && !NotAGame().IsMatch(Path.GetFileName(sub)) && !Extra().IsMatch(Path.GetFileName(sub)))
            .Select(sub => (Folder: sub, Program: ProgramOf(sub)))
            .Where(sub => sub.Program is { Length: > 0 })
            .ToList();
        return inside switch
        {
            [var only] => [Game(folder, NameOf(only.Folder, only.Program!, outer: folder))],
            [_, _, ..] => inside.Select(sub => Game(sub.Folder, NameOf(sub.Folder, sub.Program!))).ToList(),
            _ => Fingerprinter.MainExe(folder) is { } deep && !Tool().IsMatch(Path.GetFileName(deep)) ? [Game(folder, NameOf(folder, deep))] : [],
        };
    }

    // A GOG install names itself in its record (KAN-69); otherwise the name comes from the folder or its program.
    private static InstalledGame Game(string folder, string title) =>
        new() { Store = StoreKind.Loose, Title = Fingerprinter.Gog(folder)?.Name is { Length: > 0 } gog ? gog : title, InstallDir = folder };

    /// <summary>
    /// The game's program in a folder: the biggest at its top, or in the folders engines keep programs in (<c>bin\x64</c>,
    /// <c>Binaries\Win64</c>, an Unreal game's <c>&lt;project&gt;\Binaries\Win64</c> beside its <c>Engine</c> folder). Empty
    /// when every program there is a tool, such as a save manager; null when there's no program.
    /// </summary>
    internal static string? ProgramOf(string folder)
    {
        var programs = Programs(folder);
        if (programs.Count == 0)
        {
            programs = Subfolders(folder).Where(sub => ProgramFolder().IsMatch(Path.GetFileName(sub)))
                .SelectMany(sub => Programs(sub).Concat(Subfolders(sub).Where(inner => ProgramFolder().IsMatch(Path.GetFileName(inner))).SelectMany(Programs)))
                .ToList();
        }

        if (programs.Count == 0 && Directory.Exists(Path.Combine(folder, "Engine")))
        {
            programs = Subfolders(folder).SelectMany(project => Programs(Path.Combine(project, "Binaries", "Win64"))).ToList();
        }

        if (programs.Count == 0)
        {
            return null;
        }

        return programs.Where(p => !Tool().IsMatch(Path.GetFileName(p))).OrderByDescending(SafeLength).FirstOrDefault() ?? "";
    }

    /// <summary>
    /// The name a game in a folder of games goes by: its folder's, when that reads as a name; else its program's own
    /// name or description ("Friday Night Funkin'", "Call of Duty: Advanced Warfare"); else its program's file name
    /// ("BlackOps3" is Black Ops 3); else, for a game in a folder of its own, that folder's; its folder's name as it is
    /// as the last resort.
    /// </summary>
    internal static string NameOf(string folder, string program, string? outer = null)
    {
        if (Readable(Path.GetFileName(folder), fromFolder: true) is { } byFolder)
        {
            return byFolder;
        }

        if (Fingerprinter.VersionInfo(program) is { } info)
        {
            foreach (var own in new[] { info.ProductName, info.FileDescription })
            {
                if (Readable(own, fromFolder: false) is { } byProgram)
                {
                    return byProgram;
                }
            }
        }

        return Readable(CamelCase().Replace(Path.GetFileNameWithoutExtension(program), " "), fromFolder: false)
            ?? (outer is null ? null : Readable(Path.GetFileName(outer), fromFolder: true))
            ?? Path.GetFileName(folder);
    }

    /// <summary>
    /// A name as it reads, without notes about the build or where it came from, or null when what's left isn't a name:
    /// an ID ("me.funkin.fnf"), a code ("t7", "s1x"), a word such as Game, or from a folder a single word in lower case.
    /// </summary>
    internal static string? Readable(string? name, bool fromFolder)
    {
        if (string.IsNullOrWhiteSpace(name) || DottedId().IsMatch(name.Trim()))
        {
            return null;
        }

        var text = Site().Replace(Fingerprinter.Plain(name), "");
        text = WordJoin().Replace(BuildWords().Replace(text, " "), " ");
        text = string.Join(' ', BuildWords().Replace(text, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '_', '.');
        if (!Word().IsMatch(text) || Generic().IsMatch(text) || (fromFolder && !text.Contains(' ', StringComparison.Ordinal) && text == text.ToLowerInvariant()))
        {
            return null;
        }

        return text;
    }

    private static List<string> Programs(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.exe").Where(Fingerprinter.IsGameProgram).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Subfolders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Hidden and system folders, links, and store folders aren't looked in.
    private static bool Usable(string full, List<string> skipped)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return !attributes.HasFlag(FileAttributes.Hidden) && !attributes.HasFlag(FileAttributes.System) && !attributes.HasFlag(FileAttributes.ReparsePoint) &&
            !skipped.Any(s => full.Equals(s, StringComparison.OrdinalIgnoreCase) || full.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Folders engines keep a game's programs in, below the game's own folder.</summary>
    [GeneratedRegex(@"^(binaries|bin|bin32|bin64|bin_x64|win32|win64|wingdk|x86|x64|x86_64|amd64|retail|shipping)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramFolder();

    /// <summary>
    /// LIB-24: the game's own folder, from its program picked in Locate the game…: the program's folder, above the folders
    /// engines keep programs in (<c>bin\x64</c>, <c>Binaries\Win64</c>), and above an Unreal game's project folder, which
    /// sits beside its <c>Engine</c> folder: <c>G:\Black Myth Wukong\b1\Binaries\Win64\b1-Win64-Shipping.exe</c> is in
    /// <c>G:\Black Myth Wukong</c>. It never climbs to a drive's top.
    /// </summary>
    public static string GameFolderOf(string program)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(program))!;
        static bool BelowTop(string path) => Path.GetDirectoryName(path) is { } parent && Path.GetDirectoryName(parent) is not null;
        while (BelowTop(folder) && ProgramFolder().IsMatch(Path.GetFileName(folder)))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        if (BelowTop(folder) && Path.GetDirectoryName(folder) is { } above && Directory.Exists(Path.Combine(above, "Engine")) &&
            !string.Equals(Path.GetFileName(folder), "Engine", StringComparison.OrdinalIgnoreCase))
        {
            folder = above;
        }

        return folder;
    }
}
