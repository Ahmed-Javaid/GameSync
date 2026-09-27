namespace GameSync.Core.Model;

/// <summary>Comparisons between file lists. Paths compare without case, as Windows does.</summary>
public static class FileSet
{
    public static bool SameContent(IReadOnlyCollection<FileEntry> a, IReadOnlyCollection<FileEntry> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var hashes = ToMap(b);
        return a.All(f => hashes.TryGetValue(f.Path, out var hash) && hash == f.Hash);
    }

    /// <summary>Files in <paramref name="now"/> that are new or whose contents differ from <paramref name="before"/>.</summary>
    public static IReadOnlyList<FileEntry> ChangedOrAdded(IReadOnlyCollection<FileEntry> now, IReadOnlyCollection<FileEntry> before)
    {
        var old = ToMap(before);
        return now.Where(f => !old.TryGetValue(f.Path, out var hash) || hash != f.Hash).ToList();
    }

    public static IReadOnlyList<FileEntry> Removed(IReadOnlyCollection<FileEntry> now, IReadOnlyCollection<FileEntry> before)
    {
        var current = ToMap(now);
        return before.Where(f => !current.ContainsKey(f.Path)).ToList();
    }

    public static long TotalSize(IEnumerable<FileEntry> files) => files.Sum(f => f.Size);

    /// <summary>The newest modified time among files that changed since <paramref name="before"/>, or null when nothing did.</summary>
    public static DateTime? NewestChange(IReadOnlyCollection<FileEntry> now, IReadOnlyCollection<FileEntry> before)
    {
        var changed = ChangedOrAdded(now, before);
        return changed.Count == 0 ? null : changed.Max(f => f.ModifiedUtc);
    }

    private static Dictionary<string, BlobId> ToMap(IEnumerable<FileEntry> files)
    {
        var map = new Dictionary<string, BlobId>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            map[file.Path] = file.Hash;
        }

        return map;
    }
}
