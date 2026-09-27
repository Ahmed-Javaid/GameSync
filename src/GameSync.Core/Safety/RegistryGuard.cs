namespace GameSync.Core.Safety;

/// <summary>R7: registry saves restore only under the game's own approved key in HKCU\Software, never an autostart key.</summary>
public static class RegistryGuard
{
    private static readonly string[] Forbidden =
    [
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNONCE",
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNSERVICES",
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNSERVICESONCE",
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\POLICIES",
        @"SOFTWARE\MICROSOFT\WINDOWS NT\CURRENTVERSION\WINLOGON",
        @"SOFTWARE\MICROSOFT\WINDOWS NT\CURRENTVERSION\WINDOWS",
        @"SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\EXPLORER",
        @"SOFTWARE\CLASSES",
        @"SOFTWARE\POLICIES",
    ];

    /// <summary>Returns why a key from a version can't be restored, or null when it can.</summary>
    public static string? Check(string keyPath, IEnumerable<string> approvedKeys)
    {
        if (Normalize(keyPath) is not { } key)
        {
            return $"'{keyPath}' isn't under HKEY_CURRENT_USER\\Software.";
        }

        if (Forbidden.Any(f => IsSameOrInside(key, f)))
        {
            return $"'{keyPath}' is a Windows startup or system key.";
        }

        var approved = approvedKeys.Select(Normalize).OfType<string>().Where(a => !Forbidden.Any(f => IsSameOrInside(a, f)));
        return approved.Any(a => IsSameOrInside(key, a)) ? null : $"'{keyPath}' isn't one of this game's approved keys.";
    }

    /// <summary>Whether two spellings name the same key under HKEY_CURRENT_USER\Software, like "HKCU\Software\X" and "HKEY_CURRENT_USER/Software/x".</summary>
    public static bool SameKey(string a, string b) => Normalize(a) is { } first && first == Normalize(b);

    /// <summary>"HKCU\Software\X" or "HKEY_CURRENT_USER\Software\X" as "SOFTWARE\X"; null for anything else.</summary>
    private static string? Normalize(string keyPath)
    {
        var parts = keyPath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts.Any(p => p is "." or ".."))
        {
            return null;
        }

        var hive = parts[0].ToUpperInvariant();
        if (hive is not ("HKCU" or "HKEY_CURRENT_USER") || !parts[1].Equals("Software", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Join('\\', parts[1..]).ToUpperInvariant();
    }

    private static bool IsSameOrInside(string key, string parent) =>
        key == parent || key.StartsWith(parent + "\\", StringComparison.Ordinal);
}
