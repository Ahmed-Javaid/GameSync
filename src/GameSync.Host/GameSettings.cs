using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;

namespace GameSync.Host;

/// <summary>A file in one of a game's save places, as its Properties list it: ticked files are backed up and synced (FIND-12).</summary>
/// <param name="Path">Its path inside the place, with forward slashes.</param>
/// <param name="Why">Why it's out, when it is: "You left it out", "Skipped: a log, dump or cache", "A program file: never backed up".</param>
/// <param name="Locked">Never backed up whatever the person picks: a program file (R1).</param>
public sealed record SaveFileItem(string Path, long Bytes, bool Included, string? Why = null, bool Locked = false);

/// <summary>A place a game keeps saves, with the files the scan of it found, as its Properties list them (FIND-12).</summary>
/// <param name="Root">The rule's root key, or <c>registry:</c> and the key for a registry key.</param>
/// <param name="Shown">This PC's folder, or the registry key.</param>
/// <param name="Off">The whole place is left out.</param>
/// <param name="More">Files past the ones listed, and their size.</param>
/// <param name="Missing">The folder isn't on this PC now: an unplugged drive is never taken for an empty one.</param>
public sealed record SavePlaceFiles(string Root, string Shown, SaveCategory Category, bool IsRegistry, bool Off, IReadOnlyList<SaveFileItem> Files,
    int More = 0, long MoreBytes = 0, bool Missing = false);

/// <summary>What a game's Properties show and change (design system → GamePropertiesDialog; LIB-20, FIND-12).</summary>
public sealed record GameProperties
{
    public required GameId Id { get; init; }

    public required string Title { get; init; }

    public bool Favourite { get; init; }

    public bool Hidden { get; init; }

    /// <summary>Its saves sync: its file choices can change. A game not syncing yet shows what was found.</summary>
    public bool Syncs { get; init; }

    public bool Installed { get; init; }

    public string? InstallDir { get; init; }

    public GameAbout About { get; init; } = new();

    public GameMode Mode { get; init; }

    public ConflictPolicy Conflict { get; init; }

    /// <summary><c>sync</c>, <c>this-pc</c> or <c>off</c>: what happens to its settings files.</summary>
    public string SettingsFiles { get; init; } = GameSettings.ThisPc;

    public bool Screenshots { get; init; }

    public bool SkipDefaults { get; init; } = true;

    /// <summary>It has settings files (a place tagged Settings), so the choice for them means something.</summary>
    public bool HasSettingsFiles { get; init; }

    public IReadOnlyList<SavePlaceFiles> Places { get; init; } = [];
}

/// <summary>A file, or a whole place (an empty path), ticked or unticked in a game's Properties.</summary>
public sealed record FileChoice(string Root, string Path, bool Include);

/// <summary>What the person changed in a game's Properties; null leaves a setting as it is.</summary>
public sealed record GamePropertiesChange
{
    public string? Title { get; init; }

    public bool? Favourite { get; init; }

    public bool? Hidden { get; init; }

    public string? LaunchOptions { get; init; }

    public string? Program { get; init; }

    public GameMode? Mode { get; init; }

    public ConflictPolicy? Conflict { get; init; }

    public string? SettingsFiles { get; init; }

    public bool? Screenshots { get; init; }

    public bool? SkipDefaults { get; init; }

    public IReadOnlyList<FileChoice> Files { get; init; } = [];

    /// <summary>Stop syncing it on this PC: its versions stay in the cloud and here, and it can sync again later.</summary>
    public bool StopSyncing { get; init; }
}

/// <summary>
/// A game's Properties (LIB-20, FIND-12): read from this PC, and changed as the person saves them. File choices change
/// the game's own rules, which travel with its next version to its other PCs (R8); what's unticked stays on the PC and
/// in older versions, and is never deleted.
/// </summary>
public static class GameSettings
{
    public const string SyncBetween = "sync";
    public const string ThisPc = "this-pc";
    public const string Off = "off";

    /// <summary>A place lists at most this many files; the rest show as a count.</summary>
    public const int FilesShown = 200;

    /// <summary>A place's scan stops after this many files, so a huge folder never holds the dialog up.</summary>
    private const int FilesLooked = 5000;

    public static async Task<GameProperties?> ReadAsync(string dataDir, GameId game, long? steamAppId, CancellationToken ct)
    {
        var detail = await GameDetails.ReadAsync(dataDir, game, ct, steamAppId);
        if (detail is null)
        {
            return null;
        }

        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null);
        var resolved = engine.Games.FirstOrDefault(g => g.Id == game);
        var portable = entry?.Confirmed ?? engine.Config.Games.FirstOrDefault(g => g.Id == game);
        var shown = resolved ?? Found(entry, engine.Here.Resolver);
        var configRules = (portable?.Rules ?? []).Where(r => r.Category == SaveCategory.Config).ToList();
        return new GameProperties
        {
            Id = game,
            Title = resolved?.Title ?? entry?.DisplayTitle ?? game.Value,
            Favourite = engine.State.GetSetting(Launcher.FavouriteKey(game)) == "1",
            Hidden = engine.State.GetSetting(Launcher.HiddenKey(game)) == "1",
            Syncs = resolved is not null,
            Installed = entry?.Installed ?? true,
            InstallDir = detail.InstallDir,
            About = detail.About,
            Mode = portable?.Mode ?? (entry?.StoreCloud == true ? GameMode.BackupOnly : GameMode.Sync),
            Conflict = portable?.ConflictPolicy ?? ConflictPolicy.NewestWins,
            SettingsFiles = configRules.Count > 0 && configRules.All(r => r.Exclude.Contains("**")) ? Off : portable?.SyncConfig == true ? SyncBetween : ThisPc,
            Screenshots = portable?.IncludeScreenshots == true,
            SkipDefaults = portable is null || portable.Rules.All(r => r.UseDefaultExcludes || r.Include != "**"),
            HasSettingsFiles = (shown?.Rules ?? []).Any(r => r.Category == SaveCategory.Config),
            Places = shown is null ? [] : Places(shown),
        };
    }

    /// <summary>
    /// Saves what the person changed, under the engine lock so the agent isn't syncing the game meanwhile, and says what
    /// changed in a sentence.
    /// </summary>
    public static async Task<string> ApplyAsync(string dataDir, GameId game, GamePropertiesChange change, Action waiting, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, waiting, ct);
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null);
        var configGame = engine.Config.Games.FirstOrDefault(g => g.Id == game);
        if (entry is null && configGame is null)
        {
            throw new UsageException($"There's no game '{game}' on this PC any more.");
        }

        var title = entry?.DisplayTitle ?? configGame!.Title;
        var said = new List<string>();
        if (change.Favourite is { } favourite)
        {
            engine.State.SetSetting(Launcher.FavouriteKey(game), favourite ? "1" : "");
            said.Add(favourite ? "a favourite" : "not a favourite");
        }

        if (change.Hidden is { } hidden)
        {
            engine.State.SetSetting(Launcher.HiddenKey(game), hidden ? "1" : "");
            said.Add(hidden ? "hidden from the library" : "shown in the library");
        }

        if (change.LaunchOptions is { } options)
        {
            GameLaunch.SetOptions(engine.State, game, options);
            said.Add(options.Trim().Length == 0 ? "no launch options" : "new launch options");
        }

        if (change.Program is { } program)
        {
            GameLaunch.SetProgram(engine.State, game, program);
            said.Add("a new program to start");
        }

        var newTitle = change.Title?.Trim();
        if (newTitle is { Length: 0 })
        {
            throw new UsageException("A game needs a name.");
        }

        var portable = entry?.Confirmed ?? configGame;
        var rulesChange = change.Mode is not null || change.Conflict is not null || change.SettingsFiles is not null || change.Screenshots is not null ||
            change.SkipDefaults is not null || change.Files.Count > 0;
        if (rulesChange && portable is null)
        {
            throw new UsageException($"{title} isn't syncing yet: sync its saves first, then choose its files.");
        }

        var updated = portable is null ? null : Changed(portable, change);
        if (updated is not null && change.Files.Count > 0)
        {
            said.Add(Files(change.Files));
        }

        if (updated is not null && (change.Mode is not null || change.Conflict is not null || change.SettingsFiles is not null || change.Screenshots is not null || change.SkipDefaults is not null))
        {
            said.Add("new save settings");
        }

        if (entry is not null)
        {
            var next = entry with
            {
                TitleByHand = newTitle is not null && newTitle != entry.DisplayTitle ? newTitle : entry.TitleByHand,
                Confirmed = updated is not null ? updated with { Title = newTitle ?? updated.Title } : entry.Confirmed,
                State = change.StopSyncing ? LibraryState.Found : entry.State,
            };
            engine.Library.SaveAll([next]);
        }
        else if (configGame is not null)
        {
            if (change.StopSyncing)
            {
                throw new UsageException($"{title} was added by hand; remove it from games.json to stop syncing it.");
            }

            var next = (updated ?? configGame) with { Title = newTitle ?? configGame.Title };
            (engine.Config with { Games = engine.Config.Games.Select(g => g.Id == game ? next : g).ToList() }).Save(dataDir);
        }

        if (newTitle is not null && newTitle != title)
        {
            said.Insert(0, $"now called {newTitle}");
        }

        if (change.StopSyncing)
        {
            said.Add("not syncing any more; its versions stay in the cloud and on this PC");
        }

        return said.Count == 0 ? $"{title}: nothing changed." : $"{newTitle ?? title}: {string.Join(", ", said)}.";
    }

    /// <summary>The game's rules with the person's changes: file choices become excludes, or rules of their own for a file taken back in.</summary>
    public static GameDefinition Changed(GameDefinition game, GamePropertiesChange change)
    {
        var rules = game.Rules.ToList();
        var registry = game.Registry.ToList();
        foreach (var choice in change.Files)
        {
            if (choice.Root.StartsWith("registry:", StringComparison.Ordinal))
            {
                var key = choice.Root["registry:".Length..];
                if (!choice.Include)
                {
                    registry.RemoveAll(r => Core.Safety.RegistryGuard.SameKey(r.Key, key));
                }

                continue;
            }

            var at = rules.Select((r, i) => (Rule: r, Index: i)).Where(x => x.Rule.Root == choice.Root).ToList();
            if (at.Count == 0)
            {
                continue;
            }

            if (choice.Path.Length == 0)
            {
                // The whole place: left out as a whole, or taken back.
                foreach (var (rule, index) in at)
                {
                    rules[index] = rule with { Exclude = choice.Include ? rule.Exclude.Where(e => e != "**").ToList() : rule.Exclude.Append("**").Distinct().ToList() };
                }

                continue;
            }

            var path = choice.Path.Replace('\\', '/');
            if (!choice.Include)
            {
                // A file taken in by a rule of its own leaves with that rule; the wider rules that take it exclude it.
                rules.RemoveAll(r => r.Root == choice.Root && string.Equals(r.Include, path, StringComparison.OrdinalIgnoreCase) && r.Exclude.Count == 0);
                for (var i = 0; i < rules.Count; i++)
                {
                    if (rules[i].Root == choice.Root && new Glob(rules[i].Include).IsMatch(path))
                    {
                        rules[i] = rules[i] with { Exclude = rules[i].Exclude.Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
                    }
                }

                continue;
            }

            var taking = at.Where(x => new Glob(x.Rule.Include).IsMatch(path)).ToList();

            // Taken back in: its own exclude goes; if a wider pattern or the usual skips still leave it out, a rule of its own takes it.
            foreach (var (rule, index) in taking)
            {
                rules[index] = rule with { Exclude = rule.Exclude.Where(e => !string.Equals(e, path, StringComparison.OrdinalIgnoreCase)).ToList() };
            }

            if (!rules.Where(r => r.Root == choice.Root).Any(r => Takes(r, path)))
            {
                var category = taking.Select(x => x.Rule.Category).FirstOrDefault(at[0].Rule.Category);
                rules.Add(new SaveRule { Root = choice.Root, Include = path, Category = category, UseDefaultExcludes = false });
            }
        }

        if (change.SettingsFiles is { } settings)
        {
            rules = rules.Select(r => r.Category != SaveCategory.Config ? r
                : r with { Exclude = settings == Off ? r.Exclude.Append("**").Distinct().ToList() : r.Exclude.Where(e => e != "**").ToList() }).ToList();
        }

        if (change.SkipDefaults is { } skip)
        {
            rules = rules.Select(r => r.Include == "**" || r.Include.Contains('*') ? r with { UseDefaultExcludes = skip } : r).ToList();
        }

        return game with
        {
            Rules = rules,
            Registry = registry,
            Mode = change.Mode ?? game.Mode,
            ConflictPolicy = change.Conflict ?? game.ConflictPolicy,
            SyncConfig = change.SettingsFiles is { } s ? s == SyncBetween : game.SyncConfig,
            IncludeScreenshots = change.Screenshots ?? game.IncludeScreenshots,
        };
    }

    /// <summary>Whether a rule backs up a file at this path inside its root.</summary>
    public static bool Takes(SaveRule rule, string path) =>
        new Glob(rule.Include).IsMatch(path) && !rule.Exclude.Any(e => new Glob(e).IsMatch(path)) &&
        !(rule.UseDefaultExcludes && DefaultExcludes.All.Any(d => d.IsMatch(path)));

    /// <summary>Each place of a game's rules with the files there on this PC, ticked when they're backed up.</summary>
    public static IReadOnlyList<SavePlaceFiles> Places(GameDefinition game)
    {
        var places = new List<SavePlaceFiles>();
        foreach (var group in game.Rules.Where(r => r.Root != GameDefinition.RegistryRoot).GroupBy(r => r.Root))
        {
            var rules = group.ToList();
            var category = rules.Any(r => r.Category == SaveCategory.Save) ? SaveCategory.Save : rules[0].Category;
            var off = rules.All(r => r.Exclude.Contains("**"));
            var folder = game.Roots.GetValueOrDefault(group.Key);
            if (folder is null || RootResolver.IsUnresolved(folder) || !Directory.Exists(folder))
            {
                places.Add(new SavePlaceFiles(group.Key, folder is null || RootResolver.IsUnresolved(folder) ? group.Key : folder, category, false, off, [], Missing: true));
                continue;
            }

            var includes = rules.Select(r => (Rule: r, Include: new Glob(r.Include), Excludes: r.Exclude.Select(e => new Glob(e)).ToList())).ToList();
            var files = new List<SaveFileItem>();
            int more = 0, looked = 0;
            long moreBytes = 0;
            foreach (var file in Enumerate(folder))
            {
                if (++looked > FilesLooked)
                {
                    break;
                }

                var path = Path.GetRelativePath(folder, file.FullName).Replace('\\', '/');
                var taking = includes.Where(x => x.Include.IsMatch(path)).ToList();
                if (taking.Count == 0 || DefaultExcludes.Staged.IsMatch(path))
                {
                    continue;
                }

                SaveFileItem item;
                if (ProgramFileDetector.HasBlockedExtension(path))
                {
                    item = new SaveFileItem(path, file.Length, false, "A program file: never backed up", Locked: true);
                }
                else if (taking.Any(x => !x.Excludes.Any(e => e.IsMatch(path)) && !(x.Rule.UseDefaultExcludes && DefaultExcludes.All.Any(d => d.IsMatch(path)))))
                {
                    item = new SaveFileItem(path, file.Length, true);
                }
                else
                {
                    var byPerson = taking.Any(x => x.Excludes.Any(e => e.IsMatch(path) && e.Pattern != "**"));
                    item = new SaveFileItem(path, file.Length, false, off ? "The place is left out" : byPerson ? "You left it out" : "Skipped: a log, dump or cache");
                }

                if (files.Count < FilesShown)
                {
                    files.Add(item);
                }
                else
                {
                    more++;
                    moreBytes += file.Length;
                }
            }

            places.Add(new SavePlaceFiles(group.Key, folder, category, false, off, files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(), more, moreBytes));
        }

        places.AddRange(game.Registry.Select(r => new SavePlaceFiles($"registry:{r.Key}", r.Key.Replace('/', '\\'), r.Category, true, false, [])));
        return places;
    }

    /// <summary>The found places of a game not syncing yet, as rules resolved for this PC, to list what's there.</summary>
    private static GameDefinition? Found(LibraryEntry? entry, RootResolver resolver)
    {
        if (entry is null || entry.Proposals.Count + entry.RegistryProposals.Count == 0)
        {
            return null;
        }

        var definition = entry.Proposals.Count > 0 ? Discoverer.ToDefinition(entry.Id, entry.DisplayTitle, entry.Proposals, GameMode.Sync)
            : new GameDefinition { Id = entry.Id, Title = entry.DisplayTitle, Roots = new Dictionary<string, string>(), Rules = [] };
        return resolver.Resolve(definition with { Registry = entry.RegistryProposals.Select(p => new RegistryRule { Key = p.Key, Category = p.Category }).ToList() });
    }

    /// <summary>The files under a folder as they come, so a big one is cut short rather than read whole.</summary>
    private static IEnumerable<FileInfo> Enumerate(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Files(IReadOnlyList<FileChoice> choices)
    {
        var inCount = choices.Count(c => c.Include);
        var outCount = choices.Count - inCount;
        return (inCount, outCount) switch
        {
            (0, 1) => "1 file or folder left out",
            (0, var n) => $"{n} files or folders left out",
            (1, 0) => "1 file or folder backed up again",
            (var n, 0) => $"{n} files or folders backed up again",
            _ => $"{inCount} backed up again and {outCount} left out",
        };
    }
}
