using System.Globalization;
using System.Text.Json;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>SET-03: the last daily run, as Settings shows it.</summary>
internal sealed record DailyRun(DateTime AtUtc, int Games, int Uploads, int NeedYou);

/// <summary>
/// The daily backup (BG-03), the safety net under the syncs at each game's exit: it skips running games, keeps the
/// saves of games that updated (BAK-06), syncs every game, refreshes the save list when it's a week old, writes one line
/// per game to the activity log, and records the run (SET-03).
/// </summary>
internal static class Daily
{
    /// <summary>The note on the saves the daily backup makes current, as a game's history and the Versions tab show them (MGR-08).</summary>
    public const string VersionNote = "Daily backup";

    private const string LastKey = "daily.last";

    /// <param name="deferUploads">KAN-88: the app's agent runs it: its uploads follow beside the rounds.</param>
    public static async Task<DailyRun> RunAsync(string dataDir, IAgentOutput output, Func<GameId, bool>? isRunning, CancellationToken ct,
        bool deferUploads = false)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        using var engine = Engine.Open(dataDir);
        var (_, note) = await new SaveListStore(dataDir).GetAsync(refresh: false, Cli.LudusaviManifest, ct);
        if (note is not null)
        {
            output.Say(note);
        }

        var running = isRunning ?? new RunningGames(engine).IsRunning;
        var (service, recovered) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = running, UploadLabel = VersionNote, DeferUploads = deferUploads }, ct);
        await Builds.KeepAsync(engine, service, Builds.Changed(engine), output, ct);
        var results = recovered.Concat(await service.SyncAsync(null, ct)).ToList();

        // A game that changed has its sync's own line already; one that didn't gets the daily backup's, so every game has one.
        foreach (var result in results.Where(r => r.Action is SyncAction.None))
        {
            engine.State.Log(result.Game, "info", $"Daily backup: {result.Message}", EventTags.Daily);
        }

        var run = new DailyRun(DateTime.UtcNow, results.Count, results.Count(r => r.NewVersion is not null),
            results.Count(r => r.Status is GameStatus.Conflict or GameStatus.SavesMissing or GameStatus.Blocked or GameStatus.HeldForReview));
        engine.State.SetSetting(LastKey, JsonSerializer.Serialize(run, Json.Options));
        output.Say($"Daily run: {run.Games} games checked, {run.Uploads} uploaded{(run.NeedYou > 0 ? $", {run.NeedYou} conflict{(run.NeedYou == 1 ? "" : "s")}" : "")}.");

        // BG-05: the optional daily summary, one line the person asked for in Settings → Notifications.
        if (engine.State.GetSetting(SettingsData.DailyNoteKey) == "1")
        {
            output.Tell("Daily backup", Summary(run));
        }

        return run;
    }

    /// <summary>The daily summary's line (BG-05): what was checked and what changed.</summary>
    public static string Summary(DailyRun run)
    {
        var games = run.Games == 1 ? "1 game" : $"{run.Games} games";
        var changed = run.Uploads switch
        {
            0 => $"Checked {games}; nothing new to upload.",
            1 => $"Checked {games} and uploaded 1 new save.",
            _ => $"Checked {games} and uploaded {run.Uploads} new saves.",
        };
        return run.NeedYou switch
        {
            0 => changed,
            1 => $"{changed} 1 conflict: open GameSync to see it.",
            _ => $"{changed} {run.NeedYou} conflicts: open GameSync to see them.",
        };
    }

    public static DailyRun? Last(StateStore state) =>
        state.GetSetting(LastKey) is { Length: > 0 } text ? JsonSerializer.Deserialize<DailyRun>(text, Json.Options) : null;

    /// <summary>
    /// SET-02: whether the most recent daily time passed without a run, as when the PC was off then; the sign-in catch-up
    /// runs only in that case.
    /// </summary>
    public static bool Missed(DateTime? lastRunUtc, TimeOnly dailyAt, DateTime nowLocal)
    {
        var today = nowLocal.Date + dailyAt.ToTimeSpan();
        var due = nowLocal >= today ? today : today.AddDays(-1);
        return lastRunUtc is null || lastRunUtc.Value.ToLocalTime() < due;
    }

    public static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

/// <summary>BAK-06: games whose installed build changed since last seen, and keeping their saves before the new build runs.</summary>
internal static class Builds
{
    public static IReadOnlyList<(LibraryEntry Entry, string Build)> Changed(Engine engine)
    {
        var epic = StoreLocations.EpicManifests();
        var changed = new List<(LibraryEntry, string)>();
        foreach (var entry in engine.Library.All().Where(e => e is { State: LibraryState.Synced, Installed: true, MergedInto: null }))
        {
            if (BuildReader.Current(entry, epic) is not { } build)
            {
                continue;
            }

            var known = engine.State.GetSetting($"build.{entry.Id}");
            if (known is null)
            {
                engine.State.SetSetting($"build.{entry.Id}", build);
            }
            else if (known != build)
            {
                changed.Add((entry, build));
            }
        }

        return changed;
    }

    public static async Task KeepAsync(Engine engine, SyncService service, IReadOnlyList<(LibraryEntry Entry, string Build)> changed, IAgentOutput output,
        CancellationToken ct)
    {
        foreach (var (entry, build) in changed.Where(c => engine.Games.Any(g => g.Id == c.Entry.Id)))
        {
            try
            {
                var result = await service.KeepBeforeUpdateAsync(entry.Id, build, ct);
                output.Say($"{entry.DisplayTitle}: {result.Message}");
                engine.State.SetSetting($"build.{entry.Id}", build);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or Core.Storage.CloudException)
            {
                output.Say($"! {entry.DisplayTitle}: couldn't keep the save before its update: {e.Message}");
            }
        }
    }
}
