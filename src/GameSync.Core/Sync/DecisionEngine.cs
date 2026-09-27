using System.Globalization;
using GameSync.Core.Games;
using GameSync.Core.Model;

namespace GameSync.Core.Sync;

public enum SyncAction
{
    /// <summary>This PC and the cloud agree.</summary>
    None,

    /// <summary>This PC already has the cloud's current files; only the record of what was agreed moves forward.</summary>
    AdoptHead,

    Upload,

    /// <summary>Back up changes made while the game wasn't running, without making them current (BAK-11).</summary>
    UploadHeld,

    Download,

    /// <summary>A conflict waiting for the user; nothing moves in either direction (SYNC-11).</summary>
    NeedsYou,

    /// <summary>A drive or save folder is missing, which never reads as "no saves" (SYNC-12).</summary>
    Unavailable,

    /// <summary>Every save file is gone from this PC; that never syncs (SYNC-13).</summary>
    SavesMissing,

    NoSaves,

    /// <summary>Offline, and the cloud's side matters: this PC's changes are kept locally and the decision waits (PC-05).</summary>
    WaitForCloud,
}

/// <summary>Before downloading, this PC's files are kept as a pinned version.</summary>
public sealed record KeepLocal(VersionOrigin Origin, string Label);

/// <summary>A pin to add; <paramref name="FromConflict"/> marks the save that lost a conflict, which Swap switches to.</summary>
public sealed record PinRequest(VersionId Version, string Label, bool FromConflict = false);

public sealed record SyncDecision
{
    public required SyncAction Action { get; init; }

    public required string Reason { get; init; }

    /// <summary>The cloud version to adopt or download.</summary>
    public VersionRecord? Head { get; init; }

    public VersionKind UploadKind { get; init; } = VersionKind.Normal;

    public VersionOrigin UploadOrigin { get; init; } = VersionOrigin.Session;

    public VersionId? UploadParent { get; init; }

    public IReadOnlyList<VersionId> Supersedes { get; init; } = [];

    public KeepLocal? KeepLocalFirst { get; init; }

    public IReadOnlyList<PinRequest> Pins { get; init; } = [];

    /// <summary>What to tell the user afterwards, for example that a conflict was resolved and Swap reverses it.</summary>
    public string? Notice { get; init; }

    /// <summary>The game's changes are held for review (BAK-11), whether or not this sync uploads them.</summary>
    public bool Held { get; init; }
}

public sealed record SyncInputs
{
    public required GameDefinition Game { get; init; }

    public required DeviceInfo Device { get; init; }

    /// <summary>This PC's files for the stream being synced.</summary>
    public required IReadOnlyList<FileEntry> Local { get; init; }

    /// <summary>Root keys whose folder or drive is missing on this PC.</summary>
    public IReadOnlyList<Scanning.RootProblem> Problems { get; init; } = [];

    /// <summary>The version this PC last agreed on; null on the game's first sync here.</summary>
    public VersionRecord? Base { get; init; }

    /// <summary>BAK-07: the game was reinstalled since the last sync, so this counts as a first sync.</summary>
    public bool Reinstalled { get; init; }

    public required IReadOnlyList<VersionRecord> Versions { get; init; }

    public IReadOnlyList<SessionInfo> Sessions { get; init; } = [];

    /// <summary>The user approved this PC's changes (BAK-11), so they count as made during play.</summary>
    public bool TreatChangesAsInSession { get; init; }

    /// <summary>A conflict is already waiting for the user; it stays until they decide (SYNC-11).</summary>
    public bool ConflictPending { get; init; }

    /// <summary>False when the cloud couldn't be reached, so <see cref="Versions"/> may be out of date (PC-05).</summary>
    public bool CloudReachable { get; init; } = true;

    /// <summary>This PC's clock minus the cloud's, when the cloud has a clock (SYNC-08).</summary>
    public TimeSpan? ClockSkew { get; init; }

    public TimeSpan ClockTolerance { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>Decides what one game's sync does. Pure: the same inputs always give the same decision (SYNC-14).</summary>
public static class DecisionEngine
{
    public static readonly TimeSpan SessionSlack = TimeSpan.FromSeconds(30);

    public static SyncDecision Decide(SyncInputs input)
    {
        var decision = DecideOnline(input);
        return input.CloudReachable ? decision : WhileOffline(input, decision);
    }

    /// <summary>"5 minutes ahead of" or "3 minutes behind", for a clock that's off.</summary>
    public static string DescribeSkew(TimeSpan skew)
    {
        var minutes = Math.Max(1, (int)Math.Round(skew.Duration().TotalMinutes));
        var unit = minutes == 1 ? "minute" : "minutes";
        return skew > TimeSpan.Zero ? $"{minutes} {unit} ahead of" : $"{minutes} {unit} behind";
    }

    /// <summary>
    /// PC-05: offline, a sync only snapshots this PC. Uploads that don't depend on the cloud's newest state go ahead
    /// (they wait in the outbox); downloads and conflicts wait until the cloud can be seen again, with this PC's
    /// changes kept locally meanwhile.
    /// </summary>
    private static SyncDecision WhileOffline(SyncInputs input, SyncDecision online)
    {
        switch (online.Action)
        {
            // A first sync here can't know whether the cloud already has this game's saves, so the first-sync rule waits.
            case SyncAction.Upload when input.Base is null || input.Reinstalled:
                return new SyncDecision
                {
                    Action = SyncAction.WaitForCloud,
                    KeepLocalFirst = new KeepLocal(VersionOrigin.KeptAtFirstSync, $"{input.Device.Name}'s files before its first sync"),
                    Reason = "Offline, and this is the game's first sync on this PC: its files are kept here, and what happens next is decided when you're back online.",
                };
            case SyncAction.Upload when online.Pins.Count == 0 && online.Supersedes.Count == 0:
                return online with { Reason = $"{online.Reason} Offline: kept on this PC, and it uploads when you're back online." };
            case SyncAction.Download when online.KeepLocalFirst is null:
                return new SyncDecision
                {
                    Action = SyncAction.WaitForCloud,
                    Reason = $"Offline: {online.Reason} It downloads when you're back online.",
                };
            case SyncAction.Upload or SyncAction.Download:
                return new SyncDecision
                {
                    Action = SyncAction.WaitForCloud,
                    KeepLocalFirst = online.KeepLocalFirst ?? new KeepLocal(VersionOrigin.KeptInConflict, $"{input.Device.Name}'s save while offline"),
                    Reason = "Offline, and the cloud has a different save too: this PC's is kept, and which one continues is decided when you're back online.",
                };
            default:
                return online;
        }
    }

    private static SyncDecision DecideOnline(SyncInputs input)
    {
        var local = input.Local;
        var basis = input.Reinstalled ? null : input.Base;

        if (UnavailableReason(input, basis) is { } unavailable)
        {
            return new SyncDecision { Action = SyncAction.Unavailable, Reason = unavailable };
        }

        var heads = VersionGraph.Heads(input.Versions);
        if (input.Game.Mode == GameMode.BackupOnly)
        {
            return DecideBackupOnly(input, basis, heads);
        }

        if (heads.Count > 1)
        {
            return NeedsYou($"{Names(heads)} uploaded different saves from the same starting point.");
        }

        var head = heads.FirstOrDefault();
        if (head is null)
        {
            if (local.Count == 0)
            {
                return basis is { Files.Count: > 0 } ? SavesMissing() : new SyncDecision { Action = SyncAction.NoSaves, Reason = "No save files found yet." };
            }

            return Upload(basis is null ? VersionOrigin.FirstBackup : VersionOrigin.Reupload, parent: null,
                "Nothing in the cloud yet: uploading this PC's saves.");
        }

        if (basis is null)
        {
            return DecideFirstSync(input, head);
        }

        var localChanged = !FileSet.SameContent(local, basis.Files);
        var cloudChanged = head.Id != basis.Id && !FileSet.SameContent(head.Files, basis.Files);

        if (local.Count == 0 && basis.Files.Count > 0)
        {
            return cloudChanged
                ? Download(head, null, "This PC's save files are gone; getting the newer saves from the cloud.")
                : SavesMissing();
        }

        if (!localChanged && !cloudChanged)
        {
            return head.Id == basis.Id
                ? new SyncDecision { Action = SyncAction.None, Reason = "In sync." }
                : new SyncDecision { Action = SyncAction.AdoptHead, Head = head, Reason = "In sync." };
        }

        if (localChanged && FileSet.SameContent(local, head.Files))
        {
            return new SyncDecision { Action = SyncAction.AdoptHead, Head = head, Reason = "Both PCs ended up with the same saves." };
        }

        if (input.ConflictPending)
        {
            return NeedsYou("A conflict is waiting for you to choose which save continues.");
        }

        if (localChanged && !cloudChanged)
        {
            return InSession(input, basis)
                ? Upload(VersionOrigin.Session, head.Id, "Changed on this PC.")
                : Held(input, basis);
        }

        if (!localChanged)
        {
            return Download(head, null, $"Newer save from {head.Device.Name}.");
        }

        return DecideConflict(input, basis, head);
    }

    private static SyncDecision DecideFirstSync(SyncInputs input, VersionRecord head)
    {
        var local = input.Local;
        if (local.Count == 0)
        {
            return Download(head, null, "First sync on this PC: getting the cloud's saves.");
        }

        if (FileSet.SameContent(local, head.Files))
        {
            return new SyncDecision { Action = SyncAction.AdoptHead, Head = head, Reason = "This PC already has the cloud's saves." };
        }

        // SYNC-05 and BAK-07: never "newest wins" here, because a fresh install's empty save is always the newest.
        var reason = input.Reinstalled
            ? "Reinstalled: the cloud's saves stay current, and the new local files are kept in history."
            : "First sync on this PC: the cloud's saves stay current, and this PC's files are kept in history.";
        var label = input.Reinstalled
            ? $"{input.Device.Name}'s files after a reinstall"
            : $"{input.Device.Name}'s files before its first sync";
        return Download(head, new KeepLocal(VersionOrigin.KeptAtFirstSync, label), reason);
    }

    private static SyncDecision DecideConflict(SyncInputs input, VersionRecord basis, VersionRecord head)
    {
        var local = input.Local;
        var localInSession = InSession(input, basis);
        var cloudName = head.Device.Name;
        var here = input.Device.Name;

        switch (input.Game.ConflictPolicy)
        {
            case ConflictPolicy.AlwaysAsk:
                return NeedsYou($"Changed on {here} and on {cloudName}; this game is set to always ask.");
            case ConflictPolicy.ThisPcWins when localInSession:
                return LocalWins(input, head, $"Changed on {here} and on {cloudName}; this game is set to keep {here}'s save.");
            case ConflictPolicy.ThisPcWins:
                return NeedsYou($"Changed on {here} and on {cloudName}, and {here}'s change happened while the game wasn't running.");
        }

        // SYNC-08: newest-wins compares times, which a wrong clock makes meaningless.
        if (input.ClockSkew is { } skew && skew.Duration() > input.ClockTolerance)
        {
            return NeedsYou($"Changed on {here} and on {cloudName}, and {here}'s clock is {DescribeSkew(skew)} Google's, so which save is newer can't be trusted.");
        }

        var localNewest = FileSet.NewestChange(local, basis.Files) ?? DateTime.MinValue;
        var cloudNewest = FileSet.NewestChange(head.Files, basis.Files) ?? head.CreatedUtc;
        var localIsNewer = localNewest > cloudNewest;
        var newer = localIsNewer ? local : head.Files;
        var newerName = localIsNewer ? here : cloudName;

        // SYNC-06: a newer side that lost most of its files looks like a reset, not progress.
        if (newer.Count == 0 || LostHalf(newer, basis.Files))
        {
            return NeedsYou($"{newerName}'s save is newer, but it lost more than half of its files or size since the last sync.");
        }

        // SYNC-07: a change nobody played for never wins by itself.
        if (localIsNewer && !localInSession)
        {
            return NeedsYou($"{here}'s save is newer, but it changed while the game wasn't running.");
        }

        return localIsNewer
            ? LocalWins(input, head, $"Changed on {here} and on {cloudName}; {here}'s save is newer ({Time(localNewest)}).")
            : CloudWins(input, head, $"Changed on {here} and on {cloudName}; {cloudName}'s save is newer ({Time(cloudNewest)}).");
    }

    private static SyncDecision LocalWins(SyncInputs input, VersionRecord head, string reason) => new()
    {
        Action = SyncAction.Upload,
        UploadKind = VersionKind.Normal,
        UploadOrigin = VersionOrigin.Session,
        UploadParent = head.Id,
        Head = head,
        Reason = reason,
        Pins = [new PinRequest(head.Id, $"{head.Device.Name}'s save, replaced by {input.Device.Name}'s in a conflict", FromConflict: true)],
        Notice = $"Kept {input.Device.Name}'s save. {head.Device.Name}'s is pinned in history; Swap switches to it.",
    };

    private static SyncDecision CloudWins(SyncInputs input, VersionRecord head, string reason) => new()
    {
        Action = SyncAction.Download,
        Head = head,
        Reason = reason,
        KeepLocalFirst = new KeepLocal(VersionOrigin.KeptInConflict, $"{input.Device.Name}'s save, replaced by {head.Device.Name}'s in a conflict"),
        Notice = $"Kept {head.Device.Name}'s save. {input.Device.Name}'s is pinned in history; Swap switches to it.",
    };

    private static SyncDecision DecideBackupOnly(SyncInputs input, VersionRecord? basis, IReadOnlyList<VersionRecord> heads)
    {
        var local = input.Local;
        if (local.Count == 0)
        {
            return basis is { Files.Count: > 0 } ? SavesMissing() : new SyncDecision { Action = SyncAction.NoSaves, Reason = "No save files found yet." };
        }

        if (basis is not null && FileSet.SameContent(local, basis.Files))
        {
            return new SyncDecision { Action = SyncAction.None, Reason = "Backed up." };
        }

        // Each PC backs up its own copy; parenting on every head keeps a single line of history.
        return new SyncDecision
        {
            Action = SyncAction.Upload,
            UploadKind = VersionKind.Normal,
            UploadOrigin = basis is null ? VersionOrigin.FirstBackup : VersionOrigin.Session,
            UploadParent = heads.FirstOrDefault()?.Id,
            Supersedes = heads.Skip(1).Select(h => h.Id).ToList(),
            Reason = "Backing up this PC's saves (the store's cloud syncs this game).",
        };
    }

    private static SyncDecision Held(SyncInputs input, VersionRecord basis)
    {
        var alreadyHeld = input.Versions
            .Where(v => v.Kind == VersionKind.Held && v.Device.Id == input.Device.Id && v.Parent == basis.Id)
            .OrderByDescending(v => v.CreatedUtc)
            .FirstOrDefault();
        if (alreadyHeld is not null && FileSet.SameContent(input.Local, alreadyHeld.Files))
        {
            return new SyncDecision
            {
                Action = SyncAction.None,
                Held = true,
                Reason = "Held for review: changed while the game wasn't running. Approve to sync it.",
            };
        }

        return new SyncDecision
        {
            Action = SyncAction.UploadHeld,
            Held = true,
            UploadKind = VersionKind.Held,
            UploadOrigin = VersionOrigin.OutOfSession,
            UploadParent = basis.Id,
            Reason = "Changed while the game wasn't running: backed up and held for review.",
        };
    }

    /// <summary>
    /// True when every file that changed since <paramref name="basis"/> was written during a known play session,
    /// and any deletions happened after a session that ended since then.
    /// </summary>
    internal static bool InSession(SyncInputs input, VersionRecord basis)
    {
        if (input.TreatChangesAsInSession)
        {
            return true;
        }

        var changed = FileSet.ChangedOrAdded(input.Local, basis.Files);
        var removed = FileSet.Removed(input.Local, basis.Files);
        if (changed.Count == 0 && removed.Count == 0)
        {
            return true;
        }

        if (changed.Any(f => !input.Sessions.Any(s => s.Covers(f.ModifiedUtc, SessionSlack))))
        {
            return false;
        }

        return removed.Count == 0 || input.Sessions.Any(s => s.EndUtc >= basis.CreatedUtc);
    }

    private static string? UnavailableReason(SyncInputs input, VersionRecord? basis)
    {
        foreach (var problem in input.Problems)
        {
            // A missing drive always blocks; a missing folder only matters when saves were there before.
            var hadFiles = basis?.Files.Any(f => f.Path.StartsWith(problem.RootKey + "/", StringComparison.OrdinalIgnoreCase)) ?? false;
            if (problem.DriveMissing || hadFiles)
            {
                return problem.Message;
            }
        }

        return null;
    }

    private static bool LostHalf(IReadOnlyCollection<FileEntry> newer, IReadOnlyCollection<FileEntry> before)
    {
        if (before.Count == 0)
        {
            return false;
        }

        return newer.Count * 2 < before.Count || FileSet.TotalSize(newer) * 2 < FileSet.TotalSize(before);
    }

    private static SyncDecision Upload(VersionOrigin origin, VersionId? parent, string reason) => new()
    {
        Action = SyncAction.Upload,
        UploadKind = VersionKind.Normal,
        UploadOrigin = origin,
        UploadParent = parent,
        Reason = reason,
    };

    private static SyncDecision Download(VersionRecord head, KeepLocal? keep, string reason) => new()
    {
        Action = SyncAction.Download,
        Head = head,
        KeepLocalFirst = keep,
        Reason = reason,
    };

    private static SyncDecision NeedsYou(string reason) => new() { Action = SyncAction.NeedsYou, Reason = $"Conflict, needs you: {reason}" };

    private static SyncDecision SavesMissing() => new()
    {
        Action = SyncAction.SavesMissing,
        Reason = "Every save file for this game is gone from this PC. Nothing was changed in the cloud.",
    };

    private static string Names(IEnumerable<VersionRecord> heads) =>
        string.Join(" and ", heads.Select(h => h.Device.Name).Distinct());

    private static string Time(DateTime utc) => utc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
}
