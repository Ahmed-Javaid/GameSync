using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Sessions;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>Milestone 4's commands: the agent, launching, "I'm done playing", and the daily backup.</summary>
public static partial class Cli
{
    private const string DailyTimeKey = "daily.time";

    /// <summary>BG-02, BG-03: the daily backup; while the background app runs, it runs it instead. With --if-missed, only when the PC was off at the daily time (SET-02).</summary>
    internal static async Task<int> DailyAsync(string dataDir, List<string> rest, IAgentOutput output)
    {
        using (var state = new StateStore(dataDir))
        {
            if (rest.Contains("--if-missed") &&
                !(state.GetSetting(DailyTimeKey) is { Length: > 0 } time && Daily.TryParseTime(time, out var at) && Daily.Missed(Daily.Last(state)?.AtUtc, at, DateTime.Now)))
            {
                return 0;
            }

            if (EngineLock.AgentRunning(dataDir))
            {
                state.SetSetting(Agent.DailyRequestKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                output.Say("GameSync's background app runs the daily backup now.");
                return 0;
            }
        }

        await Daily.RunAsync(dataDir, output, isRunning: null, CancellationToken.None);
        return 0;
    }

    /// <summary>BG-01, BG-02, SET-02, SET-03: what's scheduled, and changing it.</summary>
    private static int ScheduleCommand(string dataDir, List<string> rest)
    {
        using var state = new StateStore(dataDir);
        switch (rest.Count == 0 ? "" : rest[0])
        {
            case "":
                var time = state.GetSetting(DailyTimeKey) is { Length: > 0 } at ? $"every day at {at}, and about 10 minutes after sign-in when the PC was off then" : "off";
                Console.WriteLine($"Daily backup:    {time}");
                Console.WriteLine($"GameSync app:    {(SignInStart.ForThisUser().IsOn || Schedule.Exists(Schedule.BackgroundTask) ? "starts at sign-in" : "doesn't start at sign-in")}{(EngineLock.AgentRunning(dataDir) ? ", running now" : "")}");
                Console.WriteLine(Daily.Last(state) is { } last
                    ? $"Last daily run:  {last.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}: {last.Games} games checked, {last.Uploads} uploaded{(last.NeedYou > 0 ? $", {last.NeedYou} need you" : "")}"
                    : "Last daily run:  none yet");
                return 0;

            case "daily" when Arg(rest, 1, "time, like 20:00, or off") == "off":
                Schedule.Remove(Schedule.DailyTask);
                Schedule.Remove(Schedule.CatchUpTask);
                state.SetSetting(DailyTimeKey, "");
                Console.WriteLine("The daily backup is off. Games still sync after you play, while the background app runs.");
                return 0;

            case "daily":
                if (!Daily.TryParseTime(rest[1], out var daily))
                {
                    throw new UsageException("Give the time as 20:00, or off.");
                }

                var program = BackgroundProgram();
                Schedule.Register(Schedule.DailyTask, Schedule.TaskXml("GameSync: backs up and syncs every game once a day.",
                    Schedule.DailyTrigger(daily, DateTime.Now), program, Schedule.Arguments("daily", dataDir), unlimited: false));
                Schedule.Register(Schedule.CatchUpTask, Schedule.TaskXml("GameSync: runs the daily backup after sign-in when the PC was off at its time.",
                    Schedule.SignInTrigger(Schedule.CatchUpDelay), program, Schedule.Arguments("daily --if-missed", dataDir), unlimited: false));
                state.SetSetting(DailyTimeKey, daily.ToString("HH:mm", CultureInfo.InvariantCulture));
                Console.WriteLine($"The daily backup runs at {daily:HH:mm}; when the PC is off then, about 10 minutes after the next sign-in.");
                return 0;

            case "background" when Arg(rest, 1, "on or off") == "on":
                SignInStart.ForThisUser().TurnOn(Schedule.SignInCommand(BackgroundProgram(), dataDir));
                Schedule.Remove(Schedule.BackgroundTask);
                Console.WriteLine("GameSync starts in the tray when you sign in; Task Manager's Startup apps lists it as GameSync. Start it now with: GameSync.Tray.exe");
                return 0;

            case "background" when rest[1] == "off":
                SignInStart.ForThisUser().TurnOff();
                Schedule.Remove(Schedule.BackgroundTask);
                Console.WriteLine("GameSync no longer starts when you sign in.");
                return 0;

            default:
                throw new UsageException("Try: gamesync schedule, schedule daily <20:00|off>, or schedule background <on|off>.");
        }
    }

    private static string BackgroundProgram() =>
        File.Exists(Schedule.BackgroundProgram)
            ? Schedule.BackgroundProgram
            : throw new UsageException($"GameSync.Tray.exe isn't next to gamesync.exe in {AppContext.BaseDirectory}.");

    /// <summary>The agent in this terminal (design.md → Background work); the background app runs the same with no window.</summary>
    private static async Task<int> RunAgentAsync(string dataDir)
    {
        using var agentLock = EngineLock.TryAcquireAgent(dataDir) ?? throw new UsageException("GameSync's agent is already running on this PC.");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        using var agent = new Agent(dataDir, new ConsoleAgentOutput());
        Console.WriteLine("GameSync is watching for games, and syncs each one after you play. Ctrl+C stops it.");
        await agent.RunAsync(stop.Token);
        return 0;
    }

    /// <summary>PLAY-06, "I'm done playing": ends a session a launcher or an updater keeps open.</summary>
    private static int Done(string dataDir, List<string> rest)
    {
        var game = Game(rest, 0);
        using var state = new StateStore(dataDir);
        if (state.GetSetting(RunningGames.OpenSessionKey(game)) is not { Length: > 0 } started)
        {
            Console.WriteLine($"{game} has no session open.");
            return 0;
        }

        if (EngineLock.AgentRunning(dataDir))
        {
            state.SetSetting(Agent.DoneKey(game), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Console.WriteLine($"Done playing {game}: its session ends now, and it syncs in a moment.");
            return 0;
        }

        // The agent was stopped mid-session, so nobody saw it end: it's recorded up to the last save written, and the
        // game syncs with the next sync.
        var start = DateTime.Parse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        state.AddSession(game, new SessionInfo(start, SaveActivity.LastWrite(SaveFoldersOf(dataDir, game), start, DateTime.UtcNow)));
        state.SetSetting(RunningGames.OpenSessionKey(game), "");
        Console.WriteLine($"Done playing {game}: the session is recorded. Sync it with: gamesync sync {game}");
        return 0;
    }

    /// <summary>How long a launch waits for a sync in the background to finish before the game starts without its check.</summary>
    private static readonly TimeSpan CheckPatience = TimeSpan.FromMinutes(2);

    /// <summary>
    /// PLAY-02, PLAY-03, PLAY-08: the pre-launch check, then the game's official route: Steam's or Epic's link, or a loose
    /// game's main program from its own folder, never as admin. After "--", the command Steam gives a launch option
    /// (<c>%command%</c>) runs instead, and it runs even when GameSync can't check first: GameSync never stands between
    /// you and the game. When the agent isn't running, this stays until the game closes and syncs it. A game found but
    /// not synced starts the same way, with nothing to check or sync.
    /// </summary>
    /// <param name="background">For the app and its jobs, which have no terminal: lines go to their log, warnings become notifications.</param>
    internal static async Task<int> LaunchAsync(string dataDir, List<string> rest, IAgentOutput? background = null)
    {
        var separator = rest.IndexOf("--");
        var command = separator >= 0 ? rest.Skip(separator + 1).ToList() : [];
        var game = Game(separator >= 0 ? rest.Take(separator).ToList() : rest, 0);
        var output = background ?? new LaunchConsole();
        var ct = CancellationToken.None;

        (string Title, GamePrograms? Programs, ProcessStartInfo Start, bool Syncs) Look()
        {
            using var engine = Engine.Open(dataDir);
            var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.State != LibraryState.Ignored && e.MergedInto is null);
            var definition = engine.Games.FirstOrDefault(g => g.Id == game);
            var title = definition?.Title ?? entry?.DisplayTitle
                ?? throw new UsageException($"There's no game '{game}' in the library. See them with 'gamesync library'.");
            var folder = engine.InstallDirs.GetValueOrDefault(game) ?? entry?.InstallDir;
            var programs = folder is not null && Directory.Exists(folder) ? GamePrograms.For(game, folder) : null;
            return (title, programs, command.Count > 0 ? Wrapped(command) : Route(title, entry, folder), definition is not null);
        }

        string title;
        GamePrograms? programs;
        ProcessStartInfo start;
        bool checkFirst;
        if (command.Count == 0)
        {
            if (IsElevated())
            {
                throw new UsageException("GameSync is running as administrator, so the game would too. Start GameSync normally and launch again.");
            }

            (title, programs, start, checkFirst) = Look();
            if (programs is not null && new ProcessWatcher().Find([programs]).Count > 0)
            {
                throw new UsageException($"{title} is already running.");
            }
        }
        else
        {
            // Steam's launch option: Steam already knows whether the game runs, and the game starts whatever GameSync's trouble.
            try
            {
                (title, programs, start, checkFirst) = Look();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                (title, programs, start, checkFirst) = (game.Value, null, Wrapped(command), false);
                output.NeedsYou(title, $"GameSync couldn't check the save before the game started: {e.Message}");
            }
        }

        if (checkFirst)
        {
            await CheckAsync(dataDir, game, title, output, ct);
        }

        // A store link hands the launch to the store's own process, so there may be no new process to hold.
        Process? launched;
        try
        {
            launched = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new UsageException($"{title} didn't start: {e.Message}");
        }

        using (launched)
        {
            if (launched is null && !start.UseShellExecute)
            {
                throw new InvalidOperationException($"{title} didn't start.");
            }

            if (!checkFirst || EngineLock.AgentRunning(dataDir))
            {
                output.Say(checkFirst ? $"Launched {title}. GameSync syncs it after you play." : $"Launched {title}.");
                return 0;
            }

            output.Say($"Launched {title}. GameSync isn't running in the background, so this waits and syncs {title} once you've played.");
            return await WatchAndSyncAsync(dataDir, game, title, programs, start.UseShellExecute ? null : launched, output, ct);
        }
    }

    /// <summary>
    /// PLAY-03, PLAY-07: the check before playing: a newer save from the cloud when that's safe, and what to know first.
    /// It holds the engine only for itself, never for the session, and whatever goes wrong, the game still starts.
    /// </summary>
    private static async Task CheckAsync(string dataDir, GameId game, string title, IAgentOutput output, CancellationToken ct)
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(CheckPatience);
        try
        {
            using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("GameSync is syncing in the background; the check waits for it..."), patience.Token);
            using var engine = Engine.Open(dataDir);
            var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = new RunningGames(engine).IsRunning }, ct);
            var check = await service.PrepareLaunchAsync(game, ct);
            if (check.Download is { } download)
            {
                output.Say($"{download.Title}: {download.Message}");
            }

            foreach (var warning in check.Warnings)
            {
                output.NeedsYou(title, warning);
            }
        }
        catch (OperationCanceledException) when (patience.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            output.NeedsYou(title, $"GameSync was still syncing after {CheckPatience.TotalMinutes:0} minutes, so {title} starts without its check; a newer save on another PC, if there is one, wasn't brought down.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            output.NeedsYou(title, $"The check before playing didn't finish, so {title} starts without it: {e.Message}");
        }
    }

    /// <summary>A launch in a terminal: lines as they come, and what needs you marked.</summary>
    private sealed class LaunchConsole : IAgentOutput
    {
        public void Say(string line) => Console.WriteLine(line);

        public void NeedsYou(string title, string message) => Console.WriteLine($"  ! {message}");
    }

    /// <summary>
    /// Without the agent: watches this one game until its session ends (PLAY-04), with the program it started counting
    /// too, then records the session and syncs the game.
    /// </summary>
    private static async Task<int> WatchAndSyncAsync(string dataDir, GameId game, string title, GamePrograms? programs, Process? started, IAgentOutput output,
        CancellationToken ct)
    {
        var tracker = new SessionTracker();
        var watcher = new ProcessWatcher();
        SaveActivity? activity = null;
        SessionInfo? session = null;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        try
        {
            while (session is null)
            {
                var now = DateTime.UtcNow;
                var running = programs is null ? new List<GameProcess>() : watcher.Find([programs]).ToList();
                if (started is { HasExited: false })
                {
                    running.Add(new GameProcess(game, started.Id, started.StartTime.ToUniversalTime()));
                }

                if (tracker.Playing.Count == 0 && now > deadline)
                {
                    output.Say($"{title} didn't show up running. Once you've played, sync it with: gamesync sync {game}");
                    return 1;
                }

                var writes = activity?.LastWriteUtc is { } last ? new Dictionary<GameId, DateTime> { [game] = last } : null;
                foreach (var change in tracker.Tick(now, running, writes))
                {
                    if (change is SessionStarted)
                    {
                        activity = new SaveActivity(SaveFoldersOf(dataDir, game));
                        await MarkAsync(dataDir, game, (SessionStarted)change, ct);
                    }
                    else if (change is SessionEnded ended)
                    {
                        session = ended.Session;
                    }
                }

                await Task.Delay(Agent.TickEvery, ct);
            }
        }
        finally
        {
            activity?.Dispose();
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        using var engine = Engine.Open(dataDir);
        engine.State.AddSession(game, session);
        var games = new RunningGames(engine);
        var (service, _) = await engine.OpenServiceAsync(new SyncOptions
        {
            Progress = output is LaunchConsole ? new ConsoleProgress(_ => title) : null,
            IsRunning = games.IsRunning,
        }, ct);
        var results = await service.SyncAsync([game], ct);
        await Markers.ClearAsync(engine, service, games.IsRunning, ct);
        output.Say($"Played {title} for {Math.Max(1, (int)Math.Round((session.EndUtc - session.StartUtc).TotalMinutes))} min:");
        if (output is LaunchConsole)
        {
            PrintResults(results);
        }
        else
        {
            Agent.Report(output, results);
        }

        return 0;
    }

    private static async Task MarkAsync(string dataDir, GameId game, SessionStarted started, CancellationToken ct)
    {
        try
        {
            await Markers.SetAsync(dataDir, game, started.StartUtc, ct);
        }
        catch (Exception e) when (e is Core.Storage.CloudException or IOException or InvalidOperationException or UsageException)
        {
            // The marker is a courtesy to the other PCs.
        }
    }

    private static IReadOnlyList<string> SaveFoldersOf(string dataDir, GameId game)
    {
        using var engine = Engine.Open(dataDir);
        return engine.Games.FirstOrDefault(g => g.Id == game)?.Roots.Values.Where(Directory.Exists).ToList() ?? [];
    }

    /// <summary>PLAY-02: the store's own link for store games, so their DRM and anti-cheat start normally; a loose game's main program otherwise.</summary>
    private static ProcessStartInfo Route(string title, LibraryEntry? entry, string? folder)
    {
        switch (entry)
        {
            case { Store: StoreKind.Steam, StoreId: { Length: > 0 } app }:
                return new ProcessStartInfo($"steam://rungameid/{app}") { UseShellExecute = true };
            case { Store: StoreKind.Epic, StoreId: { Length: > 0 } app }:
                return new ProcessStartInfo($"com.epicgames.launcher://apps/{Uri.EscapeDataString(app)}?action=launch&silent=true") { UseShellExecute = true };
        }

        if (folder is null || !Directory.Exists(folder))
        {
            throw new UsageException($"{title} has no install folder on this PC to start it from. Set one with: gamesync set install-dir <game> <folder>");
        }

        var exe = Fingerprinter.MainExe(folder) ?? throw new UsageException($"No program was found in {folder} to start {title} with.");
        return new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
    }

    /// <summary>PLAY-08: the command after "--", as Steam passes <c>%command%</c>, run as given from its own folder.</summary>
    private static ProcessStartInfo Wrapped(IReadOnlyList<string> command)
    {
        var start = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        if (Path.GetDirectoryName(Path.GetFullPath(command[0])) is { Length: > 0 } folder)
        {
            start.WorkingDirectory = folder;
        }

        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    /// <summary>PLAY-09: "   12 h 30 min played, last on 2026-09-28", from the game's sessions on this PC.</summary>
    private static string Playtime(IReadOnlyList<SessionInfo> sessions)
    {
        if (sessions.Count == 0)
        {
            return "";
        }

        var total = TimeSpan.FromTicks(sessions.Sum(s => (s.EndUtc - s.StartUtc).Ticks));
        var time = total.TotalHours >= 1 ? $"{(int)total.TotalHours} h {total.Minutes} min" : $"{Math.Max(1, (int)Math.Round(total.TotalMinutes))} min";
        return $"   {time} played, last on {sessions.Max(s => s.EndUtc).ToLocalTime():yyyy-MM-dd}";
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
