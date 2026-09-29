using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;

namespace GameSync.Host;

/// <summary>
/// A folder or file picked in Add a place (FOLD-01), as the dialog shows it before anything is added: how every PC reads
/// it, what's there, and whether it can be a place of the game's saves.
/// </summary>
public sealed record NewPlaceLook
{
    /// <summary>The folder or file as picked, in full.</summary>
    public required string Path { get; init; }

    /// <summary>One save file, taken by itself, rather than a folder and everything in it.</summary>
    public bool IsFile { get; init; }

    /// <summary>The folder that becomes the place: the one picked, or the picked file's folder.</summary>
    public required string Folder { get; init; }

    /// <summary>How every PC reads that folder: <c>&lt;localLow&gt;/Team Cherry/Hollow Knight</c>, or a full path under none of Windows' own folders.</summary>
    public required string Portable { get; init; }

    /// <summary>It's under one of Windows' own folders (or the game's install folder), so each PC finds it in its own.</summary>
    public bool Travels => Portable.StartsWith('<');

    public int Files { get; init; }

    public long Bytes { get; init; }

    public DateTime? NewestUtc { get; init; }

    /// <summary>Program files in it, which are never taken (R1).</summary>
    public int Programs { get; init; }

    /// <summary>More files than were looked at; the counts are of the first ones.</summary>
    public bool More { get; init; }

    /// <summary>Why it can't be added, and what to pick instead.</summary>
    public string? Refused { get; init; }

    /// <summary>It can be added, but: another game keeps saves there too.</summary>
    public string? Warning { get; init; }
}

/// <summary>One of a game's places: its key in versions, and its folder as every PC reads it.</summary>
public sealed record SaveRoot(string Key, string Portable);

/// <summary>Looks at places picked for a game's saves by hand (FOLD-01).</summary>
public static class SavePlaces
{
    /// <summary>A game's places, for which one a folder of kept copies copies (BAK-19).</summary>
    public static IReadOnlyList<SaveRoot> Roots(string dataDir, GameId game)
    {
        using var engine = Engine.Open(dataDir);
        return engine.Games.FirstOrDefault(g => g.Id == game) is { } definition
            ? (definition.PortableRoots ?? definition.Roots).Where(r => r.Key != GameDefinition.RegistryRoot).Select(r => new SaveRoot(r.Key, r.Value)).ToList()
            : [];
    }

    /// <summary>How many files a look counts, so a folder with far too much in it answers quickly.</summary>
    public const int FilesLooked = 5000;

    /// <summary>What a folder or file picked for a game holds, and whether it can be one of the game's places.</summary>
    public static NewPlaceLook Look(string dataDir, GameId game, string path)
    {
        using var engine = Engine.Open(dataDir);
        return Look(engine, game, path);
    }

    internal static NewPlaceLook Look(Engine engine, GameId game, string path) =>
        Look(path, engine.Games.FirstOrDefault(g => g.Id == game), engine.Games.Where(g => g.Id != game).ToList(), engine.Here.Folders,
            engine.InstallDirs.GetValueOrDefault(game), engine.Here.Guard, Cli.HistoryFolder(engine.State, engine.DataDir));

    /// <param name="game">The game, with this PC's folders filled in; null for one this PC doesn't sync.</param>
    /// <param name="others">The other games this PC syncs, whose places may be the same folder.</param>
    /// <param name="folders">This PC's own folders for the placeholders: <c>&lt;documents&gt;</c> and the rest.</param>
    /// <param name="installDir">The game's install folder on this PC, which becomes <c>&lt;installDir&gt;</c>.</param>
    /// <param name="historyFolder">This PC's backup folder, which can't be a game's place.</param>
    internal static NewPlaceLook Look(string path, GameDefinition? game, IReadOnlyList<GameDefinition> others, IReadOnlyDictionary<string, string> folders,
        string? installDir, SensitivePathGuard guard, string historyFolder)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var isFile = File.Exists(full);
        var folder = isFile ? System.IO.Path.GetDirectoryName(full)! : full;
        var known = new Dictionary<string, string>(folders, StringComparer.OrdinalIgnoreCase);
        if (installDir is { Length: > 0 })
        {
            known[RootResolver.InstallDir] = installDir;
        }

        var look = new NewPlaceLook { Path = full, IsFile = isFile, Folder = folder, Portable = RootResolver.ToPortable(folder, known) };
        if (!isFile && !Directory.Exists(full))
        {
            return look with { Refused = $"{full} isn't there on this PC." };
        }

        if (Refusal(folder, look.Portable, guard, historyFolder) is { } refused)
        {
            return look with { Refused = refused };
        }

        var mine = game?.Roots.Values.Where(r => !RootResolver.IsUnresolved(r)).ToList() ?? [];
        if (!isFile && mine.Any(r => Same(r, folder)))
        {
            return look with { Refused = $"It's already one of {game!.Title}'s places. Choose files… in its Properties picks what's backed up there." };
        }

        var (files, bytes, newest, programs, more) = Count(isFile ? [full] : Enumerate(folder));
        if (isFile && programs > 0)
        {
            return look with { Refused = $"{System.IO.Path.GetFileName(full)} is a program file, and only save data moves. Pick a save file." };
        }

        var shared = others.FirstOrDefault(o => o.Roots.Values.Any(r => !RootResolver.IsUnresolved(r) && (Same(r, folder) || Inside(r, folder) || Inside(folder, r))));
        return look with
        {
            Files = files,
            Bytes = bytes,
            NewestUtc = newest,
            Programs = programs,
            More = more,
            Warning = shared is null ? null : $"{shared.Title} keeps saves here too. A file both games take shows as an error on both.",
        };
    }

    /// <summary>
    /// Why a folder can't be a place of a game's saves: what the safety guard refuses (R5: a drive, Windows, credential
    /// stores, GameSync's own data, or a folder holding one), a whole Windows folder or the game's whole install folder,
    /// which hold far more than saves, and the backup folder.
    /// </summary>
    private static string? Refusal(string folder, string portable, SensitivePathGuard guard, string historyFolder)
    {
        if (guard.CheckRoot(folder) is { } refusal)
        {
            return $"{char.ToUpperInvariant(refusal[0])}{refusal[1..].TrimEnd('.')}. Pick the game's own folder.";
        }

        if (portable.StartsWith('<') && !portable.Contains('/', StringComparison.Ordinal))
        {
            return portable.Equals(RootResolver.InstallDir, StringComparison.OrdinalIgnoreCase)
                ? "That's the game's whole install folder, with all its game files. Pick the folder its saves are in."
                : $"That's your whole {WholeName(portable)} folder, far more than one game's saves. Pick the game's own folder inside it.";
        }

        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath))
        {
            if (programs.Length > 0 && Same(programs, folder))
            {
                return "That's the whole Program Files folder, far more than one game's saves. Pick the game's own folder inside it.";
            }
        }

        if (Same(historyFolder, folder) || Inside(folder, historyFolder))
        {
            return "That's where GameSync keeps its backups, so it can't be a game's place.";
        }

        return Inside(historyFolder, folder) ? "It holds GameSync's backup folder, so it can't be a game's place. Pick the game's own folder." : null;
    }

    private static string WholeName(string placeholder) => placeholder.ToLowerInvariant() switch
    {
        "<documents>" => "Documents",
        "<savedgames>" => "Saved Games",
        "<locallow>" => @"AppData\LocalLow",
        "<roaming>" => @"AppData\Roaming",
        "<localappdata>" => @"AppData\Local",
        "<home>" => "user",
        "<public>" => "Public",
        "<publicdocuments>" => "Public Documents",
        "<programdata>" => "ProgramData",
        "<steamroot>" => "Steam",
        _ => placeholder.Trim('<', '>'),
    };

    /// <summary>A folder's files, the ones this PC may read, up to <see cref="FilesLooked"/> and one more to tell there are more.</summary>
    private static IEnumerable<string> Enumerate(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(folder, "*", options).Take(FilesLooked + 1);
    }

    private static (int Files, long Bytes, DateTime? NewestUtc, int Programs, bool More) Count(IEnumerable<string> paths)
    {
        var (files, bytes, programs) = (0, 0L, 0);
        DateTime? newest = null;
        foreach (var path in paths)
        {
            if (files + programs == FilesLooked)
            {
                return (files, bytes, newest, programs, true);
            }

            try
            {
                // Program files are never taken (R1), so they're counted apart from what would be backed up.
                if (ProgramFileDetector.IsProgramFile(path))
                {
                    programs++;
                    continue;
                }

                var info = new FileInfo(path);
                files++;
                bytes += info.Length;
                newest = newest is { } seen && seen >= info.LastWriteTimeUtc ? seen : info.LastWriteTimeUtc;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A file in use or out of reach is counted by the sync that reads it, not here.
            }
        }

        return (files, bytes, newest, programs, false);
    }

    private static string Norm(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="path"/> is inside <paramref name="folder"/>.</summary>
    private static bool Inside(string path, string folder) =>
        Norm(path).StartsWith(Norm(folder) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
