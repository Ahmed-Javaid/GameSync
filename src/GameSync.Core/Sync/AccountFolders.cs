using GameSync.Core.Model;
using GameSync.Core.Safety;

namespace GameSync.Core.Sync;

/// <summary>
/// A save someone shared keeps each file under the account folder it was made in: a Steam or store ID's folder, as a save
/// list's <c>&lt;storeUserId&gt;</c> leaves it in a game's rules. Restored as it is, it lands in a folder this PC's game
/// never reads, and this PC's own save, in its own account folder, was taken away to match it (the owner's friend's Dark
/// Souls, 8 Oct 2026). Restoring a shared save puts it in this PC's account folder instead, when there's one to put it in,
/// and never takes away another account's files.
/// </summary>
public static class AccountFolders
{
    /// <summary>An account folder's name: a Steam or store ID, eight or more hexadecimal digits ("76561198012345678", "0110000100000666").</summary>
    public static bool LooksLikeAccount(string name) => name.Length >= 8 && name.All(char.IsAsciiHexDigit);

    /// <summary>
    /// The shared save's files in this PC's account folder: for each folder of the game's saves where every shared file sits
    /// under one account folder and this PC's save under one other, the shared files move into this PC's. Anything else
    /// stays as it is.
    /// </summary>
    public static IReadOnlyList<FileEntry> OntoThisPc(IReadOnlyList<FileEntry> shared, IReadOnlyList<FileEntry> local)
    {
        var mapped = shared.ToList();
        foreach (var root in shared.Select(f => RestorePathGuard.Split(f.Path).RootKey).Distinct(StringComparer.Ordinal))
        {
            var theirs = Accounts(shared, root);
            var mine = Accounts(local, root);
            if (theirs is [{ } from] && mine is [{ } to] && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase) &&
                shared.Where(f => RestorePathGuard.Split(f.Path).RootKey == root).All(f => Account(f) is { } a && string.Equals(a, from, StringComparison.OrdinalIgnoreCase)))
            {
                mapped = mapped.Select(f => Account(f) is { } a && string.Equals(a, from, StringComparison.OrdinalIgnoreCase) && RestorePathGuard.Split(f.Path).RootKey == root
                    ? f with { Path = $"{root}/{to}/{RestorePathGuard.Split(f.Path).Relative[(from.Length + 1)..]}" }
                    : f).ToList();
            }
        }

        return mapped;
    }

    /// <summary>This PC's files in an account folder the shared save has nothing in: restoring it never takes them away.</summary>
    public static IReadOnlySet<string> OthersHere(IReadOnlyList<FileEntry> shared, IReadOnlyList<FileEntry> local)
    {
        var theirs = shared.Select(f => (RestorePathGuard.Split(f.Path).RootKey, Account(f)?.ToUpperInvariant())).ToHashSet();
        return local.Where(f => Account(f) is { } a && !theirs.Contains((RestorePathGuard.Split(f.Path).RootKey, a.ToUpperInvariant())))
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The account folders a folder of the game's saves holds files in.</summary>
    private static List<string> Accounts(IReadOnlyList<FileEntry> files, string root) =>
        files.Where(f => RestorePathGuard.Split(f.Path).RootKey == root).Select(Account).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The account folder a file sits in, the first folder under its save folder; null when that isn't one.</summary>
    private static string? Account(FileEntry file)
    {
        var relative = RestorePathGuard.Split(file.Path).Relative;
        var slash = relative.IndexOf('/');
        return slash > 0 && LooksLikeAccount(relative[..slash]) ? relative[..slash] : null;
    }
}
