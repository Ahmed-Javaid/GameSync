using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Storage.Drive;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>A save folder the person added (FOLD-08): as every PC reads it, where it is here, and whether its subfolders are Steam app IDs.</summary>
/// <param name="Full">Where it is on this PC; null when this PC can't place it.</param>
public sealed record SaveFolderSummary(string Path, string? Full, bool ById, bool Missing);

/// <summary>A PC that syncs with this cloud (PC-01): its name, when it was last seen and its GameSync; this PC has no last seen.</summary>
public sealed record PcSummary(string Name, DateTime? LastSeenUtc, string? AppVersion, bool ThisPc);

/// <summary>SET-03: the last daily backup: when, how many games it checked and uploaded, and how many need the person.</summary>
public sealed record DailySummary(DateTime AtUtc, int Games, int Uploads, int NeedYou);

/// <summary>A game with an anti-cheat (ONB-05): no learn mode and no sharing, and it launches only through its own launcher.</summary>
public sealed record AntiCheatGame(GameId Id, string Title, string AntiCheat);

/// <summary>The cloud as Settings shows it: a link or folder to open, the signed-in account, and how full it is (CLOUD-05, CLOUD-08).</summary>
public sealed record CloudDetails(string Where, string? Account, long? UsedBytes, long? TotalBytes);

/// <summary>What the backup folder holds (FOLD-10): its size on the drive, the versions it keeps, and the drive's free space.</summary>
public sealed record BackupFigures(long Bytes, int Versions, string Drive, long? FreeBytes);

/// <summary>Every setting on this PC, as Settings shows them (SET-01), read from this PC alone so the page opens at once.</summary>
public sealed record SettingsView
{
    public required string BackupFolder { get; init; }

    /// <summary>FOLD-05: why backups are paused (the backup folder's drive isn't connected); null when they aren't.</summary>
    public string? BackupMissing { get; init; }

    /// <summary>FOLD-06: every version's files stay on this PC, rather than the last 10 per game within 2 GB.</summary>
    public bool KeepEverything { get; init; }

    public IReadOnlyList<GameFolderSummary> GameFolders { get; init; } = [];

    public IReadOnlyList<SaveFolderSummary> SaveFolders { get; init; } = [];

    /// <summary>BG-01: GameSync starts in the tray when the person signs in.</summary>
    public bool StartsAtSignIn { get; init; }

    /// <summary>SET-02: the daily backup's time; null when it's off.</summary>
    public TimeOnly? DailyAt { get; init; }

    public DailySummary? LastDaily { get; init; }

    /// <summary>Run now was pressed and the agent hasn't started it yet.</summary>
    public bool DailyAsked { get; init; }

    /// <summary>
    /// This is GameSync's usual data folder, the only one whose choices change what Windows starts; a test copy on
    /// another one shows them but says it can't change them.
    /// </summary>
    public bool UsualDataFolder { get; init; }

    public GameDefaults Defaults { get; init; } = new();

    /// <summary>Games that sync or are backed up, which a change to the defaults can be applied to.</summary>
    public int Games { get; init; }

    /// <summary>Of those, the ones whose files don't follow the defaults (SET-06), and those whose conflict choice doesn't.</summary>
    public int FilesDiffer { get; init; }

    public int ConflictDiffer { get; init; }

    /// <summary>"drive", "folder" or "none" (Skip for now, ONB-06).</summary>
    public string Cloud { get; init; } = SettingsData.NoCloud;

    /// <summary>The folder every PC points at, for a folder cloud.</summary>
    public string? CloudFolder { get; init; }

    /// <summary>Signed in to Google Drive, for a Drive cloud.</summary>
    public bool SignedIn { get; init; }

    /// <summary>This copy of GameSync carries a Google client, so it can sign in (CLOUD-12).</summary>
    public bool CanSignIn { get; init; }

    public required string ThisPc { get; init; }

    /// <summary>This PC first, then the others that sync with this cloud, the most recently seen first (PC-01).</summary>
    public IReadOnlyList<PcSummary> Pcs { get; init; } = [];

    public required string AppVersion { get; init; }

    /// <summary>BG-06: notifications wait while a fullscreen game runs.</summary>
    public bool HoldWhileFullscreen { get; init; } = true;

    /// <summary>BG-05: one line after the daily backup, saying what it checked and changed.</summary>
    public bool DailyNote { get; init; }

    public IReadOnlyList<AntiCheatGame> AntiCheat { get; init; } = [];
}

/// <summary>
/// Settings (SET-01 to SET-06): reads every setting on this PC and changes them, as the command line's <c>set</c>,
/// <c>schedule</c>, <c>add-folder</c> and the rest do. Everything here stays on this PC; nothing is synced but a PC's
/// own name, which its next sync tells the others.
/// </summary>
public static class SettingsData
{
    public const string Drive = "drive";
    public const string Folder = "folder";
    public const string NoCloud = "none";

    public const string HoldKey = "notify.holdFullscreen";
    public const string DailyNoteKey = "notify.dailySummary";

    /// <summary>A PC's name is at most this long (PC-02).</summary>
    public const int NameLength = 32;

    public static SettingsView Read(string dataDir)
    {
        using var engine = Engine.Open(dataDir);
        var state = engine.State;
        var history = new LocalHistory(Cli.HistoryFolder(state, dataDir));
        var library = engine.Library.All().Where(e => e.MergedInto is null).ToList();
        var defaults = GameDefaults.Load(state);
        var syncing = library.Where(e => e is { State: LibraryState.Synced, Confirmed: not null }).Select(e => e.Confirmed!).Concat(engine.Config.Games).ToList();
        var resolver = new Discoverer(null, engine.Here.Folders);
        var others = history.LoadDevices().Where(d => d.Id != engine.Device.Id).OrderByDescending(d => d.LastSeenUtc);
        var usual = Usual(dataDir);
        return new SettingsView
        {
            BackupFolder = history.Folder,
            BackupMissing = history.UnavailableReason(),
            KeepEverything = state.GetSetting("history.keep") == "all",
            GameFolders = Cli.GameFoldersOf(state)
                .Select(f => new GameFolderSummary(f, library.Count(e => e is { Installed: true, State: not LibraryState.Ignored, InstallDir: { } dir } && Inside(dir, f)), !Directory.Exists(f)))
                .ToList(),
            SaveFolders = Cli.SaveFolders(state)
                .Select(f => resolver.Resolve(f.Path, "") is { } full ? new SaveFolderSummary(f.Path, full, f.ById, !Directory.Exists(full)) : new SaveFolderSummary(f.Path, null, f.ById, true))
                .ToList(),
            StartsAtSignIn = usual ? SignInStart.ForThisUser().IsOn : StartsHere(dataDir),
            DailyAt = state.GetSetting("daily.time") is { Length: > 0 } time && Daily.TryParseTime(time, out var at) ? at : null,
            LastDaily = Daily.Last(state) is { } last ? new DailySummary(last.AtUtc, last.Games, last.Uploads, last.NeedYou) : null,
            DailyAsked = state.GetSetting(Agent.DailyRequestKey) is { Length: > 0 },
            UsualDataFolder = usual,
            Defaults = defaults,
            Games = syncing.Count,
            FilesDiffer = syncing.Count(g => !defaults.FollowsFiles(g)),
            ConflictDiffer = syncing.Count(g => g.Mode == GameMode.Sync && !defaults.FollowsConflict(g)),
            Cloud = engine.Config.HasNoCloud ? NoCloud : engine.Config.UsesDrive ? Drive : Folder,
            CloudFolder = engine.Config.HasNoCloud || engine.Config.UsesDrive ? null : engine.Config.Remote,
            SignedIn = engine.Config.UsesDrive && SignedIn(dataDir),
            CanSignIn = FirstRun.GoogleClientFile(dataDir) is not null,
            ThisPc = engine.Device.Name,
            Pcs = new[] { new PcSummary(engine.Device.Name, null, Cli.AppVersion, true) }
                .Concat(others.Select(d => new PcSummary(d.Name, d.LastSeenUtc, d.AppVersion, false)))
                .ToList(),
            AppVersion = Cli.AppVersion,
            HoldWhileFullscreen = state.GetSetting(HoldKey) != "0",
            DailyNote = state.GetSetting(DailyNoteKey) == "1",
            AntiCheat = library.Where(e => e.HasAntiCheat && e.State != LibraryState.Ignored)
                .Select(e => new AntiCheatGame(e.Id, e.DisplayTitle, e.AntiCheatByHand == true && e.AntiCheat is null ? "Marked by you" : e.AntiCheat ?? "Anti-cheat"))
                .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>FOLD-10: the backup folder's size on its drive, the versions it keeps, and the drive's free space; null when the folder isn't there.</summary>
    public static async Task<BackupFigures?> FiguresAsync(string dataDir, CancellationToken ct)
    {
        if (SaveOverview.Space(dataDir) is not { } space)
        {
            return null;
        }

        using var state = new StateStore(dataDir);
        var history = new LocalHistory(Cli.HistoryFolder(state, dataDir));
        var versions = 0;
        foreach (var game in history.Games())
        {
            versions += (await history.Log.ListAsync(game, ct)).Count;
        }

        return new BackupFigures(space.Bytes, versions, space.Drive, space.FreeBytes);
    }

    /// <summary>
    /// CLOUD-05, CLOUD-08: the cloud's link or folder, the signed-in account and how full it is, asked of the cloud
    /// itself; null with no cloud connected, or when it can't be asked now (offline, signed out).
    /// </summary>
    public static async Task<CloudDetails?> CloudAsync(string dataDir, CancellationToken ct)
    {
        var config = AppConfig.Load(dataDir);
        if (config is null || config.HasNoCloud)
        {
            return null;
        }

        try
        {
            if (config.UsesDrive)
            {
                using var drive = Cli.OpenDrive(dataDir);
                var info = await new DriveCloud(drive, _ => null).GetInfoAsync(ct);
                return new CloudDetails(info.Where, info.Account, info.UsedBytes, info.TotalBytes);
            }

            var folder = await new FolderCloud(config.Remote).GetInfoAsync(ct);
            var free = FreeSpace(config.Remote);
            return new CloudDetails(folder.Where, null, null, free);
        }
        catch (Exception e) when (e is UsageException or CloudException or IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// The backup folder a folder picked in Change… means: an empty folder, or one called GameSync, is used as it is;
    /// any other gets a GameSync folder inside it, as the cloud's folder does, so the move never mixes with what's there.
    /// </summary>
    public static string BackupFolderFor(string picked)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        var empty = Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any();
        return empty || string.Equals(Path.GetFileName(full), "GameSync", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "GameSync");
    }

    /// <summary>
    /// FOLD-02, FOLD-03: moves the backup folder, under the engine lock so no sync writes to it meanwhile: everything
    /// is copied and checked before the old folder goes, and if anything fails the old one stays in use. What it
    /// refuses (FOLD-04) and what goes wrong is thrown, for the page to say. Returns what the app says once it's done.
    /// </summary>
    public static async Task<string> MoveBackupFolderAsync(string dataDir, string target, IProgress<(int Done, int Total)>? progress, Action waiting, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, waiting, ct);
        using var engine = Engine.Open(dataDir);
        var skipped = await Task.Run(() => Cli.MoveHistoryFolder(dataDir, engine.State, engine.Games, engine.InstallDirs.Values, target, progress, ct), ct);
        return skipped switch
        {
            null => $"The backup folder is {target} already.",
            0 => $"Moved the backup folder to {target}. Every file was checked there before the old folder was removed.",
            _ => $"Moved the backup folder to {target}. {skipped} damaged files stayed behind; the cloud still has them.",
        };
    }

    /// <summary>FOLD-06: every version's files on this PC, or the last 10 per game within 2 GB; thinned at the next sync.</summary>
    public static string SetKeepEverything(string dataDir, bool everything)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting("history.keep", everything ? "all" : "recent");
        return everything
            ? "This PC keeps every version's files from now on. The cloud keeps everything either way."
            : "This PC keeps the files of the last 10 versions of each game, within 2 GB; older ones are thinned at the next sync. The cloud keeps everything.";
    }

    /// <summary>
    /// FOLD-08: a folder the name search looks in for saves, kept in this PC's settings. A folder whose subfolders are
    /// mostly Steam app IDs (Steam's <c>userdata</c>, an emulator's save folder) is taken by ID. This PC is scanned again
    /// at once. Returns what the app says once it's done.
    /// </summary>
    public static async Task<string> AddSaveFolderAsync(string dataDir, string folder, IAgentOutput output, CancellationToken ct)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(full))
        {
            throw new UsageException($"{full} isn't there on this PC.");
        }

        string portable;
        bool byId;
        using (var engine = Engine.Open(dataDir))
        {
            if (engine.Here.Guard.CheckRoot(full) is { } refusal)
            {
                throw new UsageException(refusal);
            }

            portable = RootResolver.ToPortable(full, engine.Here.Folders);
            byId = NamedById(full);
            var folders = Cli.SaveFolders(engine.State);
            folders.RemoveAll(f => f.Path.Equals(portable, StringComparison.OrdinalIgnoreCase));
            folders.Add(new ExtraSaveFolder(portable, byId));
            Cli.SetSaveFolders(engine.State, folders);
        }

        output.Say($"GameSync looks for saves in {full} too; scanning this PC again...");
        await LocalGames.RescanAsync(dataDir, output, ct);
        return byId
            ? $"GameSync looks in {full} for saves, one folder per Steam app ID. What it found is on each game's page."
            : $"GameSync looks in {full} for saves too. What it found is on each game's page.";
    }

    /// <summary>FOLD-08: a save folder is looked in no more; games that sync a place in it keep syncing it.</summary>
    public static async Task<string> RemoveSaveFolderAsync(string dataDir, string path, IAgentOutput output, CancellationToken ct)
    {
        using (var engine = Engine.Open(dataDir))
        {
            var folders = Cli.SaveFolders(engine.State);
            if (folders.RemoveAll(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                throw new UsageException($"GameSync wasn't looking in {path} for saves.");
            }

            Cli.SetSaveFolders(engine.State, folders);
        }

        await LocalGames.RescanAsync(dataDir, output, ct);
        return $"GameSync no longer looks in {path} for saves. Games that sync a place in it keep syncing it.";
    }

    /// <summary>
    /// Folders named by Steam app ID: most of the folders inside are numbers, as in Steam's <c>userdata\&lt;account&gt;</c>
    /// or an emulator's save folder.
    /// </summary>
    internal static bool NamedById(string folder)
    {
        var names = Directory.EnumerateDirectories(folder).Select(Path.GetFileName).Take(500).ToList();
        return names.Count > 0 && names.Count(n => n is { Length: >= 3 } && n.All(char.IsAsciiDigit)) * 10 >= names.Count * 6;
    }

    /// <summary>
    /// BG-01: GameSync starts in the tray at sign-in, or doesn't. Only for its usual data folder: a copy on another one
    /// (a test) never changes what Windows starts, which would take the real one's place, and says so.
    /// </summary>
    public static string SetStartAtSignIn(string dataDir, bool on) =>
        Usual(dataDir) ? new FirstRun.WindowsSchedule(dataDir).StartAtSignIn(on) ?? "" : throw new UsageException(NotUsual);

    /// <summary>SET-02: the daily backup at a time, or off; only for GameSync's usual data folder.</summary>
    public static string SetDaily(string dataDir, TimeOnly? at) =>
        Usual(dataDir) ? new FirstRun.WindowsSchedule(dataDir).Daily(at) ?? "" : throw new UsageException(NotUsual);

    private const string NotUsual =
        "This copy of GameSync runs on another data folder, such as a test's, so it leaves what Windows starts as it is; the GameSync you use changes it.";

    private static bool Usual(string dataDir) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir)), Path.TrimEndingDirectorySeparator(Engine.DefaultDataDir), StringComparison.OrdinalIgnoreCase);

    /// <summary>SET-03: Run now: the agent runs the daily backup at its next round with nothing playing, as the scheduled one does.</summary>
    public static string RunDailyNow(string dataDir)
    {
        using var state = new StateStore(dataDir);
        state.SetSetting(Agent.DailyRequestKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return "The daily backup runs now: every game is checked, and games being played are skipped.";
    }

    /// <summary>SET-06: what new games start from, and who wins for them when two PCs disagree.</summary>
    public static void SetDefaults(string dataDir, GameDefaults defaults)
    {
        using var state = new StateStore(dataDir);
        defaults.Save(state);
    }

    /// <summary>
    /// SET-06: the defaults, applied to the games already syncing: their files (settings files, screenshots, logs and
    /// caches, the patterns) and, for games that sync between PCs, who wins. Each game's rules are checked as the engine
    /// checks them; a game whose rules wouldn't pass keeps its own. Its next version carries its files to its other PCs
    /// (R8). Returns what the app says once it's done.
    /// </summary>
    public static async Task<string> ApplyDefaultsAsync(string dataDir, bool files, bool conflict, Action waiting, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, waiting, ct);
        using var engine = Engine.Open(dataDir);
        var defaults = GameDefaults.Load(engine.State);
        GameDefinition Next(GameDefinition game)
        {
            var next = files ? defaults.ApplyFiles(game) : game;
            return conflict && game.Mode == GameMode.Sync ? next with { ConflictPolicy = defaults.Conflict } : next;
        }

        bool Passes(GameDefinition game) => GameValidator.Problems(engine.Here.Resolver.Resolve(game), engine.Here.Guard).FirstOrDefault() is null;

        var (changed, kept) = (0, new List<string>());
        var entries = new List<LibraryEntry>();
        foreach (var entry in engine.Library.All().Where(e => e is { MergedInto: null, State: LibraryState.Synced, Confirmed: not null }))
        {
            var next = Next(entry.Confirmed!);
            if (next == entry.Confirmed || Same(next, entry.Confirmed!))
            {
                continue;
            }

            if (!Passes(next))
            {
                kept.Add(entry.DisplayTitle);
                continue;
            }

            entries.Add(entry with { Confirmed = next });
            changed++;
        }

        engine.Library.SaveAll(entries);
        var configGames = engine.Config.Games.Select(g =>
        {
            var next = Next(g);
            if (Same(next, g))
            {
                return g;
            }

            if (!Passes(next))
            {
                kept.Add(g.Title);
                return g;
            }

            changed++;
            return next;
        }).ToList();
        if (!configGames.SequenceEqual(engine.Config.Games))
        {
            (engine.Config with { Games = configGames }).Save(dataDir);
        }

        var said = changed switch
        {
            0 => "Every game follows the defaults already.",
            1 => "1 game follows the defaults now; its next version carries its choice to your other PCs.",
            _ => $"{changed} games follow the defaults now; each one's next version carries its choice to your other PCs.",
        };
        return kept.Count == 0 ? said : $"{said} {string.Join(", ", kept)} kept {(kept.Count == 1 ? "its" : "their")} own: the defaults' rules didn't pass the checks for {(kept.Count == 1 ? "it" : "them")}.";
    }

    /// <summary>CLOUD-13: signs out of Google Drive: Google lets GameSync in no more, and syncing waits until a sign-in.</summary>
    public static async Task<string> SignOutAsync(string dataDir, CancellationToken ct)
    {
        if (FirstRun.GoogleClientFile(dataDir) is not { } client)
        {
            return "GameSync isn't signed in to Google Drive.";
        }

        var auth = new GoogleAuth(GoogleClient.Parse(await File.ReadAllBytesAsync(client, ct)), Path.Combine(dataDir, "google-token.bin"), new DpapiProtector());
        if (!auth.IsSignedIn)
        {
            return "GameSync isn't signed in to Google Drive.";
        }

        await auth.SignOutAsync(ct);
        return "Signed out of Google Drive. Syncing waits until you sign in again; your saves stay on this PC and in your Drive.";
    }

    /// <summary>PC-02: this PC's name, which its next sync tells the other PCs. Throws why a name can't be.</summary>
    public static string RenamePc(string dataDir, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new UsageException("A PC needs a name, such as DESKTOP or LAPTOP.");
        }

        if (trimmed.Length > NameLength || trimmed.Any(char.IsControl))
        {
            throw new UsageException($"A PC's name is at most {NameLength} characters, on one line.");
        }

        using var engine = Engine.Open(dataDir);
        if (trimmed == engine.Device.Name)
        {
            return $"This PC is {trimmed} already.";
        }

        var others = new LocalHistory(Cli.HistoryFolder(engine.State, dataDir)).LoadDevices().Where(d => d.Id != engine.Device.Id);
        if (others.Any(d => d.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UsageException($"Another PC is called {trimmed}; give this one a name of its own, so every save says which PC it came from.");
        }

        engine.State.RenameDevice(trimmed);
        return $"This PC is {trimmed} now. Your other PCs show the new name after its next sync.";
    }

    /// <summary>BG-05, BG-06: whether notifications wait while a fullscreen game runs, and the daily backup's line.</summary>
    public static void SetNotifications(string dataDir, bool? hold, bool? dailyNote)
    {
        using var state = new StateStore(dataDir);
        if (hold is { } h)
        {
            state.SetSetting(HoldKey, h ? "1" : "0");
        }

        if (dailyNote is { } d)
        {
            state.SetSetting(DailyNoteKey, d ? "1" : "0");
        }
    }

    /// <summary>BG-06: notifications wait while a fullscreen game runs, unless the person turned that off.</summary>
    public static bool HoldsWhileFullscreen(string dataDir)
    {
        try
        {
            using var state = new StateStore(dataDir);
            return state.GetSetting(HoldKey) != "0";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// SET-05: Copy diagnostics, for a bug report: GameSync and Windows, this PC's settings, every game and its versions,
    /// and the last days of the logs. Never a sign-in token, an account ID or a save file: anything shaped like a Google
    /// token is taken out, and saves are never read.
    /// </summary>
    public static async Task<string> DiagnosticsAsync(string dataDir, CancellationToken ct)
    {
        var text = new StringBuilder();
        var now = DateTime.Now;
        text.AppendLine(CultureInfo.InvariantCulture, $"GameSync diagnostics, {now:yyyy-MM-dd HH:mm} (UTC{now:zzz})");
        text.AppendLine(CultureInfo.InvariantCulture, $"GameSync {Cli.AppVersion} on {Environment.OSVersion.VersionString}, {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        var view = Read(dataDir);
        text.AppendLine(CultureInfo.InvariantCulture, $"Data folder: {(view.UsualDataFolder ? "the usual one" : dataDir)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Backup folder: {view.BackupFolder}{(view.BackupMissing is { } missing ? $" ({missing})" : "")}, {(view.KeepEverything ? "every version kept" : "the last 10 versions per game within 2 GB")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Cloud: {view.Cloud switch { Drive => $"Google Drive, {(view.SignedIn ? "signed in" : "signed out")}", Folder => $"a folder, {view.CloudFolder}", _ => "none yet" }}");
        text.AppendLine(CultureInfo.InvariantCulture, $"PCs: {string.Join("; ", view.Pcs.Select(p => p.ThisPc ? $"{p.Name} (this PC)" : $"{p.Name} (last seen {p.LastSeenUtc?.ToLocalTime():yyyy-MM-dd HH:mm}, GameSync {p.AppVersion})"))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Start at sign-in: {(view.StartsAtSignIn ? "on" : "off")}; daily backup: {view.DailyAt?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "off"}; last daily run: {(view.LastDaily is { } last ? $"{last.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}, {last.Games} checked, {last.Uploads} uploaded, {last.NeedYou} need you" : "none yet")}");
        var d = view.Defaults;
        text.AppendLine(CultureInfo.InvariantCulture, $"New games: settings files {d.SettingsFiles}, screenshots {(d.Screenshots ? "on" : "off")}, skip logs and caches {(d.SkipJunk ? "on" : "off")}, also skip [{string.Join(", ", d.Skip)}], conflicts {(d.Conflict == ConflictPolicy.AlwaysAsk ? "always ask" : "newest wins")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Game folders: {string.Join("; ", view.GameFolders.Select(f => $"{f.Path} ({f.Games} games{(f.Missing ? ", not there now" : "")})"))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Save folders: {string.Join("; ", view.SaveFolders.Select(f => $"{f.Path}{(f.ById ? " (by ID)" : "")}"))}");
        text.AppendLine();

        using (var engine = Engine.Open(dataDir))
        {
            var entries = engine.Library.All().Where(e => e.MergedInto is null && e.State != LibraryState.Ignored).OrderBy(e => e.DisplayTitle, StringComparer.OrdinalIgnoreCase).ToList();
            text.AppendLine(CultureInfo.InvariantCulture, $"Games ({entries.Count}):");
            foreach (var entry in entries)
            {
                var sync = engine.State.GetState(entry.Id);
                var status = sync.Status is { } known ? $"{known}{(sync.Detail is { Length: > 0 } detail ? $": {detail}" : "")}" : "no status";
                text.AppendLine(CultureInfo.InvariantCulture, $"  {entry.Id}  {entry.DisplayTitle}  [{entry.State}{(entry.Confirmed is { } c ? $", {c.Mode}, {c.ConflictPolicy}" : "")}; {(entry.Installed ? $"installed, {entry.Store}" : "not installed")}; {status}]");
            }
        }

        text.AppendLine();
        var versions = await SaveHistory.ReadVersionsAsync(dataDir, ct);
        text.AppendLine(CultureInfo.InvariantCulture, $"Versions ({versions.Versions.Count}, newest 10 of each game):");
        foreach (var game in versions.Versions.GroupBy(v => v.Game).OrderBy(g => g.First().Title, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {game.First().Title} ({game.Count()}):");
            foreach (var v in game.Take(10))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    {v.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {v.Pc}  {v.Files} files, {v.Bytes:N0} bytes  {v.Why}{(v.Kept ? "  (kept)" : "")}  {v.Id}");
            }
        }

        text.AppendLine();
        text.AppendLine("Activity log, the last 7 days:");
        foreach (var line in SaveHistory.ReadLog(dataDir).Where(e => e.AtUtc >= DateTime.UtcNow.AddDays(-7)).OrderBy(e => e.AtUtc).TakeLast(1000))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {line.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {(line.Tag ?? line.Level).ToUpperInvariant(),-8}  {line.Title}: {line.Message}");
        }

        text.AppendLine();
        text.AppendLine("Agent log, today and yesterday:");
        foreach (var day in new[] { now.Date.AddDays(-1), now.Date })
        {
            var file = Path.Combine(dataDir, "logs", $"agent-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
            if (File.Exists(file))
            {
                foreach (var line in (await File.ReadAllLinesAsync(file, ct)).TakeLast(1000))
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  {day:MM-dd} {line}");
                }
            }
        }

        return Scrub(text.ToString());
    }

    /// <summary>Takes out anything shaped like a Google sign-in token, should an error ever have quoted one (SET-05).</summary>
    public static string Scrub(string text) => Tokens().Replace(text, "[sign-in token removed]");

    private static readonly Regex TokensRegex = new(@"ya29\.[\w.-]+|1//[\w-]{20,}|(?<=(?:access|refresh|id)_token[""'=:\s]{1,3})[^\s""'&,]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex Tokens() => TokensRegex;

    private static bool SignedIn(string dataDir)
    {
        try
        {
            return FirstRun.SignedIn(dataDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>A copy on another data folder starts at sign-in only if the Run key names that folder.</summary>
    private static bool StartsHere(string dataDir) =>
        SignInStart.ForThisUser().Command is { } command && command.Contains(Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase);

    private static long? FreeSpace(string folder)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(folder)) is { } root ? new DriveInfo(root).AvailableFreeSpace : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The same rules, whatever lists the records hold them in.</summary>
    private static bool Same(GameDefinition a, GameDefinition b) =>
        a.SyncConfig == b.SyncConfig && a.IncludeScreenshots == b.IncludeScreenshots && a.ConflictPolicy == b.ConflictPolicy && a.Rules.Count == b.Rules.Count &&
        a.Rules.Zip(b.Rules).All(p => p.First.Root == p.Second.Root && p.First.Include == p.Second.Include && p.First.Category == p.Second.Category &&
            p.First.UseDefaultExcludes == p.Second.UseDefaultExcludes && p.First.Exclude.SequenceEqual(p.Second.Exclude));

    private static bool Inside(string path, string folder)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
