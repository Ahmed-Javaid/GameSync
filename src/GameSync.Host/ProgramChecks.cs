using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;

namespace GameSync.Host;

/// <summary>LIB-24 (design system version 57): what a game's program, picked for it on this PC, says it is.</summary>
public enum ProgramFit
{
    /// <summary>Its own record names this game.</summary>
    Fits,

    /// <summary>Nothing in it or its folder names a game, so GameSync goes by the person's pick.</summary>
    Unclear,

    /// <summary>Its own record names another game, or another game is in its folder.</summary>
    OtherGame,
}

/// <summary>A program's check: what it says it is, in a sentence, and the game folder it was made for.</summary>
public sealed record ProgramCheck(ProgramFit Fit, string Sentence, string Folder)
{
    /// <summary>A pick GameSync won't take: another game is in that folder already.</summary>
    public bool Refused { get; init; }
}

/// <summary>
/// LIB-24 (design system version 57, GamePropertiesDialog #files-wrong; a friend pointed Dark Souls III at Dark Souls'
/// program, and Locate the game's message showed once and never again): the program picked for a game in its own folder,
/// by Locate the game… or Installed files' Change…, checked against its own record and the folder, and the answer kept for
/// the game on this PC, so Installed files and its On this PC card say it while the game is in that folder.
/// </summary>
public static class ProgramChecks
{
    public static string Key(GameId game) => $"locate.check.{game}";

    /// <summary>The answer kept for the game, while it's in the folder it was made for; null otherwise.</summary>
    public static ProgramCheck? Read(StateStore state, GameId game, string? folder)
    {
        if (folder is null || state.GetSetting(Key(game)) is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProgramCheck>(json, Json.Options) is { } check && Same(check.Folder, folder) ? check : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Keep(StateStore state, GameId game, ProgramCheck check) => state.SetSetting(Key(game), JsonSerializer.Serialize(check, Json.Options));

    /// <summary>
    /// The check of <paramref name="program"/> for <paramref name="entry"/>, with the game in <paramref name="folder"/>:
    /// refused when another game is in that folder; another game's when its own record names one; fits when it names this
    /// game; unclear when it names none.
    /// </summary>
    internal static ProgramCheck Check(Engine engine, LibraryEntry entry, string program, string folder, SaveList? list)
    {
        var title = entry.DisplayTitle;
        var exe = Path.GetFileName(program);
        if (engine.InstallDirs.FirstOrDefault(d => d.Key != entry.Id && Same(d.Value, folder)) is { Key.Value: not null } taken)
        {
            var other = engine.Library.All().FirstOrDefault(e => e.Id == taken.Key)?.DisplayTitle ?? taken.Key.Value;
            return new ProgramCheck(ProgramFit.OtherGame, $"{other} already has this folder, {folder}, so it can't be {title}'s too. Pick {title}'s own program, in its own folder.", folder)
            {
                Refused = true,
            };
        }

        var (listed, evidence) = Fingerprinter.Identify(program, folder, list);
        if (listed is null)
        {
            return new ProgramCheck(ProgramFit.Unclear, $"Nothing in {exe} or its folder names a game, so GameSync goes by your pick.", folder);
        }

        if (IsThisGame(listed, entry, list))
        {
            return new ProgramCheck(ProgramFit.Fits, $"This is {title}'s program: {evidence} says so.", folder);
        }

        var otherTitle = engine.Library.All().FirstOrDefault(e => e.Id != entry.Id && e.MergedInto is null && Names(e).Contains(SaveList.Normalize(listed.Title)))?.DisplayTitle
            ?? listed.Title;
        return new ProgramCheck(ProgramFit.OtherGame,
            $"This is {otherTitle}'s program ({exe}), not {title}'s: {evidence} says so. Change… and pick {title}'s own program, in its own folder.", folder);
    }

    /// <summary>
    /// Installed files' Change…, before anything changes: the check of a program picked for a game, its folder worked out
    /// as Locate the game… does. Reads the save list, so it runs off the UI thread.
    /// </summary>
    public static async Task<ProgramCheck> CheckPickAsync(string dataDir, GameId game, string program, CancellationToken ct)
    {
        var full = Path.GetFullPath(program);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new UsageException($"{full} isn't a program on this PC. Pick the game's .exe in its folder.");
        }

        var folder = LooseScanner.GameFolderOf(full);
        var list = await Task.Run(() => new SaveListStore(dataDir).Load(), ct);
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null)
            ?? throw new UsageException($"GameSync doesn't know the game '{game}' on this PC.");
        var check = Check(engine, entry, full, folder, list);

        // Locate the game's other refusals: a drive's top, a folder the guard refuses, a whole Windows folder, GameSync's own.
        return check.Refused || LocalGames.ProgramRefusal(folder, engine, game) is not { } refused
            ? check
            : new ProgramCheck(ProgramFit.Unclear, refused, folder) { Refused = true };
    }

    /// <summary>
    /// The answer for a game located by hand before this check existed, made once when its page or Properties is read and
    /// kept; null for any other game, or one whose folder is gone.
    /// </summary>
    internal static ProgramCheck? ForLocated(string dataDir, Engine engine, LibraryEntry? entry, string? folder)
    {
        if (entry is not { InstallDirByHand: { Length: > 0 } } || folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        if (Read(engine.State, entry.Id, folder) is { } kept)
        {
            return kept;
        }

        var program = GameLaunch.PickedProgram(engine.State, entry.Id) ?? GameLaunch.Program(folder, null);
        if (program is null || !File.Exists(program))
        {
            return null;
        }

        var check = Check(engine, entry, program, folder, new SaveListStore(dataDir).Load());
        Keep(engine.State, entry.Id, check);
        return check;
    }

    private static bool IsThisGame(SaveListGame listed, LibraryEntry entry, SaveList? list)
    {
        var listedNames = listed.Aliases.Append(listed.Title).Select(SaveList.Normalize).ToHashSet();
        return Names(entry).Overlaps(listedNames) ||
               (Launcher.SteamIdOf(entry, list) is { } steamId && listed.SteamIds.Contains(steamId));
    }

    private static HashSet<string> Names(LibraryEntry entry) =>
        new[] { entry.SaveListTitle, entry.Title, entry.DisplayTitle }.OfType<string>().Select(SaveList.Normalize).Where(n => n.Length > 0).ToHashSet();

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}
