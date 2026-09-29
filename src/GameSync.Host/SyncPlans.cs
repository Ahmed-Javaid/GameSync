using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>What a change in the plan does, as the Plan tab shows it.</summary>
public enum PlanStep
{
    Upload,
    Download,

    /// <summary>A game its store's cloud syncs: GameSync only adds the save to its history.</summary>
    BackUp,

    /// <summary>Changes made while the game wasn't running: backed up, and held for the person to look at (BAK-11).</summary>
    Hold,
}

/// <summary>A change the next sync would make: which game, what, why in one sentence, and how much would move.</summary>
/// <param name="Fingerprint">What the plan was made from, so running it can tell whether anything changed since.</param>
public sealed record PlannedChange(GameId Id, string Title, PlanStep Step, string Why, long Bytes, string Fingerprint);

/// <summary>A game the next sync leaves alone, and why: it needs the person, it's playing, its saves or drive are missing, or it's offline.</summary>
/// <param name="Action">The status's own button (Resolve, See where), or null.</param>
public sealed record PlannedWait(GameId Id, GameId Game, string Title, GameStatus Status, string Why, string? Action);

/// <summary>SYNC-14: what the next sync would do for every game, and why, made without doing anything.</summary>
public sealed record SyncPlanView(IReadOnlyList<PlannedChange> Changes, IReadOnlyList<PlannedWait> Waits, IReadOnlyList<string> InSync, DateTime CheckedUtc);

/// <summary>What running the plan did: each game's result, and the games that changed since the plan, which didn't run.</summary>
public sealed record PlanRun(IReadOnlyList<GameResult> Results, IReadOnlyList<string> Changed)
{
    /// <summary>"Ran 3 changes." with anything that needs saying after it.</summary>
    public string Sentence
    {
        get
        {
            var ran = Results.Count(r => r.Status is not (GameStatus.Error or GameStatus.Blocked or GameStatus.FilesInUse));
            var failed = Results.Where(r => r.Status is GameStatus.Error or GameStatus.Blocked or GameStatus.FilesInUse).Select(r => r.Title).ToList();
            var parts = new List<string>();
            if (ran > 0 || (failed.Count == 0 && Changed.Count == 0))
            {
                parts.Add(ran == 1 ? "Ran 1 change." : ran == 0 ? "Nothing ran." : $"Ran {ran} changes.");
            }

            if (failed.Count > 0)
            {
                parts.Add($"{Names(failed)} didn't: see {(failed.Count == 1 ? "its" : "their")} saves for why.");
            }

            if (Changed.Count > 0)
            {
                parts.Add($"{Names(Changed)} changed since the plan, so {(Changed.Count == 1 ? "it" : "they")} didn't run; check again to see what {(Changed.Count == 1 ? "it" : "they")} would do now.");
            }

            return string.Join(" ", parts);
        }
    }

    private static string Names(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}

/// <summary>
/// The save manager's Plan tab (SYNC-14), as <c>gamesync plan</c> and <c>sync</c>: what the next sync would do for each
/// game and why, without doing anything, and then exactly that for the games the person leaves ticked.
/// </summary>
public static class SyncPlans
{
    public static async Task<SyncPlanView> CheckAsync(string dataDir, IAgentOutput output, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var running = new RunningGames(engine);
        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = running.IsRunning }, ct, recover: false);
        return Describe(await service.PlanAsync(null, ct), DateTime.UtcNow);
    }

    /// <summary>
    /// Runs the changes chosen, exactly as the plan showed them: each is planned again first, and one whose plan changed
    /// since (a new save, another PC's upload) doesn't run and is named, so the person can check again.
    /// </summary>
    public static async Task<PlanRun> RunAsync(string dataDir, IReadOnlyList<PlannedChange> chosen, IAgentOutput output, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var running = new RunningGames(engine);
        var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = running.IsRunning }, ct, recover: false);
        var fresh = await service.PlanAsync(chosen.Select(c => c.Id).ToList(), ct);
        var same = fresh.Where(p => chosen.Any(c => c.Id == p.Stream.Id && c.Fingerprint == p.Fingerprint && Step(p) == c.Step)).ToList();
        var changed = chosen.Where(c => same.All(p => p.Stream.Id != c.Id)).Select(c => c.Title).ToList();
        var results = same.Count == 0 ? [] : await service.ExecuteAsync(same, ct);
        Agent.Report(output, results);
        return new PlanRun(results, changed);
    }

    /// <summary>The plans as the Plan tab shows them: the changes, the games left alone and why, and those already in sync.</summary>
    internal static SyncPlanView Describe(IReadOnlyList<GamePlan> plans, DateTime checkedUtc)
    {
        var changes = new List<PlannedChange>();
        var waits = new List<PlannedWait>();
        var inSync = new List<string>();
        foreach (var plan in plans.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (plan.Error is { } error)
            {
                waits.Add(Wait(plan, error is Core.Scanning.FileInUseException ? GameStatus.FilesInUse : GameStatus.Error, error.Message, "See why"));
                continue;
            }

            var decision = plan.Decision!;
            switch (decision.Action)
            {
                case SyncAction.None or SyncAction.AdoptHead:
                    inSync.Add(plan.Title);
                    break;
                case SyncAction.Upload or SyncAction.UploadHeld or SyncAction.Download:
                    changes.Add(new PlannedChange(plan.Stream.Id, plan.Title, Step(plan)!.Value, decision.Reason, Bytes(plan), plan.Fingerprint));
                    break;
                case SyncAction.NeedsYou:
                    waits.Add(Wait(plan, GameStatus.Conflict, decision.Reason, "Resolve"));
                    break;
                case SyncAction.Playing:
                    waits.Add(Wait(plan, GameStatus.Playing, "Being played now. It syncs a few seconds after it closes.", null));
                    break;
                case SyncAction.Unavailable:
                    waits.Add(Wait(plan, GameStatus.NotAvailable, decision.Reason, "See where"));
                    break;
                case SyncAction.SavesMissing:
                    waits.Add(Wait(plan, GameStatus.SavesMissing, decision.Reason, "See where"));
                    break;
                case SyncAction.NoSaves:
                    waits.Add(Wait(plan, GameStatus.NoSaves, decision.Reason, "Add a place"));
                    break;
                default:
                    // Offline or no cloud yet: this PC's side is kept, and the rest waits for the cloud (PC-05).
                    waits.Add(Wait(plan, GameStatus.UploadPending, decision.Reason, null));
                    break;
            }
        }

        return new SyncPlanView(changes, waits, inSync, checkedUtc);
    }

    private static PlannedWait Wait(GamePlan plan, GameStatus status, string why, string? action) =>
        new(plan.Stream.Id, plan.Stream.Game.Id, plan.Title, status, why, action);

    private static PlanStep? Step(GamePlan plan) => plan.Decision?.Action switch
    {
        SyncAction.Upload => plan.Stream.Definition.Mode == GameMode.BackupOnly ? PlanStep.BackUp : PlanStep.Upload,
        SyncAction.UploadHeld => PlanStep.Hold,
        SyncAction.Download => PlanStep.Download,
        _ => null,
    };

    /// <summary>How much would move: what's new here since the last agreed save, or what the cloud's save has that this PC doesn't.</summary>
    private static long Bytes(GamePlan plan)
    {
        if (plan.Decision?.Action == SyncAction.Download && plan.Decision.Head is { } head)
        {
            var here = plan.Local.Select(f => f.Hash).ToHashSet();
            return head.Files.Where(f => !here.Contains(f.Hash)).Sum(f => f.Size);
        }

        var known = plan.Base?.Files.Select(f => f.Hash).ToHashSet() ?? [];
        return plan.Local.Where(f => !known.Contains(f.Hash)).Sum(f => f.Size);
    }
}
