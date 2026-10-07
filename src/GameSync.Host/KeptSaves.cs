using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Scanning;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>
/// KAN-61: a game not syncing yet whose save folder, as the scan proposed it whole, holds the live save and copies of it
/// kept by hand beside it (the owner's Bloodborne).
/// </summary>
/// <param name="Root">The folder the scan proposed whole, as the proposal has it ("&lt;installDir&gt;/Shadlix/user/savedata").</param>
/// <param name="Live">The live save below it, with '/' ("1/CUSA00207/SPRJ0005").</param>
/// <param name="LiveFolder">The live save's folder on this PC.</param>
/// <param name="Folder">The folder it and its copies are in on this PC, where Import kept saves looks.</param>
/// <param name="Copies">How many copies are beside it, as Import kept saves counts them.</param>
/// <param name="CopiesBytes">What the folder holds besides the live save.</param>
/// <param name="Names">The two copies made last, for a sentence.</param>
public sealed record GameKept(string Root, string Live, string LiveFolder, string Folder, int Copies, long CopiesBytes, int LiveFiles, long LiveBytes,
    DateTime? LiveNewestUtc, IReadOnlyList<string> Names);

/// <summary>What the dialog shows: the live save, each copy as Import kept saves would bring it in, and the whole folder's size.</summary>
public sealed record KeptLook(GameKept Kept, IReadOnlyList<ImportItem> Items, IReadOnlyList<string> Skipped, long WholeBytes);

/// <summary>What keeping the live save came to: how many named saves the copies became, and anything left out.</summary>
public sealed record KeptDone(int Named, IReadOnlyList<string> Skipped);

/// <summary>
/// KAN-61: keeping only a game's live save when its folder holds copies kept by hand beside it, each copy brought in as
/// a named save in the same step (design system → KeptCopiesDialog). The folders are never changed.
/// </summary>
public static class KeptSaves
{
    /// <summary>
    /// The live save beside copies kept by hand in a folder the scan proposed whole, for a game not syncing yet; null when
    /// there's none. Only folders are listed and files' sizes read, so a game's page can ask as it opens.
    /// </summary>
    public static GameKept? Find(LibraryEntry entry, RootResolver resolver)
    {
        if (entry.Confirmed is not null)
        {
            return null;
        }

        foreach (var proposal in entry.Proposals.Where(p => p.Include == "**" && p.Category == SaveCategory.Save).OrderByDescending(p => p.Files))
        {
            var folder = resolver.Resolve(new GameDefinition
            {
                Id = entry.Id,
                Title = entry.DisplayTitle,
                Roots = new Dictionary<string, string> { ["r"] = proposal.Root },
                Rules = [],
            }).Roots["r"];
            if (RootResolver.IsUnresolved(folder) || KeptCopies.Find(folder) is not { } kept)
            {
                continue;
            }

            return new GameKept(proposal.Root, Path.GetRelativePath(folder, kept.Live).Replace('\\', '/'), kept.Live, kept.Folder, kept.Copies, kept.CopiesBytes,
                kept.LiveFiles, kept.LiveBytes, kept.LiveNewestUtc, kept.Names);
        }

        return null;
    }

    /// <summary>
    /// The dialog's look: each copy read as Import kept saves would bring it in, its files hashed so that one the same as
    /// another is said so and kept once. Nothing is changed.
    /// </summary>
    /// <param name="progress">How far it is (KAN-80): each copy read, of how many, with the bytes read of all of them.</param>
    public static async Task<KeptLook> LookAsync(string dataDir, GameId game, CancellationToken ct, IProgress<WorkProgress>? progress = null)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        var entry = Entry(engine, game);
        var kept = Find(entry, engine.Here.Resolver) ?? throw Gone(entry);
        var scanner = new SnapshotScanner(engine.Here.Guard, engine.State);
        var temp = Path.Combine(Path.GetTempPath(), "GameSync", $"kept-{Guid.NewGuid():N}");
        try
        {
            return await Task.Run(() =>
            {
                var (found, skippedByFinder) = NamedSaveFinder.Find(kept.Folder, kept.LiveFolder, temp);
                var skipped = skippedByFinder.ToList();
                var items = new List<ImportItem>();
                var firsts = new List<(string Name, IReadOnlyList<FileEntry> Files)>();
                var read = 0;
                long readBytes = 0;
                progress?.Report(new WorkProgress("reading", 0, found.Count, 0, kept.CopiesBytes));
                foreach (var save in found)
                {
                    ct.ThrowIfCancellationRequested();
                    var snapshot = scanner.Scan(new GameDefinition
                    {
                        Id = game,
                        Title = entry.DisplayTitle,
                        Roots = new Dictionary<string, string> { ["saves"] = save.Folder },
                        Rules = [new SaveRule { Root = "saves" }],
                    }, _ => true);
                    readBytes += FileSet.TotalSize(snapshot.Files);
                    progress?.Report(new WorkProgress("reading", ++read, found.Count, readBytes, Math.Max(kept.CopiesBytes, readBytes)));
                    skipped.AddRange(snapshot.Warnings.Select(w => $"{save.Name}: {w}"));
                    if (snapshot.Files.Count == 0)
                    {
                        skipped.Add($"{save.Name}: no save files in it once logs and caches are left out.");
                        continue;
                    }

                    var twin = firsts.FirstOrDefault(f => FileSet.SameContent(f.Files, snapshot.Files));
                    items.Add(new ImportItem(save.Name, save.Source, snapshot.Files.Count, FileSet.TotalSize(snapshot.Files), snapshot.Files.Max(f => f.ModifiedUtc),
                        twin.Name is null ? null : $"'{twin.Name}'"));
                    if (twin.Name is null)
                    {
                        firsts.Add((save.Name, snapshot.Files));
                    }
                }

                return new KeptLook(kept, items, skipped, kept.LiveBytes + kept.CopiesBytes);
            }, ct);
        }
        finally
        {
            LocalHistory.TryDelete(temp);
        }
    }

    /// <summary>
    /// Keeps only the live save, synced between PCs or, with <paramref name="backupOnly"/>, backed up only (KAN-63), and
    /// with <paramref name="bring"/> the copies beside it as named saves, as Import kept saves brings them in. Later
    /// scans don't offer the whole folder again. What goes wrong is thrown, for the dialog to say.
    /// </summary>
    /// <param name="progress">How far bringing the copies in is (KAN-80): each copy read, then each one kept.</param>
    public static async Task<KeptDone> ApplyAsync(string dataDir, GameId game, bool bring, bool backupOnly, IAgentOutput output, CancellationToken ct,
        IProgress<WorkProgress>? progress = null)
    {
        GameKept kept;
        string title;
        string rootKey;
        using (await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct))
        using (var engine = Engine.Open(dataDir))
        {
            var entry = Entry(engine, game);
            kept = Find(entry, engine.Here.Resolver) ?? throw Gone(entry);
            title = entry.DisplayTitle;
            var confirmed = Library.ConfirmLive(entry, kept.Root, kept.Live, backupOnly ? GameMode.BackupOnly : null, GameDefaults.Load(engine.State),
                (kept.LiveFiles, kept.LiveBytes, kept.LiveNewestUtc));
            var portable = confirmed.Confirmed!;
            if (Cli.Problems(portable, engine.Here.Resolver.Resolve(portable), engine.Here) is [var problem, ..])
            {
                throw new UsageException($"{title}'s live save can't be kept yet: {problem}");
            }

            rootKey = RootKey(portable, kept);
            engine.Library.SaveAll([confirmed]);
        }

        output.Say(backupOnly
            ? $"GameSync keeps {title}'s live save from now on, backed up on this PC and in the cloud, not synced between your PCs; the copies kept beside it aren't part of it."
            : $"{title} syncs its live save from now on; the copies kept beside it aren't part of it.");
        if (!bring)
        {
            return new KeptDone(0, []);
        }

        var report = await AppActions.KeptSavesAsync(dataDir, game, kept.Folder, rootKey, apply: true, output, ct, progress);
        return new KeptDone(report.Added, report.Skipped);
    }

    /// <summary>The live save as a portable folder ("&lt;installDir&gt;/Shadlix/user/savedata/1/CUSA00207/SPRJ0005").</summary>
    public static string LiveRoot(GameKept kept) => $"{kept.Root.TrimEnd('/')}/{kept.Live}";

    /// <summary>The root key the rules <see cref="Library.ConfirmLive"/> made give the live save, which its copies come in under.</summary>
    public static string RootKey(GameDefinition rules, GameKept kept) =>
        rules.Roots.First(r => r.Value.Equals(LiveRoot(kept), StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>Whether a scan's proposal is the whole folder the live save and its copies are in.</summary>
    public static bool IsWhole(Proposal proposal, GameKept kept) =>
        proposal is { Include: "**", Category: SaveCategory.Save } && proposal.Root.Equals(kept.Root, StringComparison.OrdinalIgnoreCase);

    /// <summary>"38 copies (989 MB)", "1 copy (20 MB)".</summary>
    public static string CopiesText(GameKept kept) =>
        $"{kept.Copies} {(kept.Copies == 1 ? "copy" : "copies")} ({FirstRun.Size(kept.CopiesBytes)})";

    private static LibraryEntry Entry(Engine engine, GameId game) =>
        engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null)
        ?? throw new UsageException($"GameSync doesn't know the game '{game}' on this PC.");

    private static UsageException Gone(LibraryEntry entry) =>
        new($"{entry.DisplayTitle}'s save folder doesn't hold copies kept beside its live save any more. Look at its saves again.");
}
