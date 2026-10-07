namespace GameSync.Core.Sync;

/// <summary>
/// KAN-61: a game's live save with copies of it kept by hand beside it, inside a folder a scan proposed whole, as in
/// the owner's Bloodborne: <c>savedata\1\CUSA00207\SPRJ0005</c> beside <c>Before Orphan\SPRJ0005</c> and many more.
/// </summary>
/// <param name="Live">The live save's folder.</param>
/// <param name="Folder">The folder it and its copies are in (CUSA00207), where Import kept saves looks.</param>
/// <param name="Copies">How many copies are beside it, as Import kept saves counts them (a .zip is one).</param>
/// <param name="CopiesBytes">What the folder holds besides the live save.</param>
/// <param name="Names">The two copies made last, newest first, for a sentence ("such as Before Orphan and After maria").</param>
public sealed record KeptBeside(string Live, string Folder, int Copies, long CopiesBytes, int LiveFiles, long LiveBytes, DateTime? LiveNewestUtc,
    IReadOnlyList<string> Names);

public static class KeptCopies
{
    // How far down a proposed folder the live save is looked for, and how many folders at most: a save folder is small,
    // and a folder too big to look through quickly isn't one.
    private const int MaxDepth = 6;
    private const int MaxFolders = 5000;

    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
    private static readonly EnumerationOptions Here = new() { AttributesToSkip = FileAttributes.ReparsePoint };

    /// <summary>
    /// The live save inside <paramref name="root"/>: a folder with at least two folders beside it that each hold a folder
    /// of its name (<c>Before Orphan\SPRJ0005</c>, or one level further in); when several folders look so, the one with
    /// the most. Null when there's none.
    /// </summary>
    public static KeptBeside? Find(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        (string Parent, string Leaf, int Holders)? best = null;
        var queue = new Queue<(string Folder, int Depth)>([(root, 0)]);
        for (var looked = 0; queue.Count > 0 && looked < MaxFolders; looked++)
        {
            var (folder, depth) = queue.Dequeue();
            List<string> children;
            try
            {
                children = Directory.EnumerateDirectories(folder, "*", Here).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var held = children.ToDictionary(c => c, NamesIn);
            foreach (var child in children)
            {
                var leaf = Path.GetFileName(child);
                var holders = children.Count(other => other != child && held[other].Contains(leaf));
                if (holders >= 2 && (best is null || holders > best.Value.Holders))
                {
                    best = (folder, leaf, holders);
                }
            }

            if (depth < MaxDepth)
            {
                foreach (var child in children)
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        if (best is not { } found)
        {
            return null;
        }

        var live = Path.Combine(found.Parent, found.Leaf);
        var (copies, _) = NamedSaveFinder.Find(found.Parent, live, tempFolder: null);
        var liveFiles = Files(live);
        var all = Files(found.Parent);
        var names = copies
            .Select(c => (c.Name, When: File.Exists(c.Folder) ? File.GetLastWriteTimeUtc(c.Folder) : Newest(Files(c.Folder))))
            .OrderByDescending(c => c.When)
            .Take(2)
            .Select(c => c.Name)
            .ToList();
        return new KeptBeside(live, found.Parent, copies.Count, all.Sum(f => f.Length) - liveFiles.Sum(f => f.Length),
            liveFiles.Count, liveFiles.Sum(f => f.Length), liveFiles.Count > 0 ? Newest(liveFiles) : null, names);
    }

    /// <summary>The names of the folders in <paramref name="folder"/>, directly or one folder in: a copy beside the live save holds one of its name.</summary>
    private static HashSet<string> NamesIn(string folder)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var inner in Directory.EnumerateDirectories(folder, "*", Here))
            {
                names.Add(Path.GetFileName(inner));
                foreach (var deeper in Directory.EnumerateDirectories(inner, "*", Here))
                {
                    names.Add(Path.GetFileName(deeper));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder it can't read holds nothing it can count.
        }

        return names;
    }

    private static List<FileInfo> Files(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", Everything).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DateTime Newest(List<FileInfo> files) => files.Count == 0 ? DateTime.MinValue : files.Max(f => f.LastWriteTimeUtc);
}
