using System.Collections.Frozen;

namespace GameSync.Core.Safety;

public sealed class UnsafePathException(string message) : Exception(message);

/// <summary>
/// R6 and R8: every path read from a version is untrusted. It must be a plain relative path under a known root key,
/// and resolve inside that root without "..", absolute paths, alternate data streams, reserved names or links.
/// </summary>
public static class RestorePathGuard
{
    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",

        // Windows reserves these with superscript digits too (the safety audit, 7 Oct 2026).
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<char> InvalidNameChars = Path.GetInvalidFileNameChars().Append(':').ToFrozenSet();

    /// <summary>Splits a portable path ("saves/slot1.sav") into its root key and the checked relative part.</summary>
    public static (string RootKey, string Relative) Split(string portablePath)
    {
        var slash = portablePath.IndexOf('/');
        if (slash <= 0 || slash == portablePath.Length - 1)
        {
            throw new UnsafePathException($"'{portablePath}' has no root folder.");
        }

        var rootKey = portablePath[..slash];
        if (!IsSafeRootKey(rootKey))
        {
            throw new UnsafePathException($"'{rootKey}' isn't a valid root folder name.");
        }

        var relative = portablePath[(slash + 1)..];
        CheckRelative(relative);
        return (rootKey, relative);
    }

    public static bool IsSafeRootKey(string key) =>
        key.Length is > 0 and <= 40 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static void CheckRelative(string relative)
    {
        if (relative.Length is 0 or > 1024)
        {
            throw new UnsafePathException("The path is empty or too long.");
        }

        if (relative.Contains('\\'))
        {
            throw new UnsafePathException($"'{relative}' uses '\\'; versions store paths with '/'.");
        }

        foreach (var segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new UnsafePathException($"'{relative}' has an empty, '.' or '..' part.");
            }

            if (segment.Any(c => InvalidNameChars.Contains(c) || char.IsControl(c)))
            {
                throw new UnsafePathException($"'{relative}' has a character Windows doesn't allow in names, or an alternate data stream.");
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                throw new UnsafePathException($"'{relative}' has a name ending in a dot or space.");
            }

            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedNames.Contains(stem))
            {
                throw new UnsafePathException($"'{relative}' uses the reserved name '{stem}'.");
            }
        }
    }

    /// <summary>The full path for <paramref name="relative"/> under <paramref name="root"/>, after checking it stays inside.</summary>
    public static string Resolve(string root, string relative)
    {
        CheckRelative(relative);
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafePathException($"'{relative}' resolves outside its save folder.");
        }

        // A junction or symlink below the root could send the write somewhere else entirely.
        var current = new DirectoryInfo(Path.GetDirectoryName(full)!);
        while (current is not null && current.FullName.Length > rootFull.Length)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new UnsafePathException($"'{relative}' goes through a link ({current.FullName}).");
            }

            current = current.Parent;
        }

        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnsafePathException($"'{relative}' is a link.");
        }

        return full;
    }
}
