using System.Text.RegularExpressions;
using GameSync.Core.Model;

namespace GameSync.Core.Discovery;

/// <summary>
/// A program GameSync found in a game folder (LIB-30): its name, its own folder, its program as the person would know it,
/// and whether it looks like a game, with why. Nothing joins the library until the person has picked.
/// </summary>
/// <param name="Folder">Its own folder, which the library knows it by.</param>
/// <param name="Program">Its program, relative to the folder looked in ("Hollow Knight\hollow_knight.exe").</param>
/// <param name="Why">Why it looks like a game, or doesn't: "in the save list", "a code project: it has a .git folder".</param>
public sealed record FolderCandidate(string Title, string Folder, string? Program, bool LooksLikeGame, string Why)
{
    /// <summary>The person left it out of the library before: it's offered unticked, saying so.</summary>
    public bool LeftOutBefore { get; init; }

    /// <summary>Ticked to start with: it looks like a game, and the person hasn't left it out before.</summary>
    public bool Ticked => LooksLikeGame && !LeftOutBefore;
}

/// <summary>
/// LIB-30 (a friend's first try, 8 Oct 2026: "FL STUDIO, GitHub repo folder, cursor and other relevant items were added to
/// library"): what a game folder holds, each program judged a game or probably not, so the person picks before anything
/// joins the library. A game is known by the save list, a GOG record, Steam's game files or a game engine; an app by a code
/// project's files, Chromium's, or the words its program uses about itself. Only reads.
/// </summary>
public static partial class FolderLook
{
    /// <summary>Files a code project keeps at its top: a repository, a solution, a package's own description.</summary>
    private static readonly string[] ProjectFiles = ["*.sln", "*.csproj", "package.json", "Cargo.toml", "go.mod", "pyproject.toml", "CMakeLists.txt", "pom.xml", "build.gradle"];

    /// <summary>Words a program uses about itself when it's an app or a tool, not a game: "FL Studio", "Visual Studio Code".</summary>
    [GeneratedRegex(@"\b(studio|editor|ide|code|browser|office|drivers?|setup|installer|updater|uninstall(er)?|compiler|terminal|manager|converter|recorder|launcher|toolkit|sdk|daw|server|utility|utilities|tools?|viewer|explorer|benchmark|antivirus|vpn)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AppWords();

    /// <summary>Each game <paramref name="found"/> in <paramref name="lookedIn"/>, judged, games first, then by name.</summary>
    /// <param name="found">What a scan of the folder found, as games in their own folders.</param>
    /// <param name="leftOut">Folders the person left out of the library before.</param>
    public static IReadOnlyList<FolderCandidate> Look(string lookedIn, IEnumerable<InstalledGame> found, SaveList? list, IReadOnlySet<string>? leftOut = null) =>
        found.Select(game => Judge(game, lookedIn, list) with { LeftOutBefore = leftOut?.Contains(Key(game.InstallDir)) == true })
            .OrderByDescending(c => c.Ticked)
            .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>A folder as the library compares it: full, without a trailing separator, in lower case.</summary>
    public static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToLowerInvariant();

    /// <summary>One program found in a game folder: a game, or probably not, and why.</summary>
    public static FolderCandidate Judge(InstalledGame game, string lookedIn, SaveList? list)
    {
        var dir = game.InstallDir;
        var program = LooseScanner.ProgramOf(dir) is { Length: > 0 } own ? own : Fingerprinter.MainExe(dir);
        var shown = program is null ? null : Relative(lookedIn, program);
        FolderCandidate Game(string why) => new(game.Title, dir, shown, true, why);
        FolderCandidate Not(string why) => new(game.Title, dir, shown, false, why);

        // A repository is somebody's project, whatever it builds.
        if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
        {
            return Not("a code project: it has a .git folder");
        }

        if (ProjectFiles.Select(pattern => Fingerprinter.Find(dir, pattern, 0).FirstOrDefault()).FirstOrDefault(f => f is not null) is { } project)
        {
            return Not($"a code project: it has {Path.GetFileName(project)}");
        }

        var print = Fingerprinter.Read(dir);
        if (list is not null && Listed(list, game, dir, program, print))
        {
            return Game("in the save list");
        }

        if (print.GogId is not null)
        {
            return Game("named in its GOG record");
        }

        if (print.SteamAppId is not null || Fingerprinter.Find(dir, "steam_api*.dll", 3).Any())
        {
            return Game("has Steam's game files");
        }

        if (print.Engine != "unknown")
        {
            return Game($"made with {print.Engine}");
        }

        if (program is not null && Chromium(Path.GetDirectoryName(program)!))
        {
            return Not("an app built on Chromium, like a browser or a code editor");
        }

        if (program is not null && Fingerprinter.VersionInfo(program) is { } info)
        {
            var says = new[] { info.FileDescription, info.ProductName }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && AppWords().IsMatch(s));
            if (says is not null)
            {
                var by = string.IsNullOrWhiteSpace(info.CompanyName) ? "" : $", by {info.CompanyName.Trim()}";
                return Not($"an app: its program calls itself {Fingerprinter.Plain(says)}{by}");
            }
        }

        return Game("a program of its own; nothing says it isn't a game");
    }

    /// <summary>The save list knows it: by its Steam or GOG ID, by its name, its folder's or its program's.</summary>
    private static bool Listed(SaveList list, InstalledGame game, string dir, string? program, Fingerprint print) =>
        (print.SteamAppId is { } app && list.BySteamId(app) is not null) ||
        (print.GogId is { } gog && list.ByGogId(gog) is not null) ||
        list.ByTitle(game.Title) is not null ||
        list.ByInstallDir(Path.GetFileName(Path.TrimEndingDirectorySeparator(dir))).Count > 0 ||
        (program is not null && list.ByTitle(Path.GetFileNameWithoutExtension(program)) is not null);

    /// <summary>An app on Chromium or Electron (Cursor, Discord, a browser): its program's folder carries Chromium's files.</summary>
    private static bool Chromium(string folder) =>
        File.Exists(Path.Combine(folder, "resources", "app.asar")) ||
        File.Exists(Path.Combine(folder, "LICENSES.chromium.html")) ||
        File.Exists(Path.Combine(folder, "chrome_100_percent.pak"));

    private static string Relative(string lookedIn, string program)
    {
        var relative = Path.GetRelativePath(lookedIn, program);
        return relative.StartsWith("..", StringComparison.Ordinal) ? program : relative;
    }
}
