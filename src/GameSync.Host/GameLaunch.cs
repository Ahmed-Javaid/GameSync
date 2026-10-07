using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Host;

/// <summary>
/// How a game starts on this PC (PLAY-02, PLAY-11): a store game through its store's own link, so its DRM, its anti-cheat
/// and the store's own launch options apply; a game in its own folder from its program (the one the person picked, or
/// else the biggest in the folder) with the launch options they typed. Both choices are this PC's own, since folders and
/// programs differ between PCs.
/// </summary>
public static class GameLaunch
{
    /// <summary>The longest launch options GameSync keeps; a command line can't take much more.</summary>
    public const int OptionsMax = 1000;

    public static string ProgramKey(GameId game) => $"launch.program.{game}";

    public static string OptionsKey(GameId game) => $"launch.options.{game}";

    /// <summary>The store's own link, when a store starts the game; null for a game in its own folder.</summary>
    public static string? StoreLink(LibraryEntry? entry) => entry switch
    {
        { Store: StoreKind.Steam, StoreId: { Length: > 0 } app } => $"steam://rungameid/{app}",
        { Store: StoreKind.Epic, StoreId: { Length: > 0 } app } => $"com.epicgames.launcher://apps/{Uri.EscapeDataString(app)}?action=launch&silent=true",
        _ => null,
    };

    /// <summary>The program a game in its own folder starts from: the person's pick while it's still there, or else the biggest program in its folder.</summary>
    public static string? Program(string? folder, string? picked) =>
        picked is { Length: > 0 } && File.Exists(picked) ? picked
        : folder is { Length: > 0 } && Directory.Exists(folder) ? Fingerprinter.MainExe(folder) : null;

    /// <summary>The person's launch options for a game in its own folder; empty takes them away.</summary>
    public static void SetOptions(StateStore state, GameId game, string? options)
    {
        var text = (options ?? "").Trim();
        if (text.Length > OptionsMax || text.Any(char.IsControl))
        {
            throw new UsageException($"Launch options are one line of at most {OptionsMax} characters.");
        }

        state.SetSetting(OptionsKey(game), text);
    }

    /// <summary>The program a game in its own folder starts from; empty goes back to the biggest program in its folder.</summary>
    /// <param name="antiCheat">The game ships an anti-cheat: only its anti-cheat's own launcher may start it (R15).</param>
    public static void SetProgram(StateStore state, GameId game, string? program, bool antiCheat = false) =>
        state.SetSetting(ProgramKey(game), CheckProgram(program, antiCheat));

    /// <summary>A program picked to start a game, checked: a program on this PC, and for a game with an anti-cheat its anti-cheat's own launcher (R15).</summary>
    /// <returns>Its full path, or empty for none.</returns>
    public static string CheckProgram(string? program, bool antiCheat)
    {
        var path = (program ?? "").Trim();
        if (path.Length > 0 && (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)))
        {
            throw new UsageException($"{path} isn't a program on this PC.");
        }

        if (path.Length > 0 && antiCheat && !IsAntiCheatLauncher(path))
        {
            throw new UsageException($"{Path.GetFileName(path)} can't start this game: it has an anti-cheat, so it starts only through its anti-cheat's own launcher, such as {EacLauncher}.");
        }

        return path;
    }

    /// <summary>Easy Anti-Cheat's own launcher, which starts a game protected.</summary>
    public const string EacLauncher = "start_protected_game.exe";

    /// <summary>
    /// R15 (design system version 51): what a game with an anti-cheat that no store starts may start from: its anti-cheat's
    /// own launcher in its folder (Easy Anti-Cheat's <see cref="EacLauncher"/>, BattlEye's program ending in _BE), the
    /// shallowest, never the game's program. Null when there's none: then it starts from its launcher, not from GameSync.
    /// </summary>
    public static string? AntiCheatLauncher(string? folder)
    {
        if (folder is not { Length: > 0 } || !Directory.Exists(folder))
        {
            return null;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(folder, "*.exe", options)
            .Where(IsAntiCheatLauncher)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(p => Path.GetFileName(p).Equals(EacLauncher, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>R15: whether <paramref name="program"/> is an anti-cheat's own launcher, the only route GameSync starts such a game by.</summary>
    public static bool IsAntiCheatLauncher(string program) =>
        Path.GetFileName(program).Equals(EacLauncher, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileNameWithoutExtension(program).EndsWith("_BE", StringComparison.OrdinalIgnoreCase);

    public static string? Options(StateStore state, GameId game) => state.GetSetting(OptionsKey(game)) is { Length: > 0 } options ? options : null;

    public static string? PickedProgram(StateStore state, GameId game) => state.GetSetting(ProgramKey(game)) is { Length: > 0 } program ? program : null;
}
