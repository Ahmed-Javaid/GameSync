using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSync.Core.Games;
using GameSync.Core.Model;
using Microsoft.Data.Sqlite;

namespace GameSync.Core.Discovery;

public enum LibraryState
{
    /// <summary>Found, with its save locations still proposals.</summary>
    Found,

    /// <summary>Confirmed: its save rules are pinned and it syncs.</summary>
    Synced,

    Ignored,
}

/// <summary>One game in this PC's library: what detection found, and what the person decided, which rescans keep (LIB-07).</summary>
public sealed record LibraryEntry
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    /// <summary>A title the person chose; rescans keep it.</summary>
    public string? TitleByHand { get; init; }

    public LibraryState State { get; init; } = LibraryState.Found;

    /// <summary>False once a rescan no longer finds it; its saves stay (LIB-08).</summary>
    public bool Installed { get; init; } = true;

    public StoreKind? Store { get; init; }

    public string? StoreId { get; init; }

    public string? InstallDir { get; init; }

    /// <summary>
    /// The folder the person located the game in (Locate the game…, LIB-24), for a game no scan finds there: rescans keep
    /// it installed in that folder while the folder is on this PC.
    /// </summary>
    public string? InstallDirByHand { get; init; }

    /// <summary>
    /// LIB-13: a game or folder the person added by hand (Add a game or folder), such as a game server's world, with the
    /// folder they picked on this PC. No scan finds it, so rescans keep it and the place it was added with, and it's
    /// installed while that folder is here. With no program to watch on this PC, the folder's own quiet spells are its
    /// sessions.
    /// </summary>
    public string? OwnFolder { get; init; }

    public string? Build { get; init; }

    public string? Engine { get; init; }

    /// <summary>The anti-cheat found in its folder: no learn mode, official launch only (LIB-09).</summary>
    public string? AntiCheat { get; init; }

    /// <summary>LIB-09 by hand: true or false overrides what the folder says; rescans keep it.</summary>
    public bool? AntiCheatByHand { get; init; }

    public bool StoreCloud { get; init; }

    public bool ProbablyOnlineOnly { get; init; }

    public string? SaveListTitle { get; init; }

    public IReadOnlyList<Proposal> Proposals { get; init; } = [];

    /// <summary>Registry keys the latest scan found (FIND-10).</summary>
    public IReadOnlyList<RegistryProposal> RegistryProposals { get; init; } = [];

    /// <summary>The confirmed save rules, pinned: a new save list or rescan only suggests changes (FIND-06, R8).</summary>
    public GameDefinition? Confirmed { get; init; }

    /// <summary>Identities of entries merged into this one, so rescans keep finding it (LIB-07).</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    public GameId? MergedInto { get; init; }

    public DateTime FirstSeenUtc { get; init; }

    public DateTime LastSeenUtc { get; init; }

    [JsonIgnore]
    public string DisplayTitle => TitleByHand ?? Title;

    /// <summary>The person added it by hand (LIB-13).</summary>
    [JsonIgnore]
    public bool IsOwn => OwnFolder is not null;

    /// <summary>One of the person's own with no program on this PC, such as a server's world: it syncs once its folder is quiet.</summary>
    [JsonIgnore]
    public bool IsOwnFolder => IsOwn && InstallDir is null;

    [JsonIgnore]
    public bool HasAntiCheat => AntiCheatByHand ?? AntiCheat is not null;

    /// <summary>Whether the latest scan found a location the confirmed rules don't have, which the person reviews (R8).</summary>
    [JsonIgnore]
    public bool HasSuggestion => Suggestions.Count > 0 || RegistrySuggestions.Count > 0;

    /// <summary>The registry keys the latest scan found that the confirmed rules don't have yet.</summary>
    [JsonIgnore]
    public IReadOnlyList<RegistryProposal> RegistrySuggestions =>
        Confirmed is null || State != LibraryState.Synced
            ? []
            : RegistryProposals.Where(p => !Confirmed.Registry.Any(r => Safety.RegistryGuard.SameKey(r.Key, p.Key))).ToList();

    /// <summary>The latest scan's proposals that the confirmed rules don't have yet.</summary>
    [JsonIgnore]
    public IReadOnlyList<Proposal> Suggestions =>
        Confirmed is null || State != LibraryState.Synced
            ? []
            : Proposals.Where(p => !Confirmed.Rules.Any(r =>
                r.Include == p.Include && Confirmed.Roots.TryGetValue(r.Root, out var folder) &&
                folder.Replace('\\', '/').TrimEnd('/').Equals(p.Root.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase))).ToList();
}

/// <summary>This PC's library, in <c>state.db</c>: one JSON document per game.</summary>
public sealed class LibraryStore : IDisposable
{
    private readonly SqliteConnection _db;

    public LibraryStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "state.db"), Pooling = false }.ToString());
        _db.Open();
        using var create = _db.CreateCommand();
        create.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL; CREATE TABLE IF NOT EXISTS library (id TEXT PRIMARY KEY, json TEXT NOT NULL);";
        create.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();

    public IReadOnlyList<LibraryEntry> All()
    {
        using var select = _db.CreateCommand();
        select.CommandText = "SELECT json FROM library ORDER BY id";
        using var reader = select.ExecuteReader();
        var entries = new List<LibraryEntry>();
        while (reader.Read())
        {
            if (JsonSerializer.Deserialize<LibraryEntry>(reader.GetString(0), Json.Options) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public void SaveAll(IEnumerable<LibraryEntry> entries)
    {
        using var transaction = _db.BeginTransaction();
        Upsert(entries, transaction);
        transaction.Commit();
    }

    /// <summary>The library after a scan: these entries and no others, since a scan can find that a folder isn't a game (LIB-28).</summary>
    public void ReplaceAll(IReadOnlyCollection<LibraryEntry> entries)
    {
        var kept = entries.Select(e => e.Id.Value).ToHashSet(StringComparer.Ordinal);
        using var transaction = _db.BeginTransaction();
        var gone = new List<string>();
        using (var select = _db.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id FROM library";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                if (!kept.Contains(reader.GetString(0)))
                {
                    gone.Add(reader.GetString(0));
                }
            }
        }

        foreach (var id in gone)
        {
            using var delete = _db.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM library WHERE id = $id";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }

        Upsert(entries, transaction);
        transaction.Commit();
    }

    private void Upsert(IEnumerable<LibraryEntry> entries, SqliteTransaction transaction)
    {
        foreach (var entry in entries)
        {
            using var upsert = _db.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = "INSERT INTO library (id, json) VALUES ($id, $json) ON CONFLICT(id) DO UPDATE SET json = $json";
            upsert.Parameters.AddWithValue("$id", entry.Id.Value);
            upsert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry, Json.Options));
            upsert.ExecuteNonQuery();
        }
    }
}

/// <summary>What a person can do to the library, and how a scan's findings fold into it without undoing any of that.</summary>
public static class Library
{
    /// <summary>
    /// Folds a scan into the library. A game is recognised by its store ID, its install folder, or an identity merged
    /// into it, then by title; what the person decided (title, ignore, confirmed rules, merges) stays. Games the scan
    /// didn't find become Not installed, never deleted (LIB-08). A game new here takes the ID another PC already gave
    /// it when the titles match (<paramref name="elsewhere"/>: ID to title), so both PCs sync it as one game.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Reconcile(IReadOnlyList<LibraryEntry> library, IReadOnlyList<DiscoveredGame> found, DateTime nowUtc,
        IReadOnlyDictionary<GameId, string>? elsewhere = null, IReadOnlyList<LeftoverGame>? leftovers = null)
    {
        var entries = library.ToDictionary(e => e.Id);
        var seen = new HashSet<GameId>();
        var absorbed = new Dictionary<GameId, List<Proposal>>();

        GameId NewIdFor(params string[] titles)
        {
            var names = titles.Select(SaveList.Normalize).ToHashSet(StringComparer.Ordinal);
            return elsewhere?.Where(e => !entries.ContainsKey(e.Key) && names.Contains(SaveList.Normalize(e.Value))).Select(e => (GameId?)e.Key).FirstOrDefault()
                ?? NewId(titles[0], entries.Keys);
        }

        foreach (var game in found)
        {
            var keys = Identities(game.Installed);

            // The game itself, by its store ID or install folder.
            if (entries.Values.FirstOrDefault(e => e.MergedInto is null && Identities(e, withAliases: false).Overlaps(keys)) is { } own)
            {
                entries[own.Id] = Updated(own, game, nowUtc);
                seen.Add(own.Id);
                continue;
            }

            // Something merged into another game: its locations join that game, which keeps its own install.
            var into = entries.Values.FirstOrDefault(e => e.MergedInto is null && e.Aliases.Any(keys.Contains))
                ?? (entries.Values.FirstOrDefault(e => e.MergedInto is not null && Identities(e, withAliases: false).Overlaps(keys)) is { MergedInto: { } target }
                    ? entries.GetValueOrDefault(target)
                    : null);
            if (into is not null)
            {
                if (!absorbed.TryGetValue(into.Id, out var list))
                {
                    absorbed[into.Id] = list = [];
                }

                list.AddRange(game.Proposals);
                seen.Add(into.Id);
                continue;
            }

            // A game known here by title only, such as one that was Not installed and came back in a new folder.
            if (entries.Values.FirstOrDefault(e => e.MergedInto is null && !seen.Contains(e.Id) && SaveList.Normalize(e.Title) == SaveList.Normalize(game.Title)) is { } byTitle)
            {
                entries[byTitle.Id] = Updated(byTitle, game, nowUtc);
                seen.Add(byTitle.Id);
                continue;
            }

            var id = NewIdFor(game.Title, game.Installed.Title);
            entries[id] = Updated(new LibraryEntry { Id = id, Title = game.Title, FirstSeenUtc = nowUtc }, game, nowUtc);
            seen.Add(id);
        }

        foreach (var (id, proposals) in absorbed)
        {
            var entry = entries[id];
            entries[id] = entry with { Proposals = entry.Proposals.Concat(proposals).DistinctBy(p => (p.Root, p.Include)).ToList(), LastSeenUtc = nowUtc };
        }

        // Saves of games that aren't installed, unless an installed game already stands for them.
        var leftSeen = new HashSet<GameId>();
        foreach (var left in leftovers ?? [])
        {
            var name = SaveList.Normalize(left.Listed.Title);
            var existing = entries.Values.FirstOrDefault(e => SaveList.Normalize(e.SaveListTitle ?? e.Title) == name || SaveList.Normalize(e.Title) == name);
            if (existing is not null && (existing.MergedInto is not null || seen.Contains(existing.Id)))
            {
                continue;
            }

            var entry = existing ?? new LibraryEntry { Id = NewIdFor(left.Listed.Title), Title = left.Listed.Title, FirstSeenUtc = nowUtc };

            // The save list says which stores' clouds keep the game's saves, but a copy the person located or added is their
            // own, which no store syncs (KAN-44).
            var own = entry.InstallDirByHand is { Length: > 0 } || entry.Store == StoreKind.Loose || entry.IsOwn;
            var merged = entry with
            {
                Title = left.Listed.Title,
                Installed = Located(entry),
                SaveListTitle = left.Listed.Title,
                StoreCloud = !own && left.StoreCloud,
                ProbablyOnlineOnly = left.ProbablyOnlineOnly,
                Proposals = left.Proposals,
                RegistryProposals = left.Registry,
                LastSeenUtc = nowUtc,
            };
            entries[entry.Id] = own ? NotStoreSynced(merged, entry) : merged;
            leftSeen.Add(entry.Id);
        }

        // What this scan didn't find at all is Not installed, and nothing was found for it; confirmed rules stay (LIB-08).
        // A game the person located stays installed in its folder while the folder is here (LIB-24), and one they added
        // keeps the place they added it with (LIB-13). A folder a scan took for a game that it no longer does while the
        // folder is still here, such as a folder of several games or of tools, leaves the library if nothing was decided
        // about it (LIB-28).
        foreach (var entry in entries.Values.ToList())
        {
            if (seen.Contains(entry.Id) || leftSeen.Contains(entry.Id) || entry.MergedInto is not null)
            {
                continue;
            }

            if (NotAGameAnyMore(entry) && !entries.Values.Any(e => e.MergedInto == entry.Id))
            {
                entries.Remove(entry.Id);
            }
            else if (entry.Installed || entry.Proposals.Count > 0 || entry.RegistryProposals.Count > 0)
            {
                entries[entry.Id] = entry with { Installed = Located(entry), Proposals = ByHand(entry), RegistryProposals = [] };
            }
        }

        foreach (var settled in SettleOwnCopies(entries.Values))
        {
            entries[settled.Id] = settled;
        }

        return entries.Values.OrderBy(e => e.Id.Value, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// LIB-24: Locate the game…, for a game found by its saves or one that moved: it's installed in <paramref name="folder"/>
    /// on this PC, a game in its own folder that starts from its program there, and rescans keep it so while the folder
    /// is here. What the folder shows (its engine, an anti-cheat) is taken as a scan would.
    /// </summary>
    public static LibraryEntry Locate(LibraryEntry entry, string folder, Fingerprint? print, DateTime nowUtc) => NotStoreSynced(entry with
    {
        Installed = true,
        Store = StoreKind.Loose,
        StoreId = null,
        InstallDir = folder,
        InstallDirByHand = folder,
        Build = null,
        Engine = print?.Engine ?? entry.Engine,
        AntiCheat = print is null ? entry.AntiCheat : print.AntiCheat,
        LastSeenUtc = nowUtc,
    }, entry);

    /// <summary>
    /// KAN-44: the entries that are the person's own copies (located by hand, in their own folders, or added by them) but
    /// still taken for their store's, put right: such a copy syncs between PCs. Run whenever GameSync opens its data, so
    /// a copy located before this rule, which no scan may ever see again, is put right too; empty when nothing changes.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> SettleOwnCopies(IEnumerable<LibraryEntry> entries) => entries
        .Where(e => e.StoreCloud && e.MergedInto is null && (e.InstallDirByHand is { Length: > 0 } || e.Store == StoreKind.Loose || e.IsOwn))
        .Select(e => NotStoreSynced(e, e))
        .ToList();

    /// <summary>
    /// KAN-44: a copy no store's cloud syncs, such as one in its own folder. A game confirmed as backup only because it was
    /// taken for its store's (its saves were found before the game, and the save list says a store's cloud keeps them) syncs
    /// between PCs instead; one the person set to back up only in its Properties stays so.
    /// </summary>
    private static LibraryEntry NotStoreSynced(LibraryEntry entry, LibraryEntry before) =>
        before.StoreCloud && entry.Confirmed is { Mode: GameMode.BackupOnly } confirmed
            ? entry with { StoreCloud = false, Confirmed = confirmed with { Mode = GameMode.Sync } }
            : entry with { StoreCloud = false };

    /// <summary>
    /// LIB-13: a game or folder of the person's own joins the library under the name they gave it, with the folder they
    /// picked as its place (<paramref name="place"/>, as every PC reads it). With a program, it's installed in that
    /// program's game folder, as Locate the game… does, and starts from there; without one it has nothing to start, and
    /// its folder being here is what makes it installed. Its ID comes from the name, so the other PC's, added under the
    /// same name, is the same game.
    /// </summary>
    public static LibraryEntry AddOwn(string name, string folder, Proposal place, string? programFolder, IEnumerable<GameId> taken, DateTime nowUtc) => new()
    {
        Id = NewId(name, taken),
        Title = name,
        TitleByHand = name,
        Installed = true,
        Store = programFolder is null ? null : StoreKind.Loose,
        InstallDir = programFolder,
        InstallDirByHand = programFolder,
        OwnFolder = folder,
        Proposals = [place],
        FirstSeenUtc = nowUtc,
        LastSeenUtc = nowUtc,
    };

    /// <summary>
    /// The person located it in a folder that's on this PC now, or added it with one: an unplugged drive only makes it Not
    /// installed for now.
    /// </summary>
    private static bool Located(LibraryEntry entry) =>
        (entry.InstallDirByHand is { Length: > 0 } folder && Directory.Exists(folder)) ||
        (entry.OwnFolder is { Length: > 0 } own && Directory.Exists(own));

    /// <summary>The places the person added a game of their own with, which no scan finds and every scan keeps (LIB-13).</summary>
    private static IReadOnlyList<Proposal> ByHand(LibraryEntry entry) =>
        entry.IsOwn ? entry.Proposals.Where(p => p.FoundBy == FoundBy.ByHand).ToList() : [];

    /// <summary>
    /// FIND-06: pins the proposals as the game's save rules; a game its store's cloud syncs is backup only. Confirming
    /// a game that has rules accepts the latest scan's suggestions: new locations join, and the rules already there
    /// stay, keys and all.
    /// </summary>
    /// <param name="mode">How it syncs: between PCs, or backed up only (KAN-63: named saves and imports before syncing);
    /// by default backed up only when its store's cloud syncs it, else between PCs.</param>
    /// <param name="defaults">What a game starting to sync takes (SET-06); a game that syncs already keeps its own.</param>
    public static LibraryEntry Confirm(LibraryEntry entry, GameMode? mode = null, GameDefaults? defaults = null)
    {
        if (entry.Proposals.Count == 0 && entry.RegistryProposals.Count == 0)
        {
            throw new InvalidOperationException($"No saves were found for {entry.DisplayTitle} yet, so there's nothing to confirm.");
        }

        var chosen = mode ?? (entry.StoreCloud ? GameMode.BackupOnly : GameMode.Sync);
        var definition = entry.Confirmed is { } current
            ? Discoverer.Extend(current with { Title = entry.DisplayTitle, Mode = chosen }, entry.Proposals)
            : (defaults ?? new GameDefaults()).Apply(Discoverer.ToDefinition(entry.Id, entry.DisplayTitle, entry.Proposals, chosen));
        var keys = definition.Registry.ToList();
        keys.AddRange(entry.RegistryProposals
            .Where(p => !keys.Any(k => Safety.RegistryGuard.SameKey(k.Key, p.Key)))
            .Select(p => new RegistryRule { Key = p.Key, Category = p.Category }));
        return entry with { State = LibraryState.Synced, Confirmed = definition with { Registry = keys } };
    }

    /// <summary>
    /// PC-04 and R8: takes up the rules another PC's saves used, exactly and with their root keys, so that PC's versions
    /// map here and both PCs take the same files. Whatever this PC found beyond them stays a suggestion. This PC's own
    /// choices, which the rules don't carry (settings files synced, screenshots, who wins), come from its defaults (SET-06).
    /// </summary>
    public static LibraryEntry Adopt(LibraryEntry entry, PortableRules shared, GameDefaults? defaults = null)
    {
        var mode = entry.StoreCloud ? GameMode.BackupOnly : GameMode.Sync;
        var mine = defaults ?? new GameDefaults();
        return entry with
        {
            State = LibraryState.Synced,
            Confirmed = shared.ToDefinition(entry.Id) with
            {
                Title = entry.DisplayTitle,
                Mode = mode,
                SyncConfig = mine.SettingsFiles == GameDefaults.SyncBetween,
                IncludeScreenshots = mine.Screenshots,
                ConflictPolicy = mine.Conflict,
            },
        };
    }

    /// <summary>
    /// ONB-04: Ludusavi's ignore list. A game confirmed in GameSync stays as it is; a title the library doesn't know
    /// yet gets an entry of its own, so the scan that finds it later finds it ignored.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> IgnoreTitles(IReadOnlyList<LibraryEntry> library, IEnumerable<string> titles, DateTime nowUtc)
    {
        var entries = library.ToDictionary(e => e.Id);
        foreach (var title in titles)
        {
            var name = SaveList.Normalize(title);
            var matches = entries.Values.Where(e => e.MergedInto is null && Names(e).Contains(name)).ToList();
            if (matches.Count == 0)
            {
                var id = NewId(title, entries.Keys);
                entries[id] = new LibraryEntry { Id = id, Title = title, State = LibraryState.Ignored, Installed = false, FirstSeenUtc = nowUtc, LastSeenUtc = nowUtc };
            }

            foreach (var entry in matches.Where(e => e.State == LibraryState.Found))
            {
                entries[entry.Id] = entry with { State = LibraryState.Ignored };
            }
        }

        return entries.Values.OrderBy(e => e.Id.Value, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// ONB-04: a game added by hand in Ludusavi joins the entry it's about, found by title or by a folder both take,
    /// or becomes one. Like in Ludusavi, its places replace what the scan found, or add to them when it extends.
    /// </summary>
    public static LibraryEntry WithLudusaviGame(IReadOnlyList<LibraryEntry> library, LudusaviCustomGame game, IReadOnlyList<Proposal> proposals, DateTime nowUtc,
        IReadOnlyList<RegistryProposal>? registry = null)
    {
        var name = SaveList.Normalize(game.Name);
        var entry = library.FirstOrDefault(e => e.MergedInto is null && Names(e).Contains(name))
            ?? library.FirstOrDefault(e => e.MergedInto is null && e.Proposals.Any(p => proposals.Any(q => Discoverer.Covers(p, q) || Discoverer.Covers(q, p))))
            ?? new LibraryEntry { Id = NewId(game.Name, library.Select(e => e.Id)), Title = game.Name, Installed = false, FirstSeenUtc = nowUtc };
        var places = game.Extends ? entry.Proposals.Concat(proposals).DistinctBy(p => (p.Root, p.Include)).ToList() : proposals.ToList();
        var keys = game.Extends ? entry.RegistryProposals.Concat(registry ?? []).ToList() : (registry ?? []).ToList();
        var alias = $"{TitleAlias}{name}";
        return entry with
        {
            Proposals = places,
            RegistryProposals = keys,
            Aliases = Names(entry).Contains(name) ? entry.Aliases : [.. entry.Aliases, alias],
            LastSeenUtc = nowUtc,
        };
    }

    /// <summary>Every name an entry goes by, as letters and digits, including titles other tools know it by.</summary>
    public static IReadOnlySet<string> Names(LibraryEntry entry) =>
        new[] { entry.Title, entry.TitleByHand, entry.SaveListTitle }.OfType<string>().Select(SaveList.Normalize)
            .Concat(entry.Aliases.Where(a => a.StartsWith(TitleAlias, StringComparison.Ordinal)).Select(a => a[TitleAlias.Length..]))
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>An alias holding another title for the game, such as the one Ludusavi used.</summary>
    private const string TitleAlias = "title:";

    /// <summary>LIB-07: the first entry becomes part of the second, for good; rescans keep finding it as the second.</summary>
    public static (LibraryEntry Merged, LibraryEntry Into) Merge(LibraryEntry entry, LibraryEntry into) =>
        (entry with { MergedInto = into.Id, State = LibraryState.Ignored },
         into with
         {
             Aliases = into.Aliases.Concat(Identities(entry)).Distinct().ToList(),
             Proposals = into.Proposals.Concat(entry.Proposals).DistinctBy(p => (p.Root, p.Include)).ToList(),
         });

    /// <summary>
    /// A game's ID, from its title, so both PCs give the same game the same ID: "Black Myth: Wukong" is
    /// "black-myth-wukong". A clash with another game on this PC gets a number.
    /// </summary>
    public static GameId NewId(string title, IEnumerable<GameId> taken)
    {
        // Letters and digits stay, accents drop off ("Ragnarök" is "ragnarok"), and anything else separates words.
        var slug = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var baseId = slug.ToString().Trim('-');
        if (baseId.Length == 0)
        {
            baseId = "game";
        }

        baseId = baseId[..Math.Min(baseId.Length, 36)].TrimEnd('-');
        var used = taken.Select(t => t.Value).ToHashSet(StringComparer.Ordinal);
        var id = baseId;
        for (var n = 2; used.Contains(id); n++)
        {
            id = $"{baseId}-{n}";
        }

        return GameId.Parse(id);
    }

    // A game a scan finds is where the scan found it: a folder it was located in by hand no longer counts. Found where no
    // store's cloud syncs it, it isn't its store's any more (KAN-44).
    private static LibraryEntry Updated(LibraryEntry entry, DiscoveredGame game, DateTime nowUtc) => game.StoreCloud
        ? Found(entry, game, nowUtc)
        : NotStoreSynced(Found(entry, game, nowUtc), entry);

    private static LibraryEntry Found(LibraryEntry entry, DiscoveredGame game, DateTime nowUtc) => entry with
    {
        Title = game.Title,
        Installed = true,
        Store = game.Installed.Store,
        StoreId = game.Installed.StoreId,
        InstallDir = game.Installed.InstallDir,
        InstallDirByHand = null,
        Build = game.Installed.Build,
        Engine = game.Print.Engine,
        AntiCheat = game.Print.AntiCheat,
        StoreCloud = game.StoreCloud,
        ProbablyOnlineOnly = game.ProbablyOnlineOnly,
        SaveListTitle = game.Listed?.Title,
        Proposals = game.Proposals.Concat(ByHand(entry)).DistinctBy(p => (p.Root, p.Include)).ToList(),
        RegistryProposals = game.Registry,
        LastSeenUtc = nowUtc,
    };

    // LIB-28: a folder in a folder of games that a scan found once, still here, with nothing the person decided about it.
    private static bool NotAGameAnyMore(LibraryEntry entry) =>
        entry is { Store: StoreKind.Loose, State: LibraryState.Found, TitleByHand: null, InstallDirByHand: null, OwnFolder: null, Confirmed: null, Aliases.Count: 0, InstallDir: { Length: > 0 } dir } &&
        Directory.Exists(dir);

    private static HashSet<string> Identities(InstalledGame game)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { $"dir:{Path.GetFullPath(game.InstallDir).TrimEnd('\\').ToLowerInvariant()}" };
        if (game.StoreId is { Length: > 0 } id)
        {
            keys.Add($"{game.Store.ToString().ToLowerInvariant()}:{id}");
        }

        return keys;
    }

    private static HashSet<string> Identities(LibraryEntry entry, bool withAliases = true)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (entry.InstallDir is { Length: > 0 } dir)
        {
            keys.Add($"dir:{Path.GetFullPath(dir).TrimEnd('\\').ToLowerInvariant()}");
        }

        if (entry.Store is { } store && entry.StoreId is { Length: > 0 } id)
        {
            keys.Add($"{store.ToString().ToLowerInvariant()}:{id}");
        }

        if (withAliases)
        {
            keys.UnionWith(entry.Aliases);
        }

        return keys;
    }
}
