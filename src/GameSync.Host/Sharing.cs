using System.IO.Compression;
using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>A version the share window lists (SHARE-02): what it is, when it was saved and where, its size, and whether it's the current one.</summary>
/// <param name="Label">"Current", a named save's name, or why it was kept ("After play · 3 h", "Daily backup").</param>
public sealed record ShareVersionInfo(VersionId Id, string Label, DateTime SavedUtc, string Pc, long Bytes, bool Kept, bool Current);

/// <summary>A game the share window lists: its versions, newest first, and why it can't be shared when it can't (SHARE-07, R16).</summary>
public sealed record ShareGameInfo(GameId Id, string Title, IReadOnlyList<ShareVersionInfo> Versions, string? Blocked)
{
    /// <summary>What ticking the game shares (SHARE-01): its current save, or its newest.</summary>
    public ShareVersionInfo? Latest => Versions.FirstOrDefault(v => v.Current) ?? Versions.FirstOrDefault();
}

/// <summary>How a game in a shared zip meets this PC's library (SHARE-10, SHARE-13).</summary>
public enum ImportMatch
{
    /// <summary>It syncs or is kept here: its saves join its history.</summary>
    Matched,

    /// <summary>GameSync knows it here but doesn't keep its saves yet: importing keeps it, backed up only.</summary>
    NotSyncing,

    /// <summary>
    /// SHARE-13: not in this PC's library: importing adds it, Not installed, with the zip's rules, backed up only, and its
    /// saves wait in its history until the game is found here.
    /// </summary>
    NotInstalled,

    /// <summary>Not in this PC's library, and the zip's rules for it don't fit this PC: it can't be imported, and Warn says why.</summary>
    Unknown,

    /// <summary>R16: its saves stay with the account that made them (it has an anti-cheat, or plays only online): it can't be imported, and Warn says why.</summary>
    Blocked,
}

/// <summary>A game in a shared zip, as Import saves lists it: what's in it, how it meets this PC, and what to say about it.</summary>
/// <param name="Id">Its ID in the zip.</param>
/// <param name="LocalId">The game here it goes to; null when this PC's library doesn't have it.</param>
/// <param name="Warn">SHARE-12: a save made on another account, which may not load; for a game that can't be imported, why.</param>
/// <param name="Programs">Program files in it, which are left out (SHARE-11, R1).</param>
public sealed record ImportGamePreview(GameId Id, GameId? LocalId, string Title, ImportMatch Match, int Versions, long Bytes, string? Warn, int Programs);

/// <summary>A shared zip, read before anything is imported: who packed it and when, and its games.</summary>
public sealed record ImportPreview(string ZipPath, DateTime PackedUtc, string PackedOn, IReadOnlyList<ImportGamePreview> Games);

/// <summary>
/// Sharing saves (SHARE-01 to SHARE-13, R16): the games and versions that can be shared, where zips go (FOLD-09), packing
/// picked versions into one zip, and a shared zip's saves added to its games' histories as pinned versions.
/// </summary>
public static class Sharing
{
    public const string FolderKey = "share.folder";
    public const string AskKey = "share.ask";

    /// <summary>The manifest is small; a bigger one isn't a GameSync share.</summary>
    private const long MaxManifestBytes = 16 * 1024 * 1024;

    /// <summary>
    /// SHARE-01, SHARE-02, SHARE-05, SHARE-07: every game that syncs here, with every version from every PC (read from
    /// this PC's copy of the records, so it's quick and works offline), and why a game with an anti-cheat or that plays
    /// only online can't be shared.
    /// </summary>
    public static async Task<IReadOnlyList<ShareGameInfo>> ListAsync(string dataDir, CancellationToken ct)
    {
        var view = await SaveHistory.ReadVersionsAsync(dataDir, ct);
        using var engine = Engine.Open(dataDir);
        var library = engine.Library.All().Where(e => e.MergedInto is null).ToDictionary(e => e.Id);
        var byGame = view.Versions.ToLookup(v => v.Game);
        return engine.Games
            .Select(game =>
            {
                var current = engine.State.GetState(game.Id).Base?.Id;
                var versions = byGame[game.Id]
                    .OrderByDescending(v => v.SavedUtc)
                    .Select(v => new ShareVersionInfo(v.Id, v.Id == current ? "Current" : v.Named ? v.Why["Named: ".Length..] : v.Why, v.SavedUtc, v.Pc, v.Bytes, v.Kept,
                        v.Id == current))
                    .ToList();
                return new ShareGameInfo(game.Id, game.Title, versions, Blocked(library.GetValueOrDefault(game.Id)));
            })
            .Where(g => g.Versions.Count > 0)
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>R16, SHARE-07: why a game's saves can't go to another account; null when they can.</summary>
    internal static string? Blocked(LibraryEntry? entry) => entry switch
    {
        { HasAntiCheat: true } => "Ships an anti-cheat, so its saves can't be shared.",
        { ProbablyOnlineOnly: true } => "Plays online, so its saves can't be shared.",
        _ => null,
    };

    /// <summary>
    /// R16 (design system version 51): why someone else's saves never come into a game, from a shared zip or restored
    /// later: its saves stay with the account that made them. Null when they can.
    /// </summary>
    public static string? StaysWithAccount(LibraryEntry? entry) => entry switch
    {
        { HasAntiCheat: true } => "Has an anti-cheat, so its saves stay with the account that made them.",
        { ProbablyOnlineOnly: true } => "Plays only online, so its saves stay with the account that made them.",
        _ => null,
    };

    /// <summary>FOLD-09: where shared zips go: the folder chosen in Settings, or Downloads.</summary>
    public static string Folder(string dataDir)
    {
        using var state = new StateStore(dataDir);
        return state.GetSetting(FolderKey) is { Length: > 0 } folder ? folder : KnownFolders.Downloads() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>FOLD-09: ask where to save each zip, instead of using the folder.</summary>
    public static bool AsksEachTime(string dataDir)
    {
        using var state = new StateStore(dataDir);
        return state.GetSetting(AskKey) == "1";
    }

    public static string SetFolder(string dataDir, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(full))
        {
            throw new UsageException($"{full} isn't there on this PC.");
        }

        using var state = new StateStore(dataDir);
        state.SetSetting(FolderKey, full);
        return $"Shared zips go to {full} from now on.";
    }

    public static void SetAsk(string dataDir, bool ask)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting(AskKey, ask ? "1" : "0");
    }

    /// <summary>A zip's name for today in <paramref name="folder"/>, one that isn't taken: GameSync-saves-2026-10-01.zip, then (2) and on.</summary>
    public static string ZipPathIn(string folder, DateTime nowLocal)
    {
        var name = $"GameSync-saves-{nowLocal:yyyy-MM-dd}";
        var path = Path.Combine(folder, name + ".zip");
        for (var n = 2; File.Exists(path) || File.Exists(path + ".part"); n++)
        {
            path = Path.Combine(folder, $"{name} ({n}).zip");
        }

        return path;
    }

    /// <summary>
    /// SHARE-01 to SHARE-09: packs the versions picked into a zip, under the engine lock so nothing thins or moves the
    /// backups meanwhile. A game that can't be shared (R16) is refused before anything is written.
    /// </summary>
    public static async Task<SharePack> PackAsync(string dataDir, IReadOnlyList<SharePick> picks, string zipPath, IProgress<(long Done, long Total)>? progress,
        IAgentOutput output, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var library = engine.Library.All().Where(e => e.MergedInto is null).ToDictionary(e => e.Id);
        foreach (var pick in picks)
        {
            if (Blocked(library.GetValueOrDefault(pick.Game)) is { } why)
            {
                throw new UsageException($"{library[pick.Game].DisplayTitle}: {why}");
            }
        }

        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = new RunningGames(engine).IsRunning }, ct, recover: false);
        var pack = await service.PackAsync(picks, zipPath, Cli.AppVersion, FirstRun.Friendly, progress, ct);
        output.Say($"Shared {Count(pack.Versions, "save")} of {Count(pack.Games, "game")} in {pack.ZipPath}.");
        return pack;
    }

    /// <summary>
    /// SHARE-10, SHARE-12, SHARE-13: what a shared zip holds and how each game meets this PC, read before anything is
    /// added. Throws, for the page to say, when it isn't a GameSync share or a newer GameSync made it.
    /// </summary>
    public static ImportPreview Preview(string dataDir, string zipPath)
    {
        var (manifest, programs) = ReadManifest(zipPath);
        using var engine = Engine.Open(dataDir);
        var syncing = engine.Games.Select(g => g.Id).ToHashSet();
        var library = engine.Library.All().Where(e => e.MergedInto is null).ToList();
        var accounts = Cli.Accounts(engine.State);
        var games = manifest.Games.Select(game =>
        {
            var local = syncing.Contains(game.Id) ? game.Id
                : library.FirstOrDefault(e => e.Id == game.Id)?.Id
                ?? library.FirstOrDefault(e => Library.Names(e).Contains(SaveList.Normalize(game.Title)))?.Id;
            var refused = local is null ? Refusal(game, engine.Here) : null;
            var staysWith = local is { } known ? StaysWithAccount(library.FirstOrDefault(e => e.Id == known)) : null;
            var match = staysWith is not null ? ImportMatch.Blocked
                : local is { } here ? (syncing.Contains(here) ? ImportMatch.Matched : ImportMatch.NotSyncing)
                : refused is null ? ImportMatch.NotInstalled : ImportMatch.Unknown;
            refused ??= staysWith;
            return new ImportGamePreview(game.Id, local, game.Title, match, game.Versions.Count, game.Versions.Sum(v => FileSet.TotalSize(v.Files)),
                refused ?? AccountWarning(game, accounts), programs.GetValueOrDefault(game.Folder));
        }).ToList();
        return new ImportPreview(zipPath, manifest.PackedUtc, manifest.PackedOn, games);
    }

    /// <summary>
    /// SHARE-10, SHARE-11, SHARE-13: adds the chosen games' saves from a shared zip to their histories, as pinned versions,
    /// never current ones. A game GameSync knows but doesn't keep yet is kept first, backed up only, with the rules its saves
    /// were taken with (R8); one this PC doesn't have joins the library that way, Not installed. Returns what the app says
    /// once it's done.
    /// </summary>
    /// <param name="games">The games to import, by their IDs in the zip.</param>
    public static async Task<string> ImportAsync(string dataDir, string zipPath, IReadOnlyList<GameId> games, IAgentOutput output, CancellationToken ct)
    {
        var preview = Preview(dataDir, zipPath);
        var (manifest, _) = ReadManifest(zipPath);
        var chosen = preview.Games.Where(g => games.Contains(g.Id) && g.Match is not (ImportMatch.Unknown or ImportMatch.Blocked)).ToList();
        if (chosen.Count == 0)
        {
            throw new UsageException("None of the games picked can be imported on this PC.");
        }

        foreach (var game in chosen.Where(g => g.Match == ImportMatch.NotSyncing))
        {
            await KeepForImportAsync(dataDir, game.LocalId!.Value, manifest.Games.First(g => g.Id == game.Id).Rules, output, ct);
        }

        for (var i = 0; i < chosen.Count; i++)
        {
            if (chosen[i].Match == ImportMatch.NotInstalled)
            {
                var shared = manifest.Games.First(g => g.Id == chosen[i].Id);
                chosen[i] = chosen[i] with { LocalId = await AddForImportAsync(dataDir, shared, output, ct) };
            }
        }

        var temp = Path.Combine(Path.GetTempPath(), "GameSync", $"import-{Guid.NewGuid():N}");
        var notes = new List<string>();
        var (added, gamesAdded) = (0, 0);
        try
        {
            var unpacked = Unpack(zipPath, manifest, chosen.Select(g => g.Id).ToHashSet(), temp, notes);
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
            using var engine = Engine.Open(dataDir);
            var (service, _) = await engine.OpenServiceAsync(AppActions.ForAction(new RunningGames(engine).IsRunning), ct, recover: false);
            foreach (var game in chosen)
            {
                var versions = unpacked.Where(u => u.Game == game.Id).Select(u => (u.Version, u.Folder)).ToList();
                if (versions.Count == 0)
                {
                    continue;
                }

                var result = await service.ImportSharedAsync(game.LocalId!.Value, versions, DateTime.Now, ct);
                notes.AddRange(result.Notes.Select(n => $"{game.Title}: {n}"));
                added += result.Added;
                gamesAdded += result.Added > 0 ? 1 : 0;
                if (result.Added > 0 && game.Match == ImportMatch.NotInstalled)
                {
                    notes.Add($"{game.Title} isn't installed here, so its saves wait in GameSync: once it's installed, restore one from its saves.");
                }
            }
        }
        finally
        {
            Core.Storage.LocalHistory.TryDelete(temp);
        }

        var said = added == 0
            ? "Nothing was added."
            : $"Added {Count(added, "save")} to {Count(gamesAdded, "game")}, pinned in their histories; none of them replaces a save you have. Restore one from a game's saves.";
        output.Say($"Import saves from {Path.GetFileName(zipPath)}: {said}");
        return notes.Count == 0 ? said : $"{said} {string.Join(" ", notes)}";
    }

    /// <summary>The manifest, checked, and how many program files each game's folder holds, which are left out.</summary>
    private static (ShareManifest Manifest, Dictionary<string, int> Programs) ReadManifest(string zipPath)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(zipPath);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new UsageException($"{Path.GetFileName(zipPath)} can't be read as a zip: {e.Message}");
        }

        using (zip)
        {
            var entry = zip.GetEntry(ShareManifest.FileName)
                ?? throw new UsageException($"{Path.GetFileName(zipPath)} isn't a zip GameSync shared: it has no {ShareManifest.FileName}. A save in a zip you made yourself goes in with Import kept saves… on its game's saves.");
            if (entry.Length > MaxManifestBytes)
            {
                throw new UsageException($"{Path.GetFileName(zipPath)}'s {ShareManifest.FileName} is too big to be one GameSync wrote.");
            }

            ShareManifest manifest;
            try
            {
                using var json = entry.Open();
                manifest = JsonSerializer.Deserialize<ShareManifest>(json, Json.Options) ?? throw new JsonException("empty");
            }
            catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidDataException)
            {
                throw new UsageException($"{Path.GetFileName(zipPath)}'s {ShareManifest.FileName} is damaged: {e.Message}");
            }

            if (manifest.Format > ShareManifest.CurrentFormat)
            {
                throw new UsageException($"{Path.GetFileName(zipPath)} was made by a newer GameSync ({manifest.GameSync}). Update GameSync to import it.");
            }

            var programs = manifest.Games.ToDictionary(g => g.Folder, g => g.Versions.Sum(v => v.Files.Count(f => ProgramFileDetector.HasBlockedExtension(f.Path))),
                StringComparer.OrdinalIgnoreCase);
            return (manifest, programs);
        }
    }

    /// <summary>
    /// The chosen games' versions unpacked into <paramref name="temp"/>, one folder per version, each file at its version
    /// path. A path that could leave its folder (R6), a file the manifest doesn't size right, or more than
    /// <see cref="SyncService.MaxShareBytes"/> in all turns that save away; the engine checks the rest.
    /// </summary>
    private static List<(GameId Game, SharedVersion Version, string Folder)> Unpack(string zipPath, ShareManifest manifest, IReadOnlySet<GameId> games, string temp,
        List<string> notes)
    {
        var unpacked = new List<(GameId, SharedVersion, string)>();
        long total = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        var n = 0;
        foreach (var game in manifest.Games.Where(g => games.Contains(g.Id)))
        {
            foreach (var version in game.Versions)
            {
                var folder = Path.Combine(temp, (n++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                string? problem = null;
                foreach (var file in version.Files)
                {
                    try
                    {
                        RestorePathGuard.CheckRelative(file.Path);
                    }
                    catch (UnsafePathException e)
                    {
                        problem = e.Message;
                        break;
                    }

                    var entry = zip.GetEntry($"{game.Folder}/{version.Folder}/{file.Path}");
                    if (entry is null || entry.Length != file.Size)
                    {
                        problem = $"{file.Path} isn't in the zip as it was packed.";
                        break;
                    }

                    total += entry.Length;
                    if (total > SyncService.MaxShareBytes)
                    {
                        throw new UsageException($"{Path.GetFileName(zipPath)} unpacks to more than {Cli.FormatSize(SyncService.MaxShareBytes)}, which is too much for saves.");
                    }

                    var target = Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = entry.Open();
                    using var output = File.Create(target);
                    // No more than the manifest says, whatever the zip claims.
                    var buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        if (written > file.Size)
                        {
                            break;
                        }

                        output.Write(buffer, 0, read);
                    }

                    if (written != file.Size)
                    {
                        problem = $"{file.Path} isn't in the zip as it was packed.";
                        break;
                    }
                }

                if (problem is not null)
                {
                    notes.Add($"{game.Title}: {(version.Label is { } shown ? $"“{shown}”" : version.Pc + "'s save")} wasn't added: {problem}");
                    continue;
                }

                unpacked.Add((game.Id, version, folder));
            }
        }

        return unpacked;
    }

    /// <summary>
    /// SHARE-13: a game GameSync knows here but doesn't keep yet is kept, backed up only, before a shared zip's saves join
    /// its history: with the rules they were taken with, so they map onto this PC's folders (R8), or with what the scan
    /// found when the zip has none.
    /// </summary>
    private static async Task KeepForImportAsync(string dataDir, GameId game, PortableRules? rules, IAgentOutput output, CancellationToken ct)
    {
        if (rules is null)
        {
            if (!await AppActions.KeepAsync(dataDir, game, output, ct))
            {
                throw new UsageException("Its saves can't be kept on this PC yet; the log says why.");
            }

            return;
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().First(e => e.Id == game && e.MergedInto is null);
        var adopted = Library.Adopt(entry, rules, GameDefaults.Load(engine.State));
        adopted = adopted with { Confirmed = adopted.Confirmed! with { Mode = GameMode.BackupOnly } };
        if (Cli.Problems(adopted.Confirmed!, engine.Here.Resolver.Resolve(adopted.Confirmed!), engine.Here) is [var problem, ..])
        {
            throw new UsageException($"{entry.DisplayTitle} can't be kept on this PC with the shared save's rules: {problem}");
        }

        engine.Library.SaveAll([adopted]);
        output.Say($"GameSync keeps {entry.DisplayTitle}'s saves from now on, backed up on this PC and in the cloud, for the shared saves to join its history.");
    }

    /// <summary>
    /// SHARE-13: a game this PC doesn't have joins its library from the zip, Not installed, kept with the rules its saves
    /// were taken with, backed up only: its saves wait in its history until the game is found here.
    /// </summary>
    private static async Task<GameId> AddForImportAsync(string dataDir, SharedGame game, IAgentOutput output, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        if (Refusal(game, engine.Here) is { } refused)
        {
            throw new UsageException($"{game.Title} can't be imported on this PC: {refused}");
        }

        var entry = Library.FromShared(game.Id, game.Title, game.Rules!, engine.Library.All().Select(e => e.Id), GameDefaults.Load(engine.State), DateTime.UtcNow);
        engine.Library.SaveAll([entry]);
        output.Say($"{game.Title} isn't installed on this PC: GameSync keeps its shared saves, backed up only, until the game is found here.");
        return entry.Id;
    }

    /// <summary>SHARE-13: why a game this PC doesn't have can't be imported: the zip doesn't say where its saves go, or its rules fail this PC's checks (R5, R8).</summary>
    private static string? Refusal(SharedGame game, ThisPc here)
    {
        if (game.Rules is not { } rules)
        {
            return "The zip doesn't say where its saves go.";
        }

        var portable = rules.ToDefinition(game.Id) with { Title = game.Title };
        return Cli.Problems(portable, here.Resolver.Resolve(portable), here).FirstOrDefault();
    }

    /// <summary>SHARE-12: a save whose folders used an account ID that isn't this PC's may not load, as FromSoftware's check the account.</summary>
    private static string? AccountWarning(SharedGame game, IReadOnlyDictionary<string, string> mine)
    {
        foreach (var version in game.Versions)
        {
            foreach (var (key, value) in version.Accounts ?? new Dictionary<string, string>())
            {
                if (!mine.TryGetValue(key, out var own) || !own.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    var store = key.StartsWith("steam", StringComparison.OrdinalIgnoreCase) ? "Steam" : key.StartsWith("epic", StringComparison.OrdinalIgnoreCase) ? "Epic" : "store";
                    return $"Made on another {store} account. Some games check the account, so it may not load.";
                }
            }
        }

        return null;
    }

    private static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";
}
