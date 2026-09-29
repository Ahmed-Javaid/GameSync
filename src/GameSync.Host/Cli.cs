using System.Diagnostics;
using System.Globalization;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Storage.Drive;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>The command line's verbs, shared by <c>gamesync.exe</c> and the background app.</summary>
public static partial class Cli
{
    private const string Usage = """
        GameSync (Milestone 4: sessions, launching and the background app, from the command line)

        gamesync [--data <folder>] <command> ...

        Setup
          init --drive [--name <this PC's name>]       sync through your Google Drive
          init --remote <folder> [--name <name>]       or through a folder, such as a NAS or another drive
          signin [--client <file>]    signs in to Google Drive in your browser. The first time, give the
                                      OAuth client JSON you downloaded from Google Cloud.
          signout                     cancels GameSync's access at Google
          account                     where the cloud is, who's signed in, and how full it is
          games                       the games that sync, and how each one is doing
          devices                     every PC that syncs, its GameSync version and when it was last seen
          rename-device <name>

        Finding games and saves
          scan [--refresh-list]       finds installed games (Steam, Epic, EA and your game folders) and where
                                      each keeps its saves. Nothing syncs until you confirm it.
          library [--ignored]         what the last scan found, grouped by what's next
          show <game>                 where a game's saves were found and why, and the rules it syncs with
          confirm <game>... | --all   syncs the game with the places found. A game another PC already syncs
                                      takes up that PC's rules. Confirming again adds new places a scan found.
                  [--mine|--theirs]   use this PC's findings, or the rules another PC's newest save used
          ignore <game>...  /  unignore <game>...
          rename <game> <title>       the title GameSync shows; rescans keep it
          merge <game> <into>         makes two entries one game for good, like Spacewar into its real game
          folders                     the game folders scanned and the save folders you added
          add-folder <folder>  /  remove-folder <folder>
                                      game folders to scan, like E:\Games or G:\; each subfolder with a game
                                      program in it is a game
          add-save-folder <folder> [--by-id]  /  remove-save-folder <folder>
                                      extra places saves live; --by-id for a folder holding one folder per
                                      Steam app ID. These stay in this PC's settings.
          savelist update [--file <manifest.yaml>]
                                      refreshes the save list (the Ludusavi manifest) from GitHub, or a file
          import-ludusavi [--apply] [--config <file>]
                                      takes over Ludusavi's ignore list and the games you added to it, and
                                      brings each synced game's latest Ludusavi backup into its history as a
                                      named save. Previews by default; Ludusavi's files are never changed.
          add <name> <folder> [--program <exe>]
                                      a game or folder of your own, like a game server's world. It syncs when
                                      the program closes, or without one once the folder has been quiet for 5
                                      minutes. On your other PC, add its folder under the same name.
          add-game <id> --title <title> --root <key>=<folder> [--root ...] [--include <pattern>]
                   [--mode sync|backup-only] [--policy newest-wins|always-ask|this-pc-wins]
                   a game by hand, in games.json. A folder may start with <documents>, <publicDocuments>,
                   <roaming>, <localAppData>, <localLow>, <savedGames>, <home>, <programData>, <steamRoot>
                   or <installDir>, and may hold <steamUser> or <epicUser>.

        Settings on this PC
          set history-folder <folder>          moves the backup folder, checking every file before switching
          set history-keep all|recent          keep every version's files here, or the last 10 per game within 2 GB
          set install-dir <game> <folder>      where a game is installed on this PC, for <installDir>
          set account steamUser|epicUser <id>  this PC's account IDs, for <steamUser> and <epicUser>
          set anti-cheat <game> yes|no|auto    marks a game as shipping an anti-cheat, or not, or as found
          set surface glossy|solid             Glossy shows the last-played game's art through the app (in dark
                                               mode); Solid is the plain look. An open app repaints at once.

        Sync
          plan [<game>...]           what the next sync would do for each game, and why
          sync [<game>...]           shows the plan, then does exactly that
          backup <game>              backs up now; never downloads
          approve <game>             syncs changes held for review
          resolve <game> keep-this-pc|keep-cloud [<version>]
          swap <game>                switches to the save that lost the last conflict

        Named saves
          save <game> <name>         keeps the save as it is now under a name, like "Before Lady Maria"
          saves <game>               the game's named saves from every PC, newest first
          restore <game> <name>      brings a named save back; your current files are kept first
          rename-save <game> <name> <new name>
          forget-save <game> <name>  removes the name; the save stays in history
          import-saves <game> <folder> [--root <key>] [--apply]
                                     turns save folders you kept by hand (each holding a copy of the live
                                     save folder, or a .zip of one) into named saves. Previews by default.

        History
          history <game>
          restore <game> <version>   makes an old version current; your files are kept first
          pin <game> <version> [<label>]
          unpin <game> <version>
          thin <game> --keep <n> [--apply]   previews by default; pinned versions always stay

        Playing
          launch <game>              checks for a newer save on your other PCs, then starts the game the way its
                                     store does. With the background app off, this waits and syncs it after.
                                     A game found but not synced just starts.
          launch <game> -- <command> runs the command instead, as Steam's launch options give it: put
                                     "<folder>\GameSync.Tray.exe" launch <game> -- %command% there
          done <game>                ends a session a launcher keeps open after you quit ("I'm done playing")
          agent                      the background app in this window: notices games however they start,
                                     and syncs each after you play. Ctrl+C stops it.

        Background
          schedule                   what Windows runs for GameSync, and the last daily run
          schedule daily <20:00|off> the daily backup, and a catch-up after sign-in when the PC was off then
          schedule background on|off starts GameSync (GameSync.Tray.exe) in the tray when you sign in
          daily [--if-missed]        the daily backup now; the background app runs it when it's running
          art [--refresh]            each game's cover, hero and logo for the launcher: from Steam's own cache on
                                     this PC first, and from Steam's store only for new games and missing art
          hide <game>... / unhide <game>...
                                     hides games from Home and the game library on this PC; they sync as before
          favourite <game>... / unfavourite <game>...
                                     puts games first in the game library on this PC

        By hand
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
            switch (command)
            {
                case "init":
                    return Init(dataDir, rest);
                case "signin":
                    return await SignInAsync(dataDir, rest);
                case "signout":
                    return await SignOutAsync(dataDir);
                case "agent":
                    return await RunAgentAsync(dataDir);
                case "done":
                    return Done(dataDir, rest);
                case "launch":
                    return await LaunchAsync(dataDir, rest);
                case "daily":
                    return await DailyAsync(dataDir, rest, new ConsoleAgentOutput());
                case "schedule":
                    return ScheduleCommand(dataDir, rest);
                case "art":
                    return await ArtAsync(dataDir, rest);
                case "add":
                    return await AddOwnAsync(dataDir, rest);
            }

            if (command == "add-game")
            {
                return AddGame(dataDir, AppConfig.Load(dataDir) ?? throw new UsageException($"No games.json in {dataDir}. Run 'gamesync init' first."), rest);
            }

            if (command == "rename-device")
            {
                using var renaming = new StateStore(dataDir);
                renaming.GetOrCreateDevice(Environment.MachineName);
                renaming.RenameDevice(Arg(rest, 0, "new name"));
            }

            using var engine = Engine.Open(dataDir);
            var here = engine.Here;
            if (command == "set")
            {
                return Set(here, engine.Games, engine.InstallDirs.Values, rest);
            }

            if (await RunLibraryCommandAsync(command, rest, here) is { } done)
            {
                return done;
            }

            // BG-09: one engine touches saves at a time, so a command waits while the agent finishes a sync.
            using var engineLock = await EngineLock.AcquireAsync(dataDir,
                () => Console.WriteLine("GameSync is syncing in the background; this waits until it's done..."), CancellationToken.None);
            var running = new RunningGames(engine);
            var (service, recovered) = await engine.OpenServiceAsync(new SyncOptions
            {
                Progress = new ConsoleProgress(id => engine.Service?.Streams.FirstOrDefault(s => s.Id == id)?.Definition.Title),
                IsRunning = running.IsRunning,
            }, CancellationToken.None);

            // Anything a crash or reboot interrupted is finished before anything new starts (BAK-08, BAK-13).
            if (recovered.Count > 0)
            {
                Console.WriteLine("Finished work that was interrupted last time:");
                PrintResults(recovered);
                Console.WriteLine();
            }

            return await RunCommandAsync(command, rest, service, here, engine.Cloud!, engine.Config.UsesDrive);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (CloudException e)
        {
            Console.Error.WriteLine(e.Kind == CloudErrorKind.SignInExpired ? $"{e.Message} Run 'gamesync signin' to sign in again." : e.Message);
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static async Task<int> RunCommandAsync(string command, List<string> rest, SyncService service, ThisPc here, ICloud cloud, bool usesDrive)
    {
        var ct = CancellationToken.None;
        var state = here.State;
        if (command is "games" or "plan" or "sync")
        {
            PrintSkipped(here);
        }

        switch (command)
        {
            case "scan":
                return await ScanAsync(rest, service, here, ct);

            case "library":
                PrintLibrary(here.Library.All(), await service.OtherGamesAsync(ct), rest.Contains("--ignored"));
                return 0;

            case "show":
                return await ShowAsync(Game(rest, 0), service, here, ct);

            case "confirm":
                return await ConfirmAsync(rest, service, here, ct);

            case "import-ludusavi":
                return await ImportLudusaviAsync(rest, service, here, ct);

            case "games":
                foreach (var stream in service.Streams)
                {
                    var s = state.GetState(stream.Id);
                    var played = stream.PerDevice ? "" : Playtime(state.GetSessions(stream.Game.Id));
                    Console.WriteLine($"{stream.Id,-32} {s.Status?.ToString() ?? "Not synced yet",-14} {stream.Definition.Title}{played}");
                    if (s.Detail is not null)
                    {
                        Console.WriteLine($"{"",-32} {s.Detail}");
                    }

                    if (!stream.PerDevice && state.GetSetting($"moved.{stream.Game.Id}") is { Length: > 0 } moved)
                    {
                        Console.WriteLine($"{"",-32} ! Saves may have moved to {moved}. See: gamesync show {stream.Game.Id}");
                    }
                }

                return 0;

            case "account":
            {
                var info = await cloud.GetInfoAsync(ct);
                Console.WriteLine($"Cloud:    {info.Where}");
                if (info.Account is not null)
                {
                    Console.WriteLine($"Account:  {info.Account}");
                }

                if (info.UsedBytes is { } used && info.TotalBytes is { } total && total > 0)
                {
                    Console.WriteLine($"Storage:  {FormatSize(used)} of {FormatSize(total)} used ({used * 100 / total}%)");
                }

                if (usesDrive)
                {
                    Console.WriteLine("Access:   only the files GameSync made (the drive.file permission); the rest of your Drive is invisible to it.");
                }

                return 0;
            }

            case "devices":
            case "rename-device":
                foreach (var d in await service.DevicesAsync(ct))
                {
                    var thisPc = d.Id == service.Device.Id ? "(this PC)" : "";
                    Console.WriteLine($"{d.Name,-16} {thisPc,-10} last seen {d.LastSeenUtc.ToLocalTime():yyyy-MM-dd HH:mm}   GameSync {d.AppVersion}");
                }

                if (command == "rename-device")
                {
                    Console.WriteLine($"This PC is now '{service.Device.Name}'. Other PCs show the new name after their next sync.");
                }

                return 0;

            case "plan":
            {
                var plans = await service.PlanAsync(Games(rest), ct);
                PrintNotices(service);
                PrintPlans(plans);
                return 0;
            }

            case "sync":
            {
                var plans = await service.PlanAsync(Games(rest), ct);
                PrintNotices(service);
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
                        entry.Uploaded ? null : "not-uploaded-yet",
                    }.OfType<string>());
                    Console.WriteLine($"{v.Id.Value,-44} {entry.DeviceName,-12} {v.Files.Count,5} files {FormatSize(FileSet.TotalSize(v.Files)),10}  {flags}");
                    if (entry.PinLabel is not null)
                    {
                        Console.WriteLine($"{"",-44} {entry.PinLabel}");
                    }
                }

                return 0;

            case "restore":
            {
                var game = Game(rest, 0);
                var what = string.Join(' ', rest.Skip(1));
                if (what.Length == 0)
                {
                    throw new UsageException("Say which save: a name from 'gamesync saves', or a version id from 'gamesync history'.");
                }

                var isVersion = VersionId.TryParse(what, out var version) && (await service.HistoryAsync(game, ct)).Any(h => h.Version.Id == version);
                PrintResults([isVersion ? await service.RestoreAsync(game, version, ct) : await service.RestoreNamedAsync(game, what, ct)]);
                return 0;
            }

            case "save":
            {
                var name = string.Join(' ', rest.Skip(1));
                if (name.Length == 0)
                {
                    throw new UsageException("Give the save a name: gamesync save <game> \"Before Lady Maria\".");
                }

                PrintResults([await service.SaveAsAsync(Game(rest, 0), name, ct)]);
                return 0;
            }

            case "saves":
            {
                var game = Game(rest, 0);
                var saves = await service.NamedSavesAsync(game, ct);
                if (saves.Count == 0)
                {
                    Console.WriteLine($"No named saves yet. Make one with: gamesync save {game} \"<name>\"");
                    return 0;
                }

                foreach (var save in saves)
                {
                    var note = save.Uploaded ? "" : "  not uploaded yet";
                    Console.WriteLine($"  {save.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {save.Name,-44} {save.DeviceName,-12} {FormatSize(FileSet.TotalSize(save.Version.Files)),9}{note}");
                }

                Console.WriteLine($"Bring one back with: gamesync restore {game} \"<name>\"");
                return 0;
            }

            case "rename-save":
                await service.RenameSaveAsync(Game(rest, 0), Arg(rest, 1, "save's name"), Arg(rest, 2, "new name"), ct);
                Console.WriteLine("Renamed.");
                return 0;

            case "forget-save":
                await service.ForgetSaveAsync(Game(rest, 0), string.Join(' ', rest.Skip(1)), ct);
                Console.WriteLine("The name is gone. The save itself stays in history.");
                return 0;

            case "import-saves":
            {
                var apply = rest.Remove("--apply");
                var root = TakeOption(rest, "--root");
                var game = Game(rest, 0);
                var folder = Path.GetFullPath(Arg(rest, 1, "folder"));
                var report = await service.ImportSavesAsync(game, folder, apply, root, ct);
                foreach (var item in report.Items.OrderBy(i => i.SavedUtc))
                {
                    var what = item.SameAs is null ? "" : $"  same files as {item.SameAs}";
                    Console.WriteLine($"  {item.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {item.Name,-44} {item.Files,4} files {FormatSize(item.Bytes),9}{what}");
                }

                foreach (var skipped in report.Skipped)
                {
                    Console.WriteLine($"  ! {skipped}");
                }

                Console.WriteLine(apply
                    ? $"Imported {report.Added} named saves. Your folders weren't changed; list the saves with: gamesync saves {game}"
                    : $"Found {report.Items.Count} kept saves. Nothing was imported yet; add --apply to import them. Your folders are never changed.");
                return 0;
            }

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
        var drive = rest.Remove("--drive");
        var remote = TakeOption(rest, "--remote");
        var name = TakeOption(rest, "--name");
        if (drive == (remote is not null))
        {
            throw new UsageException("Say where the cloud is: --drive for Google Drive, or --remote <folder>.");
        }

        if (remote is not null)
        {
            if (!Path.IsPathFullyQualified(remote))
            {
                throw new UsageException("--remote must be a full path.");
            }

            Directory.CreateDirectory(remote);
        }

        var value = drive ? "drive" : remote!;
        var existing = AppConfig.Load(dataDir);
        (existing is null ? new AppConfig { Remote = value } : existing with { Remote = value }).Save(dataDir);
        using var state = new StateStore(dataDir);
        var device = state.GetOrCreateDevice(name ?? Environment.MachineName);
        if (name is not null && device.Name != name)
        {
            state.RenameDevice(name);
        }

        Console.WriteLine(drive
            ? $"Ready. This PC is '{name ?? device.Name}'. Next, sign in to Google Drive: gamesync signin --client <the JSON from Google Cloud>"
            : $"Ready. This PC is '{name ?? device.Name}'; the cloud folder is {remote}.");
        return 0;
    }

    private static async Task<int> SignInAsync(string dataDir, List<string> rest)
    {
        if (TakeOption(rest, "--client") is { } clientFile)
        {
            if (!File.Exists(clientFile))
            {
                throw new UsageException(
                    $"There's no file at {Path.GetFullPath(clientFile)}. The one Google Cloud gave you is named like client_secret_<numbers>.apps.googleusercontent.com.json; dragging it into this window pastes its full path.");
            }

            var bytes = await File.ReadAllBytesAsync(clientFile);
            GoogleClient.Parse(bytes);
            Directory.CreateDirectory(dataDir);
            await File.WriteAllBytesAsync(ClientFile(dataDir), bytes);
        }

        var auth = Auth(dataDir)
            ?? throw new UsageException("The first sign-in needs GameSync's Google Cloud client: gamesync signin --client <the JSON you downloaded>.");
        Console.WriteLine("Opening Google's sign-in page in your browser. GameSync asks only for the files it makes in your Drive.");
        await auth.SignInAsync(url =>
        {
            Console.WriteLine($"If the browser doesn't open, paste this into it: {url.AbsoluteUri}");
            OpenBrowser(url);
        }, TimeSpan.FromMinutes(5), CancellationToken.None);

        using var drive = new GoogleDriveClient(auth);
        var about = await drive.AboutAsync(CancellationToken.None);
        Console.WriteLine(about.Email is null ? "Signed in." : $"Signed in as {about.Email}.");
        return 0;
    }

    private static async Task<int> SignOutAsync(string dataDir)
    {
        if (Auth(dataDir) is not { IsSignedIn: true } auth)
        {
            Console.WriteLine("GameSync isn't signed in to Google Drive.");
            return 0;
        }

        await auth.SignOutAsync(CancellationToken.None);
        Console.WriteLine("Signed out: Google no longer lets GameSync into your Drive. Your saves there stay where they are.");
        return 0;
    }

    internal static GoogleDriveClient OpenDrive(string dataDir)
    {
        var auth = Auth(dataDir)
            ?? throw new UsageException("GameSync needs its Google Cloud client first: gamesync signin --client <the JSON you downloaded>.");
        return auth.IsSignedIn
            ? new GoogleDriveClient(auth)
            : throw new UsageException("GameSync isn't signed in to Google Drive. Run 'gamesync signin'.");
    }

    private static GoogleAuth? Auth(string dataDir) =>
        File.Exists(ClientFile(dataDir))
            ? new GoogleAuth(GoogleClient.Parse(File.ReadAllBytes(ClientFile(dataDir))), Path.Combine(dataDir, "google-token.bin"), new DpapiProtector())
            : null;

    private static string ClientFile(string dataDir) => Path.Combine(dataDir, "google-client.json");

    private static void OpenBrowser(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The link is printed above.
        }
    }

    private static int Set(ThisPc here, IReadOnlyList<GameDefinition> games, IEnumerable<string> installDirs, List<string> rest)
    {
        var (dataDir, state) = (here.DataDir, here.State);
        switch (Arg(rest, 0, "setting"))
        {
            case "history-folder":
                return MoveHistory(dataDir, state, games, installDirs, Path.GetFullPath(Arg(rest, 1, "folder")));

            case "anti-cheat":
            {
                var entry = Entry(here, Game(rest, 1));
                bool? value = Arg(rest, 2, "yes, no or auto") switch
                {
                    "yes" => true,
                    "no" => false,
                    "auto" => null,
                    _ => throw new UsageException("Choose yes, no or auto."),
                };
                here.Library.SaveAll([entry with { AntiCheatByHand = value }]);
                Console.WriteLine(value switch
                {
                    true => $"{entry.DisplayTitle} is marked as shipping an anti-cheat: official launch only, and no learn mode.",
                    false => $"{entry.DisplayTitle} is marked as having no anti-cheat.",
                    null => $"{entry.DisplayTitle} goes by what its folder shows: {entry.AntiCheat ?? "no anti-cheat"}.",
                });
                return 0;
            }

            case "history-keep":
                var keep = Arg(rest, 1, "all or recent");
                if (keep is not ("all" or "recent"))
                {
                    throw new UsageException("Choose all or recent.");
                }

                state.SetSetting("history.keep", keep);
                Console.WriteLine(keep == "all"
                    ? "This PC now keeps every version's files. The cloud keeps everything either way."
                    : "This PC keeps the files of the last 10 versions per game, within 2 GB. The cloud keeps everything.");
                return 0;

            case "install-dir":
            {
                var game = GameId.Parse(Arg(rest, 1, "game id"));
                var folder = Arg(rest, 2, "install folder");
                if (!Path.IsPathFullyQualified(folder))
                {
                    throw new UsageException("The install folder must be a full path.");
                }

                state.SetSetting($"installDir.{game}", Path.GetFullPath(folder));
                Console.WriteLine($"<installDir> for {game} is {Path.GetFullPath(folder)} on this PC.");
                return 0;
            }

            case "account":
            {
                var key = Arg(rest, 1, "steamUser or epicUser");
                var id = Arg(rest, 2, "account id");
                if (key is not ("steamUser" or "epicUser"))
                {
                    throw new UsageException("Set steamUser or epicUser.");
                }

                if (!id.All(char.IsAsciiLetterOrDigit))
                {
                    throw new UsageException("An account id is letters and digits only.");
                }

                state.SetSetting($"account.{key}", id);
                Console.WriteLine($"<{key}> is {id} on this PC.");
                return 0;
            }

            case "surface":
            {
                var surface = Arg(rest, 1, "glossy or solid");
                if (surface is not ("glossy" or "solid"))
                {
                    throw new UsageException("Choose glossy or solid.");
                }

                state.SetSetting("look.surface", surface);
                var told = AppPipe.SendAsync(dataDir, "look", TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult() == "ok";
                Console.WriteLine((surface == "glossy"
                    ? "GameSync shows each page over the last-played game's art on this PC, in dark mode with Windows' transparency effects on."
                    : "GameSync uses the plain look on this PC.") + (told ? " The open app has repainted." : ""));
                return 0;
            }

            default:
                throw new UsageException("Settings: history-folder, history-keep, install-dir, account, anti-cheat, surface. Run 'gamesync help'.");
        }
    }

    /// <summary>FOLD-02 to FOLD-04: copies and checks everything first; if anything fails, the old folder stays in use.</summary>
    private static int MoveHistory(string dataDir, StateStore state, IReadOnlyList<GameDefinition> games, IEnumerable<string> installDirs, string target)
    {
        var current = Path.GetFullPath(HistoryFolder(state, dataDir));
        var from = Path.TrimEndingDirectorySeparator(current);
        var to = Path.TrimEndingDirectorySeparator(target);
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"The backup folder is already {target}.");
            return 0;
        }

        if (to.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            from.StartsWith(to + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The new backup folder can't be inside the current one, or the other way round.");
        }

        if (BackupFolderGuard.ForThisPc().Check(target, games, installDirs) is { } refusal)
        {
            throw new UsageException(refusal);
        }

        if (Path.GetPathRoot(target) is { } drive && !Directory.Exists(drive))
        {
            throw new UsageException($"Drive {drive.TrimEnd('\\')} isn't connected.");
        }

        int skipped;
        try
        {
            skipped = Directory.Exists(current) ? new LocalHistory(current).CopyTo(target) : 0;
            Directory.CreateDirectory(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Couldn't move the backup folder: {e.Message} GameSync keeps using {current}.");
        }

        state.SetSetting("history.folder", target);
        LocalHistory.TryDelete(current);
        Console.WriteLine($"Moved the backup folder to {target}. Every file was checked there before the old folder was removed.");
        if (skipped > 0)
        {
            Console.WriteLine($"{skipped} damaged files stayed behind; the cloud still has them.");
        }

        return 0;
    }

    /// <summary>LIB-13: <c>add &lt;name&gt; &lt;folder&gt; [--program &lt;exe&gt;]</c>, a game or folder of your own, as the app's Add a game or folder.</summary>
    private static async Task<int> AddOwnAsync(string dataDir, List<string> rest)
    {
        var program = TakeOption(rest, "--program");
        var added = await OwnGames.AddAsync(dataDir, new OwnGame(Arg(rest, 0, "name"), Arg(rest, 1, "folder"), program), new ConsoleAgentOutput(), CancellationToken.None);
        Console.WriteLine(added.Sentence);
        if (File.Exists(AppConfig.PathIn(dataDir)))
        {
            Console.WriteLine($"Back it up now with: gamesync backup {added.Id}");
        }

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
                throw new UsageException("Roots look like --root saves=<documents>/My Games/Terraria or --root saves=D:\\Games\\Saves.");
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

    private static void PrintNotices(SyncService service)
    {
        foreach (var notice in service.Notices)
        {
            Console.WriteLine($"! {notice}");
        }

        if (service.Notices.Count > 0)
        {
            Console.WriteLine();
        }
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
        SyncAction.WaitForCloud => "Waits for cloud",
        _ => action.ToString(),
    };

    public static string FormatSize(long bytes) => bytes switch
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

    internal static string? TakeOption(List<string> args, string name)
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

    internal static string AppVersion =>
        typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "dev";

    /// <summary>FOLD-02: the backup folder, by default next to the rest of GameSync's data.</summary>
    internal static string HistoryFolder(StateStore state, string dataDir) =>
        state.GetSetting("history.folder") ?? Path.Combine(dataDir, "history");

    /// <summary>FOLD-06: the last 10 versions per game within 2 GB, unless the user chose to keep everything.</summary>
    internal static HistoryLimits KeepLimits(StateStore state) =>
        state.GetSetting("history.keep") == "all" ? HistoryLimits.Everything : new HistoryLimits();

    /// <summary>PC-03: this PC's account IDs, set with 'gamesync set account'.</summary>
    internal static Dictionary<string, string> Accounts(StateStore state)
    {
        var accounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in RootResolver.AccountPlaceholders.Select(p => p.Trim('<', '>')))
        {
            if (state.GetSetting($"account.{key}") is { Length: > 0 } value)
            {
                accounts[key] = value;
            }
        }

        return accounts;
    }

    /// <summary>
    /// Where each game is installed on this PC, for &lt;installDir&gt;: where the last scan found it, unless one was set
    /// with 'gamesync set install-dir'.
    /// </summary>
    internal static Dictionary<GameId, string> InstallDirs(StateStore state, AppConfig config, IReadOnlyList<LibraryEntry> library)
    {
        var dirs = new Dictionary<GameId, string>();
        foreach (var entry in library.Where(e => e.Installed && e.InstallDir is { Length: > 0 }))
        {
            dirs[entry.Id] = entry.InstallDir!;
        }

        foreach (var id in config.Games.Select(g => g.Id).Concat(library.Select(e => e.Id)))
        {
            if (state.GetSetting($"installDir.{id}") is { Length: > 0 } folder)
            {
                dirs[id] = folder;
            }
        }

        return dirs;
    }
}

/// <summary>CLOUD-09: a line per big upload now and then, and when it finishes.</summary>
internal sealed class ConsoleProgress(Func<GameId, string?> titleOf) : IProgress<TransferProgress>
{
    private const long Big = 20L * 1024 * 1024;
    private readonly Lock _gate = new();
    private DateTime _last;

    public void Report(TransferProgress value)
    {
        if (value.BytesTotal < Big && value.FilesTotal < 100)
        {
            return;
        }

        lock (_gate)
        {
            var done = value.FilesDone == value.FilesTotal;
            if (!done && value.FilesDone > 0 && DateTime.UtcNow - _last < TimeSpan.FromSeconds(3))
            {
                return;
            }

            _last = DateTime.UtcNow;
            Console.WriteLine($"  Uploading {titleOf(value.Game) ?? value.Game.Value}: {value.FilesDone} of {value.FilesTotal} files, " +
                $"{Cli.FormatSize(value.BytesDone)} of {Cli.FormatSize(value.BytesTotal)}");
        }
    }
}

public sealed class UsageException(string message) : Exception(message);
