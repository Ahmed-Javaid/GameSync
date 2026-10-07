using System.Text.Json;
using GameSync.Core.Model;

namespace GameSync.Core.Sync;

/// <summary>One file operation of a restore. Replace moves a verified staged file into place; Remove moves a file aside.</summary>
public sealed record JournalOp(string Kind, string Target, string? Staged, string Aside);

/// <summary>
/// KAN-91: a restore's swap stopped at <see cref="Target"/>, which something opened a moment ago. <see cref="Undone"/>: what
/// it had moved is back, and the save folder is as it was; otherwise the journal stays, to finish the swap at the next start.
/// </summary>
public sealed class SwapStoppedException(string target, bool undone, Exception inner)
    : IOException($"The restore stopped at {target}: {inner.Message}", inner)
{
    public string Target { get; } = target;

    public bool Undone { get; } = undone;
}

/// <summary>
/// BAK-08: a restore writes every file to a staged copy next to its target and checks it first, then records the
/// planned renames here, then swaps. If GameSync dies mid-swap, <see cref="RecoverAll"/> finishes the swap on the next
/// start, so a save folder never ends up with half-written files.
/// </summary>
public sealed record RestoreJournal
{
    public required string Id { get; init; }

    public required GameId Game { get; init; }

    /// <summary>The version this PC agrees on once the swap is complete.</summary>
    public required VersionRecord Target { get; init; }

    public required IReadOnlyList<JournalOp> Ops { get; init; }

    /// <summary>False while files are still being staged: a crash then rolls back. True once every staged file checked out: a crash then rolls forward.</summary>
    public bool Ready { get; init; }

    public static string Folder(string dataDir) => Path.Combine(dataDir, "journal");

    public string PathIn(string dataDir) => Path.Combine(Folder(dataDir), $"{Id}.json");

    public void Save(string dataDir)
    {
        Directory.CreateDirectory(Folder(dataDir));
        var path = PathIn(dataDir);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, Json.Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Runs every operation. Each step checks what's already done, so running it twice is safe. With
    /// <paramref name="undoOnFailure"/> (a restore under way, not one finished after a crash), a step that fails because
    /// something opened a file a moment ago puts back every step before it, in reverse, and throws
    /// <see cref="SwapStoppedException"/>: the save folder is as it was, never half restored (KAN-91).
    /// </summary>
    public void Commit(bool undoOnFailure = false)
    {
        var done = new List<JournalOp>();
        foreach (var op in Ops)
        {
            try
            {
                if (op.Kind == "replace")
                {
                    if (op.Staged is not null && File.Exists(op.Staged))
                    {
                        MoveAside(op.Target, op.Aside);
                        File.Move(op.Staged, op.Target);
                    }
                }
                else
                {
                    MoveAside(op.Target, op.Aside);
                }
            }
            catch (Exception e) when (undoOnFailure && e is IOException or UnauthorizedAccessException)
            {
                // This step's own target is back too, if it got as far as moving it aside.
                throw new SwapStoppedException(op.Target, Undo([.. done, op]), e);
            }

            done.Add(op);
            if (done.Count == 1)
            {
                CrashPoints.Hit(CrashPoints.MidSwap);
            }
        }
    }

    /// <summary>Puts back what <paramref name="done"/> moved, last first; false when something couldn't be put back.</summary>
    private static bool Undo(IEnumerable<JournalOp> done)
    {
        try
        {
            foreach (var op in done.Reverse())
            {
                if (op.Kind == "replace" && op.Staged is not null && !File.Exists(op.Staged) && File.Exists(op.Target) && File.Exists(op.Aside))
                {
                    // The new file goes back to being staged, and the one it replaced back in place.
                    File.Move(op.Target, op.Staged);
                }
                else if (op.Kind == "replace" && op.Staged is not null && !File.Exists(op.Staged) && File.Exists(op.Target) && !File.Exists(op.Aside))
                {
                    // A file the restore added where there was none: it goes, back to being staged.
                    File.Move(op.Target, op.Staged);
                    continue;
                }

                if (File.Exists(op.Aside) && !File.Exists(op.Target))
                {
                    File.Move(op.Aside, op.Target);
                }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Removes staged files of a restore that never got to the swap. The save folder was never touched.</summary>
    public void Abort()
    {
        foreach (var op in Ops)
        {
            if (op.Staged is not null && File.Exists(op.Staged))
            {
                File.Delete(op.Staged);
            }
        }
    }

    public void Delete(string dataDir) => File.Delete(PathIn(dataDir));

    /// <summary>
    /// Settles every restore a crash interrupted: finishes the ready ones and rolls back the rest. Returns the finished
    /// ones so the caller can record their target versions and then delete them.
    /// </summary>
    public static IReadOnlyList<RestoreJournal> RecoverAll(string dataDir)
    {
        var folder = Folder(dataDir);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        foreach (var temp in Directory.EnumerateFiles(folder, "*.json.tmp"))
        {
            // A journal that never finished writing: nothing was staged under it yet.
            File.Delete(temp);
        }

        var finished = new List<RestoreJournal>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var journal = JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllBytes(file), Json.Options);
            if (journal is null)
            {
                continue;
            }

            if (!journal.Ready)
            {
                journal.Abort();
                File.Delete(file);
                continue;
            }

            journal.Commit();
            finished.Add(journal);
        }

        return finished;
    }

    private static void MoveAside(string target, string aside)
    {
        if (!File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
        File.Move(target, aside, overwrite: true);
    }
}
