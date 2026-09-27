using System.Diagnostics;
using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>This PC's side of the library: its settings, its library, its folders, and the checks every rule passes.</summary>
internal sealed record ThisPc(string DataDir, StateStore State, LibraryStore Library, IReadOnlyDictionary<string, string> Folders, RootResolver Resolver,
    SensitivePathGuard Guard, IRegistryStore Registry)
{
    /// <summary>Confirmed games left out of syncing, and why; said before the commands that list or sync games.</summary>
    public List<string> Skipped { get; } = [];
}

/// <summary>Milestone 3's commands: scanning, the library, confirming, and the folders to scan (LIB, FIND, FOLD-07, FOLD-08).</summary>
public static partial class Cli
{
    private const string GameFoldersKey = "library.gameFolders";
    private const string SaveFoldersKey = "library.saveFolders";

    internal static string LudusaviManifest =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ludusavi", "manifest.yaml");

    /// <summary>This PC's folders for the placeholders, with Steam's own folder for &lt;steamRoot&gt; when Steam is installed.</summary>
    internal static Dictionary<string, string> FoldersForThisPc()
    {
        var folders = new Dictionary<string, string>(KnownFolders.ForThisPc(), StringComparer.OrdinalIgnoreCase);
        if (StoreLocations.SteamRoot() is { } steam)
        {
            folders["<steamRoot>"] = steam;
        }

        return folders;
    }

    /// <summary>The library's confirmed games (FIND-06), each checked like any rule (R8); one that fails is left out and says why.</summary>
    internal static List<GameDefinition> LibraryGames(IReadOnlyList<LibraryEntry> entries, AppConfig config, ThisPc here)
    {
        var games = new List<GameDefinition>();
        foreach (var entry in entries.Where(e => e is { State: LibraryState.Synced, Confirmed: not null, MergedInto: null }))
        {
            if (config.Games.Any(g => g.Id == entry.Id))
            {
                here.Skipped.Add($"{entry.DisplayTitle}: games.json has a game with the id '{entry.Id}', and that one syncs instead.");
                continue;
            }

            var portable = entry.Confirmed! with { Title = entry.DisplayTitle };
            var resolved = here.Resolver.Resolve(portable);
            if (Problems(portable, resolved, here).FirstOrDefault() is { } problem)
            {
                here.Skipped.Add($"{entry.DisplayTitle} doesn't sync: {problem}");
                continue;
            }

            games.Add(resolved);
        }

        return games;
    }

    private static IReadOnlyList<string> Problems(GameDefinition portable, GameDefinition resolved, ThisPc here) =>
        GameValidator.Problems(portable, here.Guard).Concat(GameValidator.Problems(resolved, here.Guard)).Distinct().ToList();

    private static void PrintSkipped(ThisPc here)
    {
        foreach (var skipped in here.Skipped)
        {
            Console.WriteLine($"! {skipped}");
        }
    }

    /// <summary>The commands that need only this PC: no cloud and no sign-in. Null for any other command.</summary>
    private static async Task<int?> RunLibraryCommandAsync(string command, List<string> rest, ThisPc here)
    {
        switch (command)
        {
            case "ignore":
            case "unignore":
            {
                var ignore = command == "ignore";
                var changed = Games(rest, "game id").Select(id => Entry(here, id)).Select(e => e with
                {
                    State = ignore ? LibraryState.Ignored : e.Confirmed is null ? LibraryState.Found : LibraryState.Synced,
                }).ToList();
                here.Library.SaveAll(changed);
                foreach (var entry in changed)
                {
                    Console.WriteLine(ignore
                        ? $"Ignored {entry.DisplayTitle}. Rescans keep it ignored, and any saves it has in the cloud stay there."
                        : $"{entry.DisplayTitle} is back: {(entry.State == LibraryState.Synced ? "it syncs again" : "confirm it to sync it")}.");
                }

                return 0;
            }

            case "rename":
            {
                var entry = Entry(here, Game(rest, 0));
                var title = string.Join(' ', rest.Skip(1)).Trim();
                if (title.Length == 0)
                {
                    throw new UsageException("Give the new title: gamesync rename <game> <title>.");
                }

                here.Library.SaveAll([entry with { TitleByHand = title }]);
                Console.WriteLine($"{entry.DisplayTitle} is now called {title}. Rescans keep the name; its id stays {entry.Id}.");
                return 0;
            }

            case "merge":
            {
                var entry = Entry(here, Game(rest, 0));
                var into = Entry(here, Game(rest, 1));
                if (entry.Id == into.Id || into.MergedInto is not null)
                {
                    throw new UsageException(entry.Id == into.Id ? "A game can't be merged into itself." : $"{into.DisplayTitle} is itself merged into {into.MergedInto}; merge into that one.");
                }

                var (merged, target) = Library.Merge(entry, into);
                here.Library.SaveAll([merged, target]);
                Console.WriteLine($"{entry.DisplayTitle} is now part of {into.DisplayTitle}, and rescans keep it that way.");
                if (target.HasSuggestion)
                {
                    Console.WriteLine($"Its save locations joined {into.DisplayTitle}'s; confirm them with: gamesync confirm {into.Id}");
                }

                if (entry.State == LibraryState.Synced)
                {
                    Console.WriteLine($"{entry.DisplayTitle} stops syncing on its own; the saves it has in the cloud stay there.");
                }

                return 0;
            }

            case "folders":
            {
                var games = GameFolders(here.State);
                Console.WriteLine(games.Count == 0 ? "No game folders yet. Add one with: gamesync add-folder E:\\Games" : "Game folders scanned:");
                foreach (var folder in games)
                {
                    Console.WriteLine($"  {folder}{(Directory.Exists(folder) ? "" : "   (not there right now)")}");
                }

                var saves = SaveFolders(here.State);
                if (saves.Count > 0)
                {
                    Console.WriteLine("Save folders you added:");
                    var resolver = new Discoverer(null, here.Folders);
                    foreach (var folder in saves)
                    {
                        var full = resolver.Resolve(folder.Path, "") ?? "(not on this PC)";
                        Console.WriteLine($"  {folder.Path}{(folder.ById ? "   (one folder per Steam app ID)" : "")}{(full == folder.Path ? "" : $"   = {full}")}");
                    }
                }

                return 0;
            }

            case "add-folder":
            case "remove-folder":
            {
                var folder = FullFolder(Arg(rest, 0, "folder"));
                var folders = GameFolders(here.State);
                if (command == "add-folder")
                {
                    if (!Directory.Exists(folder))
                    {
                        throw new UsageException($"There's no folder {folder}.");
                    }

                    if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(folder);
                    }

                    Console.WriteLine($"GameSync scans {folder} from now on. Find its games with: gamesync scan");
                }
                else
                {
                    if (folders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)) == 0)
                    {
                        throw new UsageException($"{folder} isn't one of the game folders. See them with: gamesync folders");
                    }

                    Console.WriteLine($"GameSync no longer scans {folder}. Its games become Not installed at the next scan; their saves stay.");
                }

                here.State.SetSetting(GameFoldersKey, JsonSerializer.Serialize(folders));
                return 0;
            }

            case "add-save-folder":
            case "remove-save-folder":
            {
                var byId = rest.Remove("--by-id");
                var full = FullFolder(Arg(rest, 0, "folder"));
                var portable = RootResolver.ToPortable(full, here.Folders);
                var folders = SaveFolders(here.State);
                if (command == "add-save-folder")
                {
                    if (!Directory.Exists(full))
                    {
                        throw new UsageException($"There's no folder {full}.");
                    }

                    if (here.Guard.CheckRoot(full) is { } refusal)
                    {
                        throw new UsageException(refusal);
                    }

                    folders.RemoveAll(f => f.Path.Equals(portable, StringComparison.OrdinalIgnoreCase));
                    folders.Add(new ExtraSaveFolder(portable, byId));
                    Console.WriteLine(byId
                        ? $"Games with a Steam app ID now look for a folder named by it in {portable}. It stays in this PC's settings. Run: gamesync scan"
                        : $"The name search now looks in {portable} too. It stays in this PC's settings. Run: gamesync scan");
                }
                else
                {
                    if (folders.RemoveAll(f => f.Path.Equals(portable, StringComparison.OrdinalIgnoreCase)) == 0)
                    {
                        throw new UsageException($"{full} isn't one of the save folders you added. See them with: gamesync folders");
                    }

                    Console.WriteLine($"Removed {portable}. Games already confirmed with a place in it keep syncing it.");
                }

                here.State.SetSetting(SaveFoldersKey, JsonSerializer.Serialize(folders, Json.Options));
                return 0;
            }

            case "savelist":
            {
                if (Arg(rest, 0, "'update'") != "update")
                {
                    throw new UsageException("Try: gamesync savelist update [--file <manifest.yaml>]");
                }

                var store = new SaveListStore(here.DataDir);
                var file = TakeOption(rest, "--file");
                var list = file is not null ? store.UpdateFromFile(Path.GetFullPath(file)) : await store.UpdateFromWebAsync(CancellationToken.None);
                Console.WriteLine($"The save list has {list.Games.Count:N0} games, {list.Games.Count(g => g.Files.Count > 0):N0} with Windows save paths (from {list.Source}).");
                Console.WriteLine("Confirmed games keep their rules; a rescan shows what's new as suggestions.");
                return 0;
            }

            default:
                return null;
        }
    }

    // ---- scanning ----

    private static async Task<int> ScanAsync(List<string> rest, SyncService service, ThisPc here, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var (list, note) = await new SaveListStore(here.DataDir).GetAsync(rest.Contains("--refresh-list"), LudusaviManifest, ct);
        if (note is not null)
        {
            Console.WriteLine($"! {note}");
        }

        var gameFolders = GameFolders(here.State);
        var detection = Detector.Detect(StoreLocations.ForThisPc(gameFolders));
        var byStore = string.Join(", ", detection.Games.GroupBy(g => g.Store).OrderBy(g => g.Key).Select(g => $"{g.Count()} {StoreName(g.Key)}"));
        Console.WriteLine($"Found {detection.Games.Count} installed games ({(byStore.Length == 0 ? "none" : byStore)}). Looking for their saves...");

        var discoverer = new Discoverer(list, here.Folders, SaveFolders(here.State), here.Guard, here.Registry);
        var found = detection.Games.AsParallel().AsOrdered().WithDegreeOfParallelism(4).Select(discoverer.Discover).ToList();
        var leftovers = discoverer.FindUninstalled(found);
        var others = await service.OtherGamesAsync(ct);
        var entries = Library.Reconcile(here.Library.All(), found, DateTime.UtcNow, others.ToDictionary(o => o.Id, o => o.Title), leftovers);
        here.Library.SaveAll(entries);

        Console.WriteLine();
        PrintLibrary(entries, others, showIgnored: false);
        Console.WriteLine();
        Console.WriteLine($"Scanned in {clock.Elapsed.TotalSeconds:0} s; nothing was changed on your PC.");
        if (gameFolders.Count == 0)
        {
            Console.WriteLine("Games outside Steam, Epic and EA are found in the game folders you add: gamesync add-folder E:\\Games");
        }

        return 0;
    }

    /// <summary>The library by what's next for each game, like the onboarding groups in design.md.</summary>
    private static void PrintLibrary(IReadOnlyList<LibraryEntry> entries, IReadOnlyList<CloudGame> others, bool showIgnored)
    {
        var live = entries.Where(e => e.MergedInto is null).ToList();
        var cloud = others.ToDictionary(o => o.Id);
        var waiting = live.Where(e => e.State == LibraryState.Found).ToList();
        var fromCloud = waiting.Where(e => e.Installed && cloud.ContainsKey(e.Id)).ToList();
        var found = waiting.Where(e => !cloud.ContainsKey(e.Id) && FoundAnything(e)).ToList();

        Group("Syncing", live.Where(e => e.State == LibraryState.Synced), e =>
            string.Join("; ", new[]
            {
                e.Confirmed?.Mode == GameMode.BackupOnly ? "backed up only" : null,
                e.Installed ? null : "not installed here",
                e.HasSuggestion ? $"new place found, see: gamesync show {e.Id}" : null,
            }.OfType<string>()));
        Group("Found: syncs once you confirm it", found.Where(e => e is { Installed: true, StoreCloud: false, ProbablyOnlineOnly: false }), Evidence);
        Group("Found: backed up only once you confirm it (the store's own cloud syncs it)", found.Where(e => e is { Installed: true, StoreCloud: true, ProbablyOnlineOnly: false }), Evidence);
        Group("Saves found, game not installed: confirming keeps them as history", found.Where(e => e is { Installed: false, ProbablyOnlineOnly: false }), e =>
            (e.StoreCloud ? "backed up only; " : "") + Evidence(e));
        Group("Saves in the cloud from another PC: confirm takes up that PC's rules and brings the save down", fromCloud, e =>
            $"from {cloud[e.Id].Newest.Device.Name}, {cloud[e.Id].Newest.CreatedUtc.ToLocalTime():yyyy-MM-dd}");
        Group("Probably online-only: only settings found, so 'confirm --all' leaves these out", found.Where(e => e.ProbablyOnlineOnly), e =>
            (e.Installed ? "" : "not installed; ") + Evidence(e));
        Group("Installed, no saves found yet (learn mode, in Milestone 6, watches a session for them)", waiting.Where(e => e.Installed && !cloud.ContainsKey(e.Id) && !FoundAnything(e)), e =>
            e.Engine is { } engine and not "unknown" ? engine : "");
        Group("Saves in the cloud, game not installed here", others.Where(o => !live.Any(e => e.Id == o.Id)).Select(o => new LibraryEntry { Id = o.Id, Title = o.Title }), e =>
            $"from {cloud[e.Id].Newest.Device.Name}, {cloud[e.Id].Newest.CreatedUtc.ToLocalTime():yyyy-MM-dd}; offered once it's installed");
        Group("Not installed any more", waiting.Where(e => e is { Installed: false, Store: not null } && !FoundAnything(e)), _ => "");

        var ignored = live.Where(e => e.State == LibraryState.Ignored).ToList();
        if (showIgnored)
        {
            Group("Ignored", ignored, _ => "");
        }
        else if (ignored.Count > 0)
        {
            Console.WriteLine($"Ignored: {ignored.Count}. List them with: gamesync library --ignored");
        }

        if (found.Count > 0 || fromCloud.Count > 0)
        {
            Console.WriteLine("Confirm with: gamesync confirm <game>...   or everything found: gamesync confirm --all   Details: gamesync show <game>");
        }
    }

    private static void Group(string heading, IEnumerable<LibraryEntry> entries, Func<LibraryEntry, string> detail)
    {
        var list = entries.OrderBy(e => e.DisplayTitle, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            return;
        }

        Console.WriteLine($"{heading} ({list.Count})");
        foreach (var entry in list)
        {
            var flags = entry.HasAntiCheat ? " [anti-cheat]" : "";
            var text = detail(entry);
            Console.WriteLine($"  {entry.Id.Value,-30} {entry.DisplayTitle}{flags}{(text.Length == 0 ? "" : $"   {text}")}");
        }

        Console.WriteLine();
    }

    private static bool FoundAnything(LibraryEntry entry) => entry.Proposals.Count > 0 || entry.RegistryProposals.Count > 0;

    private static string Evidence(LibraryEntry entry)
    {
        var count = entry.Proposals.Count + entry.RegistryProposals.Count;
        var more = count > 1 ? $" (+{count - 1} more)" : "";
        if (entry.Proposals.Count == 0)
        {
            return Describe(entry.RegistryProposals[0]) + more;
        }

        var best = entry.Proposals.OrderByDescending(p => p.Category == SaveCategory.Save).ThenByDescending(p => p.Files).First();
        return Describe(best) + more;
    }

    private static string Describe(RegistryProposal r)
    {
        var category = r.Category == SaveCategory.Save ? "" : $" {r.Category.ToString().ToLowerInvariant()}";
        var changed = r.ChangedUtc is { } c ? $", changed {c.ToLocalTime():yyyy-MM-dd}" : "";
        return $"{Layer(r.FoundBy)}: registry {r.Key}{category}, {r.Values} value{(r.Values == 1 ? "" : "s")}{changed}";
    }

    private static string Describe(Proposal p)
    {
        var what = p.Include == "**" ? p.Root : $"{p.Root} {p.Include}";
        var category = p.Category == SaveCategory.Save ? "" : $" {p.Category.ToString().ToLowerInvariant()}";
        var newest = p.NewestUtc is { } n ? $", newest {n.ToLocalTime():yyyy-MM-dd}" : "";
        return $"{Layer(p.FoundBy)}: {what}{category}, {p.Files} file{(p.Files == 1 ? "" : "s")}, {FormatSize(p.Bytes)}{newest}";
    }

    private static string Layer(FoundBy by) => by switch
    {
        FoundBy.SaveList => "save list",
        FoundBy.EngineRule => "engine rule",
        FoundBy.NameSearch => "name search",
        FoundBy.IdFolder => "ID folder",
        FoundBy.Ludusavi => "Ludusavi",
        _ => "by hand",
    };

    private static string StoreName(StoreKind store) => store switch
    {
        StoreKind.Steam => "Steam",
        StoreKind.Epic => "Epic",
        StoreKind.Ea => "EA",
        _ => "from your game folders",
    };

    // ---- one game ----

    private static async Task<int> ShowAsync(GameId id, SyncService service, ThisPc here, CancellationToken ct)
    {
        var entry = Entry(here, id);
        Console.WriteLine($"{entry.DisplayTitle} ({entry.Id})");
        if (entry.MergedInto is { } into)
        {
            Console.WriteLine($"  Merged into {into}; its saves are found as part of that game.");
        }

        var where = entry.Store is { } store ? $"{StoreName(store)}{(entry.StoreId is null ? "" : $" {entry.StoreId}")}" : "unknown";
        var installed = entry.Installed ? $"{where}, {entry.InstallDir}" : entry.Store is null ? "no, but its saves are on this PC" : "no (not found at the last scan)";
        Console.WriteLine($"  Installed:   {installed}{(entry.Build is null ? "" : $", build {entry.Build}")}");
        Console.WriteLine($"  Engine:      {entry.Engine ?? "unknown"}");
        Console.WriteLine($"  Anti-cheat:  {(entry.AntiCheatByHand is { } byHand ? (byHand ? "yes, marked by hand" : "no, marked by hand") : entry.AntiCheat ?? "none found")}");
        if (entry.SaveListTitle is { } listed)
        {
            Console.WriteLine($"  Save list:   {listed}{(entry.StoreCloud ? ", and the store's own cloud syncs it, so it's backed up only" : "")}");
        }

        Console.WriteLine($"  Status:      {entry.State switch { LibraryState.Synced => "syncing", LibraryState.Ignored => "ignored", _ => "found, not confirmed" }}");
        Console.WriteLine(FoundAnything(entry) ? "  Found at the last scan:" : "  Found:       nothing at the last scan");
        foreach (var proposal in entry.Proposals)
        {
            Console.WriteLine($"    {Describe(proposal)}");
        }

        foreach (var key in entry.RegistryProposals)
        {
            Console.WriteLine($"    {Describe(key)}");
        }

        if (entry.Confirmed is { } confirmed)
        {
            Console.WriteLine("  Syncs with:");
            foreach (var line in PortableRules.From(confirmed).Describe())
            {
                Console.WriteLine($"    {line}");
            }

            if (entry.HasSuggestion)
            {
                Console.WriteLine($"  New since you confirmed; add them with: gamesync confirm {entry.Id}");
                foreach (var suggestion in entry.Suggestions)
                {
                    Console.WriteLine($"    {Describe(suggestion)}");
                }

                foreach (var key in entry.RegistrySuggestions)
                {
                    Console.WriteLine($"    {Describe(key)}");
                }
            }
        }

        if (await service.OtherRulesAsync(entry.Id, ct) is { } other &&
            (entry.Confirmed is null || !other.Rules.SameFilesAs(PortableRules.From(entry.Confirmed))))
        {
            Console.WriteLine($"  {other.Device.Name}'s newest save used {(entry.Confirmed is null ? "these rules" : "other rules")}; " +
                $"take them up with: gamesync confirm {entry.Id}{(entry.Confirmed is null ? "" : " --theirs")}");
            foreach (var line in other.Rules.Describe())
            {
                Console.WriteLine($"    {line}");
            }
        }

        return 0;
    }

    // ---- confirming (FIND-06, PC-04, R8) ----

    private static async Task<int> ConfirmAsync(List<string> rest, SyncService service, ThisPc here, CancellationToken ct)
    {
        var all = rest.Remove("--all");
        var mine = rest.Remove("--mine");
        var theirs = rest.Remove("--theirs");
        if (mine && theirs)
        {
            throw new UsageException("Choose --mine or --theirs, not both.");
        }

        var entries = here.Library.All().ToDictionary(e => e.Id);
        var others = (await service.OtherGamesAsync(ct)).ToDictionary(o => o.Id);
        var chosen = all
            ? entries.Values.Where(e => e is { State: LibraryState.Found, MergedInto: null, ProbablyOnlineOnly: false } &&
                (FoundAnything(e) || (e.Installed && others.ContainsKey(e.Id)))).ToList()
            : Games(rest, "game id, or --all").Select(id => Entry(here, id)).ToList();

        // A name search goes by names alone, so --all leaves those finds for a look first.
        var byNameOnly = all
            ? chosen.Where(e => !others.ContainsKey(e.Id) && e is { Proposals.Count: > 0, RegistryProposals.Count: 0 } && e.Proposals.All(p => p.FoundBy == FoundBy.NameSearch)).ToList()
            : [];
        chosen = chosen.Except(byNameOnly).ToList();
        if (chosen.Count == 0 && byNameOnly.Count == 0)
        {
            Console.WriteLine("Nothing is waiting to be confirmed. See the library with: gamesync library");
            return 0;
        }

        var confirmedCount = 0;
        foreach (var entry in chosen)
        {
            if (entry.MergedInto is { } into)
            {
                Console.WriteLine($"  {entry.DisplayTitle}: merged into {into}; confirm that one.");
                continue;
            }

            LibraryEntry confirmed;
            string source;
            (PortableRules Rules, string From)? shared = others.GetValueOrDefault(entry.Id) is { Rules: { } r } game ? (r, game.Newest.Device.Name) : null;
            if (theirs && shared is null && await service.OtherRulesAsync(entry.Id, ct) is { } other)
            {
                shared = (other.Rules, other.Device.Name);
            }

            if (theirs || (!mine && entry.Confirmed is null && shared is not null))
            {
                if (shared is not { } rules)
                {
                    Console.WriteLine($"  {entry.DisplayTitle}: no other PC's save has recorded its rules, so there's nothing to take up.");
                    continue;
                }

                confirmed = Library.Adopt(entry, rules.Rules);
                source = theirs ? $"{rules.From}'s rules" : $"{rules.From}'s rules, so both PCs take the same files";
            }
            else if (FoundAnything(entry))
            {
                confirmed = Library.Confirm(entry);
                source = entry.Confirmed is null ? "what the scan found" : "the new places the scan found";
            }
            else
            {
                Console.WriteLine($"  {entry.DisplayTitle}: no saves were found for it on this PC, so there's nothing to confirm yet.");
                continue;
            }

            var portable = confirmed.Confirmed!;
            if (Problems(portable, here.Resolver.Resolve(portable), here) is [var problem, ..])
            {
                Console.WriteLine($"  {entry.DisplayTitle}: not confirmed. {problem}");
                continue;
            }

            entries[entry.Id] = confirmed;
            confirmedCount++;
            var mode = portable.Mode == GameMode.BackupOnly ? ", backed up only" : "";
            Console.WriteLine($"  {confirmed.DisplayTitle}{mode}, with {source}:");
            foreach (var line in PortableRules.From(portable).Describe())
            {
                Console.WriteLine($"      {line}");
            }
        }

        here.Library.SaveAll(entries.Values);
        Console.WriteLine(confirmedCount == 0 ? "Nothing was confirmed." : $"Confirmed {confirmedCount}. They sync from the next 'gamesync sync'; see what it would do first with: gamesync plan");
        if (byNameOnly.Count > 0)
        {
            Console.WriteLine($"Left for a look first, since only the name search found them: {string.Join(", ", byNameOnly.Select(e => e.Id))}. " +
                "Check each with 'gamesync show <game>', then confirm it by name.");
        }

        return 0;
    }

    // ---- moving from Ludusavi (ONB-04) ----

    private static async Task<int> ImportLudusaviAsync(List<string> rest, SyncService service, ThisPc here, CancellationToken ct)
    {
        var apply = rest.Remove("--apply");
        var configPath = TakeOption(rest, "--config") ?? LudusaviReader.DefaultConfig;
        if (!File.Exists(configPath))
        {
            throw new UsageException($"There's no Ludusavi config at {configPath}. Give its path with --config <file>.");
        }

        var config = LudusaviReader.ReadConfig(configPath);
        var now = DateTime.UtcNow;
        var before = here.Library.All();

        // The ignore list; what GameSync already syncs stays synced.
        var library = Library.IgnoreTitles(before, config.IgnoredGames, now).ToList();
        var ignored = library.Where(e => e.State == LibraryState.Ignored && before.FirstOrDefault(b => b.Id == e.Id)?.State != LibraryState.Ignored).ToList();
        var stillSynced = library.Where(e => e.State == LibraryState.Synced && config.IgnoredGames.Any(t => Library.Names(e).Contains(SaveList.Normalize(t)))).ToList();
        Console.WriteLine($"Ludusavi's ignore list: {config.IgnoredGames.Count} games; {ignored.Count} {(apply ? "are now" : "would be")} ignored here too.");
        foreach (var entry in stillSynced)
        {
            Console.WriteLine($"  {entry.DisplayTitle} stays synced: you confirmed it in GameSync.");
        }

        // Games added by hand in Ludusavi, checked like any rule (R5, R8).
        var discoverer = new Discoverer(null, here.Folders, null, here.Guard, here.Registry);
        Console.WriteLine($"Games added by hand in Ludusavi: {config.CustomGames.Count}");
        foreach (var game in config.CustomGames)
        {
            var places = discoverer.FromLudusavi(game);
            var keys = discoverer.RegistryFromLudusavi(game);
            if (places.Count == 0 && keys.Count == 0)
            {
                Console.WriteLine($"  {game.Name}: nothing on this PC matches its paths.");
                continue;
            }

            var entry = Library.WithLudusaviGame(library, game, places, now, keys);
            var confirmed = Library.Confirm(entry with { State = entry.State == LibraryState.Ignored ? LibraryState.Found : entry.State });
            if (Problems(confirmed.Confirmed!, here.Resolver.Resolve(confirmed.Confirmed!), here) is [var problem, ..])
            {
                Console.WriteLine($"  {game.Name}: left out. {problem}");
                continue;
            }

            Console.WriteLine($"  {game.Name} {(apply ? "syncs" : "would sync")} as {confirmed.Id}, with:");
            foreach (var line in PortableRules.From(confirmed.Confirmed!).Describe())
            {
                Console.WriteLine($"      {line}");
            }

            library.RemoveAll(e => e.Id == confirmed.Id);
            library.Add(confirmed);
        }

        // Each game's latest backup, as a named save kept aside in its history.
        var imported = 0;
        var waiting = new List<string>();
        if (config.BackupFolder is { } folder && Directory.Exists(folder))
        {
            var problems = new List<string>();
            var sets = LudusaviReader.ReadBackups(folder, problems);

            // Every game with save rules: the ones syncing, and the ones confirmed above.
            var byName = new Dictionary<string, GameDefinition>(StringComparer.Ordinal);
            foreach (var stream in service.Streams.Where(s => !s.PerDevice))
            {
                byName.TryAdd(SaveList.Normalize(stream.Game.Title), stream.Game);
            }

            foreach (var entry in library.Where(e => e is { State: LibraryState.Synced, Confirmed: not null, MergedInto: null }))
            {
                var game = service.Streams.FirstOrDefault(s => !s.PerDevice && s.Game.Id == entry.Id)?.Game
                    ?? here.Resolver.Resolve(entry.Confirmed! with { Title = entry.DisplayTitle });
                foreach (var name in Library.Names(entry))
                {
                    byName.TryAdd(name, game);
                }
            }

            var ignoredNames = config.IgnoredGames.Select(SaveList.Normalize)
                .Concat(library.Where(e => e.State == LibraryState.Ignored).SelectMany(Library.Names))
                .ToHashSet(StringComparer.Ordinal);
            var saveList = new SaveListStore(here.DataDir).Load();
            var leftWithLudusavi = new List<string>();
            Console.WriteLine($"Ludusavi's backups in {folder}: {sets.Count} games");
            foreach (var set in sets)
            {
                var asHistory = false;
                if (!byName.TryGetValue(SaveList.Normalize(set.Title), out var game))
                {
                    if (ignoredNames.Contains(SaveList.Normalize(set.Title)))
                    {
                        leftWithLudusavi.Add(set.Title);
                        continue;
                    }

                    // A game no longer on this PC: the save list's places for it, as the backup holds them, keep it as history.
                    if (HistoryRules(set, saveList, library, here) is not { } rules)
                    {
                        waiting.Add(set.Title);
                        continue;
                    }

                    (game, asHistory) = (rules, true);
                }

                var report = await service.ImportLudusaviAsync(game, set, apply, ct);
                foreach (var item in report.Items)
                {
                    var what = item.SameAs is not null ? $"already in its history as {item.SameAs}" : apply ? "brought in" : "would be brought in";
                    var history = asHistory ? " as history (the game isn't synced on this PC)" : "";
                    Console.WriteLine($"  {set.Title}: '{item.Name}', {item.Files} files, {FormatSize(item.Bytes)}, {what}{history}");
                }

                foreach (var skipped in report.Skipped)
                {
                    Console.WriteLine($"  ! {skipped}");
                }

                imported += report.Added;
            }

            foreach (var problem in problems)
            {
                Console.WriteLine($"  ! {problem}");
            }

            if (leftWithLudusavi.Count > 0)
            {
                Console.WriteLine($"  Ignored, so their backups stay with Ludusavi: {string.Join(", ", leftWithLudusavi)}.");
            }

            if (waiting.Count > 0)
            {
                Console.WriteLine($"  No save rules for these yet, so their backups wait: {string.Join(", ", waiting)}.");
                Console.WriteLine("  Confirm the ones you want, then run this again; backups already brought in are skipped.");
            }
        }
        else
        {
            Console.WriteLine("Ludusavi's backup folder isn't there, so no backups were looked at.");
        }

        if (apply)
        {
            here.Library.SaveAll(library);
            Console.WriteLine($"Done. {imported} backups were brought in; Ludusavi's files weren't changed.");
        }
        else
        {
            Console.WriteLine("Nothing was changed yet. Add --apply to do exactly this; Ludusavi's own files are never changed.");
        }

        return 0;
    }

    /// <summary>
    /// Save rules for a game that's no longer on this PC, from the save list's places for it as the backup holds them,
    /// so its backup keeps the root keys a later scan gives it. Null when the list doesn't know it or it's zipped.
    /// </summary>
    private static GameDefinition? HistoryRules(LudusaviBackupSet set, SaveList? saveList, IReadOnlyList<LibraryEntry> library, ThisPc here)
    {
        if (saveList?.ByTitle(set.Title) is not { } listed || set.Latest is not { IsZip: false } latest)
        {
            return null;
        }

        var copy = set.CopyOf(latest);
        var inBackup = here.Folders
            .Select(f => (f.Key, Path: set.Locate(f.Value, copy)))
            .Where(f => f.Path is not null)
            .ToDictionary(f => f.Key, f => f.Path!, StringComparer.OrdinalIgnoreCase);
        var places = new Discoverer(null, inBackup).FromListOnly(listed);
        if (places.Count == 0)
        {
            return null;
        }

        var name = SaveList.Normalize(listed.Title);
        var id = library.FirstOrDefault(e => Library.Names(e).Contains(name))?.Id ?? Library.NewId(listed.Title, library.Select(e => e.Id));
        var definition = Discoverer.ToDefinition(id, listed.Title, places, listed.Cloud.Count > 0 ? GameMode.BackupOnly : GameMode.Sync);
        var resolved = here.Resolver.Resolve(definition);
        return Problems(definition, resolved, here).Count == 0 ? resolved : null;
    }

    // ---- settings and lookups ----

    private static LibraryEntry Entry(ThisPc here, GameId id) =>
        here.Library.All().FirstOrDefault(e => e.Id == id)
        ?? throw new UsageException($"There's no game '{id}' in the library. Find games with 'gamesync scan', and see them with 'gamesync library'.");

    private static IReadOnlyList<GameId> Games(List<string> rest, string what) =>
        rest.Count > 0 ? rest.Select(GameId.Parse).ToList() : throw new UsageException($"Missing the {what}. Run 'gamesync help'.");

    /// <summary>FOLD-07: the game folders to scan, such as E:\Games; this PC's own setting.</summary>
    private static List<string> GameFolders(StateStore state) =>
        JsonSerializer.Deserialize<List<string>>(state.GetSetting(GameFoldersKey) ?? "[]") ?? [];

    /// <summary>FOLD-08: extra save folders and folders named by ID; this PC's own setting, never in the repo or the cloud.</summary>
    internal static List<ExtraSaveFolder> SaveFolders(StateStore state) =>
        JsonSerializer.Deserialize<List<ExtraSaveFolder>>(state.GetSetting(SaveFoldersKey) ?? "[]", Json.Options) ?? [];

    /// <summary>A folder as given, made full and without a trailing '\' (a drive's root keeps its own, "G:\").</summary>
    private static string FullFolder(string folder) =>
        Path.IsPathFullyQualified(folder)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))
            : throw new UsageException("Give the full path of the folder, like E:\\Games.");
}
