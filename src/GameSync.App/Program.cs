using System.Globalization;
using GameSync.App;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Windows;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const string Usage = """
        GameSync (Milestone 1: the sync engine, with a folder standing in for Google Drive)

        gamesync [--data <folder>] <command> ...

        Setup
          init --remote <folder> [--name <this PC's name>]
          add-game <id> --title <title> --root <key>=<folder> [--root ...] [--include <pattern>]
                   [--mode sync|backup-only] [--policy newest-wins|always-ask|this-pc-wins]
          games

        Sync
          plan [<game>...]           what the next sync would do for each game, and why
          sync [<game>...]           shows the plan, then does exactly that
          backup <game>              backs up now; never downloads
          approve <game>             syncs changes held for review
          resolve <game> keep-this-pc|keep-cloud [<version>]
          swap <game>                switches to the save that lost the last conflict

        History
          history <game>
          restore <game> <version>   makes an old version current; your files are kept first
          pin <game> <version> [<label>]
          unpin <game> <version>
          thin <game> --keep <n> [--apply]   previews by default; pinned versions always stay

        Until the process watcher exists (Milestone 4)
          session <game> <start> <end>   records a play session, times in local time (2026-09-27T19:12)
          reinstalled <game>             treats the game's next sync as a first sync

          log [<game>]
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        var list = args.ToList();
        var dataDir = TakeOption(list, "--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSync");
        if (list.Count == 0 || list[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return list.Count == 0 ? 2 : 0;
        }

        var command = list[0];
        var rest = list.Skip(1).ToList();
        try
        {
            if (command == "init")
            {
                return Init(dataDir, rest);
            }

            var config = AppConfig.Load(dataDir)
                ?? throw new UsageException($"No games.json in {dataDir}. Run 'gamesync init --remote <folder>' first.");
            if (command == "add-game")
            {
                return AddGame(dataDir, config, rest);
            }

            using var state = new StateStore(dataDir);
            var device = state.GetOrCreateDevice(Environment.MachineName);
            var guard = SensitivePathGuard.ForThisPc(dataDir);
            foreach (var problem in config.Games.SelectMany(g => GameValidator.Problems(g, guard)))
            {
                throw new UsageException($"games.json: {problem}");
            }

            using var amsi = new AmsiScanner();
            var service = new SyncService(
                config.Games,
                new FolderBlobStore(config.Remote),
                new FolderVersionLog(config.Remote),
                state,
                new SnapshotScanner(guard, state),
                amsi,
                device,
                dataDir);

            // Anything a crash or reboot interrupted is finished before anything new starts (BAK-08, BAK-13).
            var recovered = await service.RecoverAsync(CancellationToken.None);
            if (recovered.Count > 0)
            {
                Console.WriteLine("Finished work that was interrupted last time:");
                PrintResults(recovered);
                Console.WriteLine();
            }

            return await RunCommandAsync(command, rest, service, state);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IOException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static async Task<int> RunCommandAsync(string command, List<string> rest, SyncService service, StateStore state)
    {
        var ct = CancellationToken.None;
        switch (command)
        {
            case "games":
                foreach (var stream in service.Streams)
                {
                    var s = state.GetState(stream.Id);
                    Console.WriteLine($"{stream.Id,-32} {s.Status?.ToString() ?? "Not synced yet",-14} {stream.Definition.Title}");
                    if (s.Detail is not null)
                    {
                        Console.WriteLine($"{"",-32} {s.Detail}");
                    }
                }

                return 0;

            case "plan":
                PrintPlans(await service.PlanAsync(Games(rest), ct));
                return 0;

            case "sync":
            {
                var plans = await service.PlanAsync(Games(rest), ct);
                Console.WriteLine("Plan:");
                PrintPlans(plans);
                Console.WriteLine();
                Console.WriteLine("Result:");
                PrintResults(await service.ExecuteAsync(plans, ct));
                return 0;
            }

            case "backup":
                PrintResults(await service.BackupNowAsync(Game(rest, 0), ct));
                return 0;

            case "approve":
                PrintResults([await service.ApproveAsync(Game(rest, 0), ct)]);
                return 0;

            case "resolve":
            {
                var choice = Arg(rest, 1, "choice: keep-this-pc or keep-cloud");
                if (choice is not ("keep-this-pc" or "keep-cloud"))
                {
                    throw new UsageException("Choose keep-this-pc or keep-cloud.");
                }

                var version = rest.Count > 2 ? VersionId.Parse(rest[2]) : (VersionId?)null;
                PrintResults([await service.ResolveAsync(Game(rest, 0), choice == "keep-this-pc", version, ct)]);
                return 0;
            }

            case "swap":
                PrintResults([await service.SwapAsync(Game(rest, 0), ct)]);
                return 0;

            case "history":
                foreach (var entry in await service.HistoryAsync(Game(rest, 0), ct))
                {
                    var v = entry.Version;
                    var flags = string.Join(' ', new[]
                    {
                        entry.IsCurrent ? "current" : null,
                        entry.IsBase ? "this-pc" : null,
                        entry.Pinned ? "pinned" : null,
                        v.Kind == VersionKind.Normal ? null : v.Kind.ToString().ToLowerInvariant(),
                    }.OfType<string>());
                    Console.WriteLine($"{v.Id.Value,-44} {v.Device.Name,-12} {v.Files.Count,5} files {FormatSize(FileSet.TotalSize(v.Files)),10}  {flags}");
                    if (entry.PinLabel is not null)
                    {
                        Console.WriteLine($"{"",-44} {entry.PinLabel}");
                    }
                }

                return 0;

            case "restore":
                PrintResults([await service.RestoreAsync(Game(rest, 0), VersionId.Parse(Arg(rest, 1, "version")), ct)]);
                return 0;

            case "pin":
                await service.PinAsync(Game(rest, 0), VersionId.Parse(Arg(rest, 1, "version")),
                    rest.Count > 2 ? string.Join(' ', rest.Skip(2)) : "Pinned by hand", ct);
                Console.WriteLine("Pinned. Thinning will never remove it.");
                return 0;

            case "unpin":
                await service.UnpinAsync(Game(rest, 0), VersionId.Parse(Arg(rest, 1, "version")), ct);
                Console.WriteLine("Unpinned.");
                return 0;

            case "thin":
            {
                var apply = rest.Remove("--apply");
                var keepText = TakeOption(rest, "--keep") ?? throw new UsageException("Say how many recent versions to keep: --keep <n>.");
                var keep = int.Parse(keepText, CultureInfo.InvariantCulture);
                var game = Game(rest, 0);
                var preview = await service.PreviewThinAsync(game, keep, ct);
                Console.WriteLine($"Thinning would remove {preview.Versions.Count} versions and free {FormatSize(preview.Bytes)}:");
                foreach (var v in preview.Versions)
                {
                    Console.WriteLine($"  {v.Id} ({v.Device.Name}, {v.Files.Count} files)");
                }

                if (apply)
                {
                    Console.WriteLine($"Removed {await service.ThinAsync(preview, keep, ct)} versions.");
                }
                else if (preview.Versions.Count > 0)
                {
                    Console.WriteLine("Nothing was removed. Add --apply to remove exactly these.");
                }

                return 0;
            }

            case "session":
            {
                var start = ParseLocalTime(Arg(rest, 1, "start time"));
                var end = ParseLocalTime(Arg(rest, 2, "end time"));
                state.AddSession(Game(rest, 0), new SessionInfo(start, end));
                Console.WriteLine("Session recorded.");
                return 0;
            }

            case "reinstalled":
                state.MarkReinstalled(Game(rest, 0));
                Console.WriteLine("The next sync of this game counts as a first sync: the cloud's save stays current.");
                return 0;

            case "log":
                foreach (var e in state.GetEvents(rest.Count > 0 ? GameId.Parse(rest[0]) : null, 50).Reverse())
                {
                    Console.WriteLine($"{e.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} {e.Level,-5} {e.Game,-24} {e.Message}");
                }

                return 0;

            default:
                throw new UsageException($"Unknown command '{command}'. Run 'gamesync help'.");
        }
    }

    private static int Init(string dataDir, List<string> rest)
    {
        var remote = TakeOption(rest, "--remote") ?? throw new UsageException("Say where the cloud folder is: --remote <folder>.");
        var name = TakeOption(rest, "--name");
        if (!Path.IsPathFullyQualified(remote))
        {
            throw new UsageException("--remote must be a full path.");
        }

        var existing = AppConfig.Load(dataDir);
        (existing is null ? new AppConfig { Remote = remote } : existing with { Remote = remote }).Save(dataDir);
        using var state = new StateStore(dataDir);
        var device = state.GetOrCreateDevice(name ?? Environment.MachineName);
        if (name is not null && device.Name != name)
        {
            state.RenameDevice(name);
        }

        Console.WriteLine($"Ready. This PC is '{name ?? device.Name}'; the cloud folder is {remote}.");
        return 0;
    }

    private static int AddGame(string dataDir, AppConfig config, List<string> rest)
    {
        var title = TakeOption(rest, "--title") ?? throw new UsageException("Give the game a title: --title <title>.");
        var include = TakeOption(rest, "--include") ?? "**";
        var mode = TakeOption(rest, "--mode") switch
        {
            null or "sync" => GameMode.Sync,
            "backup-only" => GameMode.BackupOnly,
            var other => throw new UsageException($"Unknown mode '{other}'."),
        };
        var policy = TakeOption(rest, "--policy") switch
        {
            null or "newest-wins" => ConflictPolicy.NewestWins,
            "always-ask" => ConflictPolicy.AlwaysAsk,
            "this-pc-wins" => ConflictPolicy.ThisPcWins,
            var other => throw new UsageException($"Unknown policy '{other}'."),
        };
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        while (TakeOption(rest, "--root") is { } root)
        {
            var parts = root.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new UsageException("Roots look like --root saves=C:\\Users\\you\\Documents\\Game.");
            }

            roots[parts[0]] = parts[1];
        }

        if (roots.Count == 0)
        {
            throw new UsageException("Add at least one save folder: --root <key>=<folder>.");
        }

        var game = new GameDefinition
        {
            Id = GameId.Parse(Arg(rest, 0, "game id")),
            Title = title,
            Mode = mode,
            ConflictPolicy = policy,
            Roots = roots,
            Rules = roots.Keys.Select(k => new SaveRule { Root = k, Include = include }).ToList(),
        };

        var problems = GameValidator.Problems(game, SensitivePathGuard.ForThisPc(dataDir));
        if (problems.Count > 0)
        {
            throw new UsageException(string.Join(Environment.NewLine, problems));
        }

        if (config.Games.Any(g => g.Id == game.Id))
        {
            throw new UsageException($"There's already a game '{game.Id}'.");
        }

        (config with { Games = [.. config.Games, game] }).Save(dataDir);
        Console.WriteLine($"Added {title}.");
        return 0;
    }

    private static void PrintPlans(IReadOnlyList<GamePlan> plans)
    {
        foreach (var plan in plans)
        {
            var action = plan.Error is not null ? "Problem" : Describe(plan.Decision!.Action);
            var reason = plan.Error?.Message ?? plan.Decision!.Reason;
            Console.WriteLine($"  {plan.Title,-36} {action,-16} {reason}");
            foreach (var warning in plan.Warnings)
            {
                Console.WriteLine($"  {"",-36} ! {warning}");
            }
        }
    }

    private static void PrintResults(IReadOnlyList<GameResult> results)
    {
        foreach (var result in results)
        {
            Console.WriteLine($"  {result.Title,-36} {result.Status,-16} {result.Message}");
            if (result.Notice is not null)
            {
                Console.WriteLine($"  {"",-36} {result.Notice}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  {"",-36} ! {warning}");
            }
        }
    }

    private static string Describe(SyncAction action) => action switch
    {
        SyncAction.None or SyncAction.AdoptHead => "Nothing to do",
        SyncAction.Upload => "Upload",
        SyncAction.UploadHeld => "Hold for review",
        SyncAction.Download => "Download",
        SyncAction.NeedsYou => "Needs you",
        SyncAction.Unavailable => "Not available",
        SyncAction.SavesMissing => "Saves missing",
        SyncAction.NoSaves => "No saves yet",
        _ => action.ToString(),
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    private static IReadOnlyCollection<GameId>? Games(List<string> rest) =>
        rest.Count == 0 ? null : rest.Select(GameId.Parse).ToList();

    private static GameId Game(List<string> rest, int index) => GameId.Parse(Arg(rest, index, "game id"));

    private static string Arg(List<string> rest, int index, string what) =>
        index < rest.Count ? rest[index] : throw new UsageException($"Missing the {what}. Run 'gamesync help'.");

    private static string? TakeOption(List<string> args, string name)
    {
        var at = args.IndexOf(name);
        if (at < 0)
        {
            return null;
        }

        if (at + 1 >= args.Count)
        {
            throw new UsageException($"{name} needs a value.");
        }

        var value = args[at + 1];
        args.RemoveRange(at, 2);
        return value;
    }

    private static DateTime ParseLocalTime(string text) =>
        DateTime.Parse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal).ToUniversalTime();
}

internal sealed class UsageException(string message) : Exception(message);
