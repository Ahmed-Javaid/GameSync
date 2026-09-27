using GameSync.Core.Model;

namespace GameSync.Core.Sync;

public static class VersionGraph
{
    /// <summary>
    /// The current versions: Normal versions no other Normal version replaced (as its parent or through Supersedes).
    /// One means a clear line; more means two PCs uploaded from the same parent, a fork (SYNC-03). Newest first.
    /// </summary>
    public static IReadOnlyList<VersionRecord> Heads(IReadOnlyList<VersionRecord> versions)
    {
        var normal = versions.Where(v => v.Kind == VersionKind.Normal).ToList();
        var replaced = new HashSet<VersionId>();
        foreach (var version in normal)
        {
            if (version.Parent is { } parent)
            {
                replaced.Add(parent);
            }

            replaced.UnionWith(version.Supersedes);
        }

        return normal
            .Where(v => !replaced.Contains(v.Id))
            .OrderByDescending(v => v.CreatedUtc)
            .ThenByDescending(v => v.Id)
            .ToList();
    }
}
