using System.Text.RegularExpressions;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;

namespace GameSync.Host;

/// <summary>What Add a game or folder adds (LIB-13): a name, a folder, and the program that uses it, if there is one.</summary>
public sealed record OwnGame(string Name, string Folder, string? Program = null);

/// <summary>What adding did: the new game, and what the app says.</summary>
public sealed record OwnGameAdded(GameId Id, string Title, string Sentence);

/// <summary>
/// Games and folders of the person's own (LIB-13): a game GameSync didn't find, or any folder to keep in step between
/// PCs, like a game server's world. It syncs like a game: when the program that uses it closes, or, with none, once the
/// folder has been quiet for 5 minutes. Program files in it are never copied (R1); the folders a game's place can't be
/// are refused, as Add a place refuses them.
/// </summary>
public static partial class OwnGames
{
    public const int NameAtMost = 80;

    /// <summary>A folder's name that says little by itself, such as a server's <c>world</c>: its parent's name goes first.</summary>
    [GeneratedRegex(@"^(worlds?|saves?|save ?games?|saved|data|profiles?|users?|userdata|remote|config|settings|game|files)$", RegexOptions.IgnoreCase)]
    private static partial Regex Plain();

    /// <summary>
    /// A folder picked in Add a game or folder, as the dialog shows it: what's there (program files counted apart), and
    /// why it can't be added, if it can't. Works before GameSync is set up, for first run.
    /// </summary>
    public static NewPlaceLook Look(string dataDir, string folder)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        return Look(engine, folder);
    }

    internal static NewPlaceLook Look(Engine engine, string folder)
    {
        var look = SavePlaces.Look(folder, null, engine.Games, engine.Here.Folders, null, engine.Here.Guard, Cli.HistoryFolder(engine.State, engine.DataDir));
        return look.IsFile && look.Refused is null
            ? look with { Refused = $"{System.IO.Path.GetFileName(look.Path)} is a file. Pick the folder it's in: GameSync keeps a whole folder in step." }
            : look;
    }

    /// <summary>The name a picked folder suggests: its own, or with its parent's when it says little by itself ("Minecraft world").</summary>
    public static string SuggestName(string folder)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        var name = System.IO.Path.GetFileName(full);
        var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(full) ?? "");
        var suggested = name.Length == 0 ? "" : Plain().IsMatch(name) && parent.Length > 0 ? $"{parent} {name}" : name;
        return suggested.Length > NameAtMost ? suggested[..NameAtMost].TrimEnd() : suggested;
    }

    /// <summary>
    /// A program picked as the one that uses it: a <c>.exe</c> here, whose game folder (LIB-24's, above <c>bin\x64</c>
    /// and the like) GameSync can watch; throws <see cref="UsageException"/> saying why not.
    /// </summary>
    public static string ProgramFolder(string dataDir, string program)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        return ProgramFolder(engine, program, null);
    }

    private static string ProgramFolder(Engine engine, string program, GameId? game)
    {
        var full = System.IO.Path.GetFullPath(program);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new UsageException($"{full} isn't a program on this PC. Pick the .exe that uses the folder.");
        }

        var folder = LooseScanner.GameFolderOf(full);
        return LocalGames.ProgramRefusal(folder, engine, game ?? GameId.Parse("new-game")) is { } refused ? throw new UsageException(refused) : folder;
    }

    /// <summary>
    /// Adds a game or folder of the person's own. Once GameSync is set up it syncs from now on (FIND-06's checks first,
    /// as a confirmed game's); in first run it joins Choose games, ticked, and syncs once setup is done. What goes wrong is
    /// thrown, for the dialog to say.
    /// </summary>
    public static async Task<OwnGameAdded> AddAsync(string dataDir, OwnGame game, IAgentOutput output, CancellationToken ct)
    {
        var name = game.Name.Trim();
        if (name.Length == 0)
        {
            throw new UsageException("Give it a name, as the library will show it.");
        }

        if (name.Length > NameAtMost)
        {
            throw new UsageException($"Keep the name to {NameAtMost} characters.");
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.OpenForSetup(dataDir);
        var setUp = File.Exists(AppConfig.PathIn(dataDir));
        var library = engine.Library.All();
        var normal = SaveList.Normalize(name);
        var same = library.Where(e => e.MergedInto is null && e.State != LibraryState.Ignored).Select(e => e.DisplayTitle)
            .Concat(engine.Config.Games.Select(g => g.Title))
            .FirstOrDefault(t => SaveList.Normalize(t) == normal);
        if (same is not null)
        {
            throw new UsageException($"There's already a game called {same} in the library. Give this one another name, or sync that one from its page.");
        }

        var look = Look(engine, game.Folder);
        if (look.Refused is { } refused)
        {
            throw new UsageException(refused);
        }

        var taken = library.Select(e => e.Id).Concat(engine.Config.Games.Select(g => g.Id)).ToList();
        var id = Library.NewId(name, taken);
        var programFolder = game.Program is { Length: > 0 } program ? ProgramFolder(engine, program, id) : null;
        var place = new Proposal(FoundBy.ByHand, look.Portable, "**", SaveCategory.Save, look.Files, look.Bytes, look.NewestUtc);
        var entry = Library.AddOwn(name, look.Folder, place, programFolder, taken, DateTime.UtcNow);
        if (setUp)
        {
            entry = Library.Confirm(entry);
            var portable = entry.Confirmed!;
            if (Cli.Problems(portable, engine.Here.Resolver.Resolve(portable), engine.Here) is [var problem, ..])
            {
                throw new UsageException($"{name} can't sync: {problem}");
            }
        }

        engine.Library.SaveAll([entry]);
        if (game.Program is { Length: > 0 } chosen)
        {
            GameLaunch.SetProgram(engine.State, entry.Id, System.IO.Path.GetFullPath(chosen));
        }

        var when = programFolder is null ? "once its folder has been quiet for 5 minutes" : $"when {System.IO.Path.GetFileName(game.Program)} closes";
        var sentence = setUp
            ? $"{name} syncs from now on, {when}. On your other PC, add its folder under the same name, and the two stay in step."
            : $"{name} is in the list, ticked. It syncs {when}, once you start using GameSync.";
        return new OwnGameAdded(entry.Id, name, sentence);
    }
}
