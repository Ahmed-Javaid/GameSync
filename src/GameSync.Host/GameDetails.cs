using System.Globalization;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Scanning;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// One place a game keeps its saves: the folder as every PC reads it (<c>&lt;roaming&gt;/Cuphead</c>), the folder it is
/// on this PC, a tag for settings, screenshots or the registry, and what's there ("3 files · 0.5 MB · newest 23 Sep 21:09").
/// </summary>
/// <param name="Folder">The folder on this PC; null when this PC can't place it (the game isn't installed here) or it's a registry key.</param>
public sealed record GamePlace(string Portable, string? Folder, string? Tag, string Evidence)
{
    /// <summary>R2 (design system version 51): the program files found in this place, by their full paths; never backed up (R1).</summary>
    public IReadOnlyList<string> Programs { get; init; } = [];
}

/// <summary>A version of a game's save, from any PC, as its history lists it.</summary>
/// <param name="Note">What sets it apart: a named save's name, "Before update to build …", a conflict's other side.</param>
/// <param name="Uploaded">False while it waits on this PC for the cloud.</param>
public sealed record GameVersion(VersionId Id, DateTime SavedUtc, string Pc, string? Note, bool IsCurrent, bool IsPinned, long Bytes, bool Uploaded);

/// <summary>A named save (BAK-18): a pinned version with a name, on every PC.</summary>
/// <param name="InPlace">Its files are the game's current save: the one restored last, or saved and not played since (KAN-51).</param>
public sealed record GameNamedSave(string Name, VersionId Version, DateTime SavedUtc, string Pc, bool Uploaded, bool InPlace = false);

/// <summary>
/// KAN-92: the live save as the game has it now, on this PC: its files, size and newest change; the newest version holding
/// exactly it (when it was backed up, and whether that's uploaded yet); and the named save it's the same as, if any, with
/// when it was restored when it came back that way. <see cref="InUse"/> names a file the game holds, so it couldn't be read.
/// </summary>
public sealed record CurrentSave(int Files, long Bytes, DateTime? NewestUtc, DateTime? BackedUpUtc, bool Uploaded, string? SameAs, DateTime? RestoredUtc)
{
    public string? InUse { get; init; }

    /// <summary>Every version holding exactly the live save, so a named save is In place only while the game still has it.</summary>
    public IReadOnlySet<VersionId> SameVersions { get; init; } = new HashSet<VersionId>();
}

/// <summary>A line of the game's own log.</summary>
/// <param name="Tag">What it was about (<see cref="Core.State.EventTags"/>); null in lines logged before tags were kept.</param>
public sealed record GameLogLine(DateTime AtUtc, string Level, string Message, string? Tag = null);

/// <summary>
/// What a game's page says about the game itself (LIB-19, ART-09, PLAY-11): its store page's basics, from Steam's store once, and
/// how it's installed and started on this PC.
/// </summary>
public sealed record GameAbout
{
    /// <summary>The store page's short description.</summary>
    public string? Description { get; init; }

    public IReadOnlyList<string> Developers { get; init; } = [];

    public IReadOnlyList<string> Publishers { get; init; } = [];

    public DateTime? ReleasedUtc { get; init; }

    /// <summary>Its top tags on Steam, by name: "Action", "Roguelike", "Co-op".</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    public long? SteamAppId { get; init; }

    public StoreKind? Store { get; init; }

    public string? StoreId { get; init; }

    public string? Engine { get; init; }

    /// <summary>The build GameSync last saw installed: Steam's build ID, Epic's version, a program's version and date.</summary>
    public string? Build { get; init; }

    public string? AntiCheat { get; init; }

    /// <summary>How much space it takes, when its store says (Steam's manifest).</summary>
    public long? InstallBytes { get; init; }

    /// <summary>The store's link a store game starts from; null for a game in its own folder.</summary>
    public string? StoreLink { get; init; }

    /// <summary>The program a game in its own folder starts from.</summary>
    public string? Program { get; init; }

    /// <summary>The program was picked for it on this PC, rather than found in its folder.</summary>
    public bool ProgramPicked { get; init; }

    /// <summary>The options the person set in Steam, which Steam adds to every start; read only.</summary>
    public string? StoreLaunchOptions { get; init; }

    /// <summary>The options GameSync adds when it starts a game in its own folder.</summary>
    public string? LaunchOptions { get; init; }

    /// <summary>How GameSync found the game on this PC: "Steam's library", "a folder you added", "you".</summary>
    public string? FoundBy { get; init; }
}

/// <summary>
/// A game's page (design system → GameDetailScreen): where its saves are, its named saves, every version from every PC
/// and the space they take (BAK-14, BAK-15), and its log. Read from this PC alone, the library, the backup folder's copy
/// of the version records, pins and PCs, and the game's events, so it opens at once, offline too, and never waits on a
/// sync. The agent brings other PCs' records down every 15 minutes.
/// </summary>
public sealed record GameDetail
{
    public required GameId Id { get; init; }

    /// <summary>Its saves sync: confirmed, or added by hand.</summary>
    public bool Syncs { get; init; }

    /// <summary>How the places were found and since when: "Found by the save list. Backed up since 27 Sep."</summary>
    public string? FoundBy { get; init; }

    public IReadOnlyList<GamePlace> Places { get; init; } = [];

    /// <summary>Places a later scan found that the game's rules don't take yet; the person adds them (R8).</summary>
    public IReadOnlyList<GamePlace> Suggestions { get; init; } = [];

    /// <summary>For a game not syncing yet, the files the last scan found and their size: what keeping it starts with (KAN-63).</summary>
    public int FoundFiles { get; init; }

    public long FoundBytes { get; init; }

    public IReadOnlyList<GameNamedSave> NamedSaves { get; init; } = [];

    /// <summary>Every version still kept, newest first.</summary>
    public IReadOnlyList<GameVersion> Versions { get; init; } = [];

    /// <summary>What the versions' files take together, each file counted once (BAK-15).</summary>
    public long HistoryBytes { get; init; }

    /// <summary>The game's log, newest first.</summary>
    public IReadOnlyList<GameLogLine> Log { get; init; } = [];

    public string? InstallDir { get; init; }

    /// <summary>It ships an anti-cheat: no learn mode, and it starts only its store's way (LIB-09).</summary>
    public bool HasAntiCheat { get; init; }

    /// <summary>When the current save was made, and on which PC: the Saves card's "Backed up".</summary>
    public DateTime? LastBackupUtc { get; init; }

    public string? LastBackupPc { get; init; }

    /// <summary>The game itself: its store page's basics, and how it's installed and started here.</summary>
    public GameAbout About { get; init; } = new();

    /// <summary>A conflict waiting for the person, or the last one GameSync settled while its winner is current (SYNC-04, SYNC-10).</summary>
    public ConflictDetail? Conflict { get; init; }

    /// <summary>
    /// KAN-61: for a game not syncing yet, its live save found beside copies kept by hand in a folder the scan proposed
    /// whole; then Places shows the live save alone, and FoundFiles and FoundBytes are the live save's with the rest.
    /// </summary>
    public GameKept? Kept { get; init; }

    /// <summary>KAN-92: the live save as it is now; null for a game not syncing, or whose saves aren't here.</summary>
    public CurrentSave? Current { get; init; }

    /// <summary>ACH-01: its achievements on this PC, from Steam's own files; null for a game no store here keeps them for.</summary>
    public GameAchievementsView? Achievements { get; init; }

    /// <summary>Left out of the achievements on this PC (KAN-110): its page says so, with Count it again.</summary>
    public bool AchievementsLeftOut { get; init; }

    /// <summary>Its Zenith has been seen on this PC (version 49), so its moment doesn't play again.</summary>
    public bool ZenithSeen { get; init; }

    /// <summary>FIND-04: learn mode for it: waiting for its next session, watching, or what it found.</summary>
    public LearnView Learn { get; init; } = LearnView.None;

    /// <summary>KAN-87: where this PC keeps the game's history (its named saves' files, stored by content).</summary>
    public string? BackupFolder { get; init; }

    /// <summary>KAN-82: how its named saves are listed on this PC: <c>newest</c> or <c>name</c>.</summary>
    public string NamedSort { get; init; } = "newest";
}

/// <summary>Reads a game's page from a data folder.</summary>
public static class GameDetails
{
    /// <summary>How many of the game's log lines the page shows.</summary>
    public const int LogLines = 50;

    /// <summary>Null for a game this PC doesn't know.</summary>
    /// <param name="steamAppId">Its Steam app ID as the launcher knows it (from Steam, or the save list for other copies), for its About.</param>
    public static async Task<GameDetail?> ReadAsync(string dataDir, GameId game, CancellationToken ct, long? steamAppId = null)
    {
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null);
        var definition = engine.Games.FirstOrDefault(g => g.Id == game);
        if (entry is null && definition is null)
        {
            return null;
        }

        var historyFolder = Cli.HistoryFolder(engine.State, dataDir);
        var history = new LocalHistory(historyFolder);
        var thinned = await history.Log.ListThinnedAsync(game, ct);
        var versions = (await history.Log.ListAsync(game, ct)).Where(v => !thinned.Contains(v.Id)).ToList();
        var allPins = await history.Log.ListPinsAsync(game, ct);
        var pins = allPins.GroupBy(p => p.Version).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedUtc).First());
        var pending = history.PendingVersions(game).ToHashSet();
        var pcs = history.LoadDevices().ToDictionary(d => d.Id, d => d.Name);
        var events = engine.State.GetEvents(game, LogLines);
        var programs = engine.State.GetSetting(SyncService.ProgramsKey(game)) is { Length: > 0 } found ? found.Split('\n') : [];
        var detail = Describe(game, entry, definition, versions, pins, pending, pcs, events, engine.Here.Resolver, programs);
        if (definition is null && entry is not null && KeptSaves.Find(entry, engine.Here.Resolver) is { } kept)
        {
            detail = WithKept(detail, entry, kept);
        }

        var folder = engine.InstallDirs.GetValueOrDefault(game) ?? (entry?.Installed == true ? entry.InstallDir : null);
        var conflict = definition is null ? null
            : ConflictDetails.Describe(definition, versions, allPins, engine.State.GetState(game), engine.State.GetSessions(game), engine.Device, pcs);
        var gameHistory = Path.Combine(historyFolder, "games", game.Value);
        var live = definition is null ? null : Current(new SnapshotScanner(engine.Here.Guard, engine.State), definition, versions, pins, pending);
        return detail with
        {
            // A named save is In place while the game's files are still it: once the game saves over it (played on after
            // a restore), it can be restored again, though nothing is backed up until the game closes. When the live
            // save can't be read, the newest version stands for it.
            NamedSaves = live is { InUse: null } ? detail.NamedSaves.Select(n => n with { InPlace = live.SameVersions.Contains(n.Version) }).ToList() : detail.NamedSaves,
            About = About(dataDir, game, entry, engine.State, folder, steamAppId),
            Conflict = conflict,
            Current = live,
            BackupFolder = Directory.Exists(gameHistory) ? gameHistory : historyFolder,
            Achievements = entry is null ? null : Host.Achievements.For(dataDir, game, entry.DisplayTitle, entry.Store, steamAppId),
            AchievementsLeftOut = Host.Achievements.IsLeftOut(engine.State, game),
            ZenithSeen = Host.Achievements.ZenithSeen(engine.State, game),
            Learn = LearnMode.View(dataDir, engine.State, entry, definition is not null),
            NamedSort = engine.State.GetSetting(LauncherData.NamedSortKey) == "name" ? "name" : "newest",
        };
    }

    /// <summary>
    /// KAN-92: the live save as the game has it now, read as a sync would read it (the saves, and the settings files when
    /// they sync), and the versions holding exactly it. The registry's exports are left out of the comparison: they're
    /// written at each sync, not by the game. Null when its saves aren't on this PC.
    /// </summary>
    internal static CurrentSave? Current(SnapshotScanner scanner, GameDefinition definition, IReadOnlyList<VersionRecord> versions,
        IReadOnlyDictionary<VersionId, PinRecord> pins, IReadOnlySet<VersionId> pending)
    {
        Snapshot snapshot;
        try
        {
            snapshot = scanner.Scan(definition, c => c == SaveCategory.Save || (c == SaveCategory.Config && definition.SyncConfig));
        }
        catch (FileInUseException e)
        {
            return new CurrentSave(0, 0, null, null, false, null, null) { InUse = Path.GetFileName(e.FilePath) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidGameDefinitionException)
        {
            return null;
        }

        var files = snapshot.Files;
        if (files.Count == 0)
        {
            return null;
        }

        static List<FileEntry> Saves(IEnumerable<FileEntry> all) => all.Where(f => !f.Path.StartsWith(GameDefinition.RegistryRoot + "/", StringComparison.Ordinal)).ToList();
        var same = versions.Where(v => FileSet.SameContent(Saves(v.Files), files)).OrderByDescending(v => v.CreatedUtc).ToList();
        var newest = same.FirstOrDefault();
        var named = same.Select(v => pins.GetValueOrDefault(v.Id)).FirstOrDefault(p => p is { Named: true });
        return new CurrentSave(files.Count, FileSet.TotalSize(files), files.Max(f => f.ModifiedUtc), newest?.CreatedUtc, newest is not null && !pending.Contains(newest.Id),
            named?.Label, newest is { Origin: VersionOrigin.Restore } restored ? restored.CreatedUtc : null)
        {
            SameVersions = same.Select(v => v.Id).ToHashSet(),
        };
    }

    /// <summary>
    /// KAN-61: a game not syncing yet whose folder holds its live save beside copies kept by hand: the live save stands
    /// where the whole folder did, and what keeping it starts with counts the live save, not the copies.
    /// </summary>
    internal static GameDetail WithKept(GameDetail detail, LibraryEntry entry, GameKept kept)
    {
        var others = entry.Proposals.Where(p => !(p.Root.Equals(kept.Root, StringComparison.OrdinalIgnoreCase) && p.Include == "**")).ToList();
        var live = new GamePlace(Shown($"{kept.Root.TrimEnd('/')}/{kept.Live}"), kept.LiveFolder, null, Evidence(kept.LiveFiles, kept.LiveBytes, kept.LiveNewestUtc));
        return detail with
        {
            Kept = kept,
            Places = [live, .. detail.Places.Where(p => p.Portable != Shown(kept.Root))],
            FoundFiles = kept.LiveFiles + others.Sum(p => p.Files),
            FoundBytes = kept.LiveBytes + others.Sum(p => p.Bytes),
        };
    }

    /// <summary>The game itself (LIB-19, ART-09, PLAY-11): what its Steam store page said when GameSync asked for its art, and how it starts here.</summary>
    private static GameAbout About(string dataDir, GameId game, LibraryEntry? entry, Core.State.StateStore state, string? folder, long? steamAppId)
    {
        SteamStoreInfo? info = null;
        IReadOnlyList<string> genres = [];
        if (steamAppId is > 0 and var appId)
        {
            using var art = new ArtCache(dataDir);
            info = art.Info(appId);
            genres = info is null ? [] : art.TagsOf(info);
        }

        var storeLink = GameLaunch.StoreLink(entry);
        var picked = GameLaunch.PickedProgram(state, game);
        var steamRoot = entry is { Store: StoreKind.Steam } ? StoreLocations.SteamRoot() : null;
        return new GameAbout
        {
            Description = info?.Description,
            Developers = info?.Developers ?? [],
            Publishers = info?.Publishers ?? [],
            ReleasedUtc = info?.ReleasedUtc,
            Genres = genres,
            SteamAppId = steamAppId,
            Store = entry?.Store,
            StoreId = entry?.StoreId,
            // A folder that gives no clue says "unknown", which isn't worth showing.
            Engine = entry?.Engine is { Length: > 0 } engine && engine != "unknown" ? engine : null,
            Build = entry?.Build,
            AntiCheat = entry?.HasAntiCheat == true ? entry.AntiCheat ?? "an anti-cheat" : null,
            InstallBytes = entry is { Store: StoreKind.Steam, StoreId: { Length: > 0 } id } && folder is not null ? SteamSize(folder, id) : null,
            StoreLink = storeLink,
            // R15: a game with an anti-cheat starts only through its anti-cheat's own launcher.
            Program = storeLink is not null ? null
                : entry?.HasAntiCheat == true ? (picked is not null && GameLaunch.IsAntiCheatLauncher(picked) && File.Exists(picked) ? picked : GameLaunch.AntiCheatLauncher(folder))
                : GameLaunch.Program(folder, picked),
            ProgramPicked = storeLink is null && picked is not null && File.Exists(picked),
            StoreLaunchOptions = steamRoot is not null && long.TryParse(entry!.StoreId, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId)
                ? SteamActivity.LaunchOptions(steamRoot, steamId) : null,
            LaunchOptions = storeLink is null ? GameLaunch.Options(state, game) : null,
            FoundBy = entry is null ? "you" : entry.Store switch
            {
                StoreKind.Steam => "Steam's library",
                StoreKind.Epic => "Epic's library",
                StoreKind.Ea => "the EA app's library",
                StoreKind.Loose => "a folder of games you added",
                _ => entry.Confirmed is not null && entry.Proposals.Count == 0 ? "you" : "its saves, from the save list",
            },
        };
    }

    /// <summary>Steam's size for an installed game, from its manifest beside the library's <c>common</c> folder.</summary>
    private static long? SteamSize(string folder, string appId)
    {
        var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)));
        return steamapps is not null && Vdf.TryReadRoot(Path.Combine(steamapps, $"appmanifest_{appId}.acf"))?["SizeOnDisk"] is { } size &&
            long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes > 0 ? bytes : null;
    }

    /// <summary>The page from what's known of the game, apart from where it's read, so every line of it is tested.</summary>
    /// <param name="programs">R2: the program files the last scan found in its save folders, by their full paths.</param>
    internal static GameDetail Describe(GameId game, LibraryEntry? entry, GameDefinition? definition, IReadOnlyList<VersionRecord> versions,
        IReadOnlyDictionary<VersionId, PinRecord> pins, IReadOnlySet<VersionId> pending, IReadOnlyDictionary<DeviceId, string> pcs,
        IReadOnlyList<Core.State.EventRow> events, RootResolver resolver, IReadOnlyList<string>? programs = null)
    {
        var heads = VersionGraph.Heads(versions).Select(v => v.Id).ToHashSet();
        var current = versions.Where(v => heads.Contains(v.Id)).MaxBy(v => v.CreatedUtc);
        string Pc(VersionRecord v) => pcs.GetValueOrDefault(v.Device.Id, v.Device.Name);

        var syncs = definition is not null;
        var places = syncs ? Places(definition!, current, entry, programs ?? []) : Found(entry, resolver);
        var suggestions = syncs && entry is not null
            ? Found(entry with { Proposals = entry.Suggestions, RegistryProposals = entry.RegistrySuggestions }, resolver)
            : [];
        var first = versions.Count > 0 ? versions.Min(v => v.CreatedUtc) : (DateTime?)null;
        var how = entry is null ? "Added by hand" : How(entry);
        var foundAnything = entry is not null && entry.Proposals.Count + entry.RegistryProposals.Count > 0;
        var foundBy = syncs
            ? first is { } since ? $"{how}. Backed up since {Day(since)}." : $"{how}. Not backed up yet."
            : foundAnything ? $"{how} at the last scan, {Day(entry!.LastSeenUtc)}." : null;

        return new GameDetail
        {
            Id = game,
            Syncs = syncs,
            FoundBy = foundBy,
            Places = places,
            Suggestions = suggestions,
            FoundFiles = syncs || entry is null ? 0 : entry.Proposals.Sum(p => p.Files),
            FoundBytes = syncs || entry is null ? 0 : entry.Proposals.Sum(p => p.Bytes),
            NamedSaves = pins.Values
                .Where(p => p.Named)
                .Select(p => versions.FirstOrDefault(v => v.Id == p.Version) is { } v ? new GameNamedSave(p.Label, v.Id, SavedAt(v), Pc(v), !pending.Contains(v.Id),
                    current is not null && FileSet.SameContent(current.Files, v.Files)) : null)
                .OfType<GameNamedSave>()
                .OrderByDescending(s => s.SavedUtc)
                .ToList(),
            // KAN-41 (the owner, 3 Oct 2026): by the time each shows, when its save was made, as the Versions tab lists them,
            // so a save named later doesn't sit above an older-looking time.
            Versions = versions
                .OrderByDescending(SavedAt)
                .ThenByDescending(v => v.CreatedUtc)
                .ThenByDescending(v => v.Id)
                .Select(v => new GameVersion(v.Id, SavedAt(v), Pc(v), Note(v, pins.GetValueOrDefault(v.Id), waiting: v.CreatedUtc > current?.CreatedUtc), v.Id == current?.Id,
                    v.Pinned || pins.ContainsKey(v.Id), FileSet.TotalSize(v.Files), !pending.Contains(v.Id)))
                .ToList(),
            HistoryBytes = versions.SelectMany(v => v.Files).DistinctBy(f => f.Hash).Sum(f => f.Size),
            Log = events.Select(e => new GameLogLine(e.AtUtc, e.Level, e.Message, e.Tag)).ToList(),
            InstallDir = entry?.Installed == true ? entry.InstallDir : null,
            HasAntiCheat = entry?.HasAntiCheat == true,
            LastBackupUtc = current is null ? null : SavedAt(current),
            LastBackupPc = current is null ? null : Pc(current),
        };
    }

    /// <summary>
    /// The deepest folder a rule's pattern names that's on this PC, for Open the save folder: <c>e:\steam\userdata</c>
    /// with <c>*/632360/remote/UserProfiles/**</c> is <c>e:\steam\userdata\41755\632360\remote\UserProfiles</c>. Where a
    /// folder is any (<c>*</c>, a Steam account), the one that leads deepest wins, and of those the latest written.
    /// </summary>
    public static string Deepest(string folder, string include)
    {
        var segments = include.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Walk(folder, segments, 0);

        static string Walk(string at, string[] segments, int from)
        {
            for (var i = from; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Contains("**", StringComparison.Ordinal))
                {
                    return at;
                }

                if (segment.Contains('*') || segment.Contains('?'))
                {
                    List<DirectoryInfo> candidates;
                    try
                    {
                        candidates = new DirectoryInfo(at).EnumerateDirectories(segment, new EnumerationOptions { IgnoreInaccessible = true })
                            .OrderByDescending(d => d.LastWriteTimeUtc).Take(20).ToList();
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        return at;
                    }

                    return candidates.Count == 0 ? at
                        : candidates.Select(c => Walk(c.FullName, segments, i + 1)).OrderByDescending(p => p.Length).First();
                }

                var next = Path.Combine(at, segment);
                if (!Directory.Exists(next))
                {
                    return at;
                }

                at = next;
            }

            return at;
        }
    }

    /// <summary>"23 Sep", or "23 Sep 2025" before this year.</summary>
    public static string Day(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.ToString(local.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>"3 files · 0.5 MB · newest 23 Sep 21:09".</summary>
    public static string Evidence(int files, long bytes, DateTime? newestUtc) =>
        string.Join(" · ", new[]
        {
            files == 1 ? "1 file" : $"{files.ToString(CultureInfo.InvariantCulture)} files",
            Cli.FormatSize(bytes),
            newestUtc is { } n ? $"newest {Day(n)} {n.ToLocalTime():HH:mm}" : null,
        }.OfType<string>());

    /// <summary>
    /// A confirmed game's folders, each with what its current version holds there and the programs found in it (R2); the
    /// registry keys after them.
    /// </summary>
    private static List<GamePlace> Places(GameDefinition definition, VersionRecord? current, LibraryEntry? entry, IReadOnlyList<string> programs)
    {
        var portableRoots = definition.PortableRoots ?? definition.Roots;
        var places = new List<GamePlace>();
        foreach (var rules in definition.Rules.Where(r => r.Root != GameDefinition.RegistryRoot).GroupBy(r => r.Root))
        {
            var portable = Shown(portableRoots.GetValueOrDefault(rules.Key, rules.Key));
            var folder = definition.Roots.GetValueOrDefault(rules.Key);
            var patterns = rules.Select(r => r.Include).Where(i => i != "**").Distinct().ToList();
            var shown = patterns.Count == 0 ? portable : $"{portable}  ({string.Join(", ", patterns)})";
            var tag = rules.All(r => r.Category == SaveCategory.Save) ? null : TagOf(rules.First(r => r.Category != SaveCategory.Save).Category);
            var files = current?.Files.Where(f => f.Path.StartsWith(rules.Key + "/", StringComparison.Ordinal)).ToList() ?? [];
            var found = entry?.Proposals.FirstOrDefault(p => SameFolder(p.Root, portable));
            var evidence = files.Count > 0 ? Evidence(files.Count, FileSet.TotalSize(files), files.Max(f => f.ModifiedUtc))
                : found is not null ? Evidence(found.Files, found.Bytes, found.NewestUtc)
                : "Nothing backed up from here yet";
            var include = rules.FirstOrDefault(r => r.Category == SaveCategory.Save)?.Include ?? rules.First().Include;
            places.Add(new GamePlace(shown, folder is null || RootResolver.IsUnresolved(folder) ? null : Directory.Exists(folder) ? Deepest(folder, include) : folder, tag, evidence)
            {
                Programs = folder is null ? [] : programs.Where(p => IsUnder(p, folder)).ToList(),
            });
        }

        foreach (var key in definition.Registry)
        {
            var found = entry?.RegistryProposals.FirstOrDefault(p => Core.Safety.RegistryGuard.SameKey(p.Key, key.Key));
            places.Add(new GamePlace(key.Key.Replace('/', '\\'), null, "Registry", found is null ? "Backed up with the saves" : Values(found)));
        }

        return places;
    }

    /// <summary>What the last scan found for a game not syncing yet: its folders, with files, size and newest date, and registry keys.</summary>
    private static List<GamePlace> Found(LibraryEntry? entry, RootResolver resolver)
    {
        if (entry is null)
        {
            return [];
        }

        var places = entry.Proposals
            .OrderByDescending(p => p.Category == SaveCategory.Save)
            .ThenByDescending(p => p.Files)
            .Select(p =>
            {
                var folder = resolver.Resolve(new GameDefinition { Id = entry.Id, Title = entry.DisplayTitle, Roots = new Dictionary<string, string> { ["r"] = p.Root }, Rules = [] })
                    .Roots["r"];
                var shown = p.Include == "**" ? Shown(p.Root) : $"{Shown(p.Root)}  ({p.Include})";
                return new GamePlace(shown, RootResolver.IsUnresolved(folder) ? null : Directory.Exists(folder) ? Deepest(folder, p.Include) : folder,
                    p.Category == SaveCategory.Save ? null : TagOf(p.Category), Evidence(p.Files, p.Bytes, p.NewestUtc));
            })
            .ToList();
        places.AddRange(entry.RegistryProposals.Select(r => new GamePlace(r.Key.Replace('/', '\\'), null, "Registry", Values(r))));
        return places;
    }

    /// <summary>How a game's places were found: "Found by the save list", "Found by the Unreal engine rule", "Added by hand".</summary>
    private static string How(LibraryEntry entry)
    {
        var by = entry.Proposals.Select(p => p.FoundBy).Concat(entry.RegistryProposals.Select(r => r.FoundBy))
            .GroupBy(f => f).OrderByDescending(g => g.Count()).Select(g => (FoundBy?)g.Key).FirstOrDefault();
        return by switch
        {
            Core.Discovery.FoundBy.SaveList => "Found by the save list",
            Core.Discovery.FoundBy.EngineRule => entry.Engine is { Length: > 0 } engine ? $"Found by the {engine} engine rule" : "Found by an engine rule",
            Core.Discovery.FoundBy.NameSearch => "Found by a search for its name",
            Core.Discovery.FoundBy.IdFolder => "Found in a folder named by its Steam ID",
            Core.Discovery.FoundBy.Ludusavi => "From Ludusavi",
            Core.Discovery.FoundBy.ByHand => "Added by hand",
            _ => entry.Confirmed is not null ? "Confirmed" : "Found",
        };
    }

    /// <summary>What sets a version apart in the history: its pin's label, or why it was kept.</summary>
    /// <param name="waiting">Newer than the current save: a held change still waits for the person.</param>
    private static string? Note(VersionRecord version, PinRecord? pin, bool waiting) => pin?.Label ?? version.Label ?? version.Origin switch
    {
        _ when version.Kind == VersionKind.Held => waiting ? "Changed while the game wasn't running, held for you" : "Changed outside play",
        VersionOrigin.KeptInConflict => "Kept from a conflict",
        VersionOrigin.KeptBeforeRestore => "Your files before a restore",
        VersionOrigin.KeptAtFirstSync => "This PC's files at its first sync",
        VersionOrigin.BeforeUpdate => "Before a game update",
        VersionOrigin.Imported => "Imported",
        _ => null,
    };

    /// <summary>When the save itself was made: its newest file, not when GameSync stored it.</summary>
    internal static DateTime SavedAt(VersionRecord version) => version.Files.Count > 0 ? version.Files.Max(f => f.ModifiedUtc) : version.CreatedUtc;

    private static string Values(RegistryProposal key) => key.Values == 1 ? "1 value" : $"{key.Values.ToString(CultureInfo.InvariantCulture)} values";

    private static string TagOf(SaveCategory category) => category switch
    {
        SaveCategory.Config => "Settings",
        SaveCategory.Screenshots => "Screenshots",
        _ => "Saves",
    };

    /// <summary>A folder as the page shows it: with its placeholder and forward slashes, or a Windows path as Windows writes it.</summary>
    private static string Shown(string folder) => folder.StartsWith('<') ? folder.Replace('\\', '/') : folder.Replace('/', '\\');

    private static bool SameFolder(string a, string b) =>
        string.Equals(a.Replace('\\', '/').TrimEnd('/'), b.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is somewhere inside <paramref name="folder"/>.</summary>
    private static bool IsUnder(string path, string folder) =>
        path.Replace('\\', '/').StartsWith(folder.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
}
