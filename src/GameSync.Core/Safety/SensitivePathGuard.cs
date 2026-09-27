namespace GameSync.Core.Safety;

/// <summary>
/// R5: folders no save rule may reach: browser profiles, credential stores, keys, crypto wallets, Windows itself and
/// GameSync's own data. A rule's root may not be one of them, sit inside one, or contain one.
/// </summary>
public sealed class SensitivePathGuard
{
    private readonly IReadOnlyList<(string Path, string Reason)> _blocked;
    private readonly IReadOnlyList<string> _allowed;

    /// <param name="allowed">Folders inside a blocked one that are fine, such as GameSync's own registry exports.</param>
    public SensitivePathGuard(IEnumerable<(string Path, string Reason)> blocked, IEnumerable<string>? allowed = null)
    {
        _blocked = blocked
            .Where(b => !string.IsNullOrWhiteSpace(b.Path))
            .Select(b => (Normalize(b.Path), b.Reason))
            .ToList();
        _allowed = (allowed ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(Normalize).ToList();
    }

    /// <summary>Where GameSync writes the registry exports it backs up (FIND-10), inside its own data folder.</summary>
    public static string RegistryExports(string gameSyncDataDir) => Path.Combine(gameSyncDataDir, "registry");

    /// <param name="steamRoot">Steam's own folder, whose <c>config</c> holds its sign-in, when Steam is installed.</param>
    public static SensitivePathGuard ForThisPc(string gameSyncDataDir, string? steamRoot = null)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        const string Credentials = "it holds passwords or keys";
        const string SignIn = "it holds an app's sign-in";
        const string Browser = "it's a browser profile";
        const string Wallet = "it's a crypto wallet";

        return new SensitivePathGuard(
        [
            (Path.Combine(profile, ".ssh"), Credentials),
            (Path.Combine(profile, ".aws"), Credentials),
            (Path.Combine(profile, ".azure"), Credentials),
            (Path.Combine(profile, ".gnupg"), Credentials),
            (Path.Combine(profile, ".kube"), Credentials),
            (Path.Combine(profile, ".docker"), Credentials),
            (Path.Combine(profile, ".config", "rclone"), Credentials),
            (Path.Combine(profile, ".config", "gh"), Credentials),
            (Path.Combine(profile, ".config", "gcloud"), Credentials),
            (Path.Combine(roaming, "rclone"), Credentials),
            (Path.Combine(roaming, "gcloud"), Credentials),
            (Path.Combine(roaming, "GitHub CLI"), Credentials),
            (Path.Combine(roaming, "Bitwarden"), Credentials),
            (Path.Combine(roaming, "KeePass"), Credentials),
            (Path.Combine(roaming, "KeePassXC"), Credentials),
            (Path.Combine(local, "1Password"), Credentials),
            (Path.Combine(roaming, "discord"), SignIn),
            (Path.Combine(roaming, "Telegram Desktop"), SignIn),
            (Path.Combine(local, "EpicGamesLauncher", "Saved", "Config"), SignIn),
            (steamRoot is null ? "" : Path.Combine(steamRoot, "config"), SignIn),
            (Path.Combine(roaming, "Microsoft", "Credentials"), Credentials),
            (Path.Combine(local, "Microsoft", "Credentials"), Credentials),
            (Path.Combine(roaming, "Microsoft", "Protect"), Credentials),
            (Path.Combine(roaming, "Microsoft", "Crypto"), Credentials),
            (Path.Combine(roaming, "Microsoft", "SystemCertificates"), Credentials),
            (Path.Combine(local, "Microsoft", "Vault"), Credentials),
            (Path.Combine(local, "Google", "Chrome", "User Data"), Browser),
            (Path.Combine(local, "Microsoft", "Edge", "User Data"), Browser),
            (Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), Browser),
            (Path.Combine(local, "Chromium", "User Data"), Browser),
            (Path.Combine(local, "Vivaldi", "User Data"), Browser),
            (Path.Combine(roaming, "Mozilla", "Firefox"), Browser),
            (Path.Combine(roaming, "Opera Software"), Browser),
            (Path.Combine(roaming, "Bitcoin"), Wallet),
            (Path.Combine(roaming, "Electrum"), Wallet),
            (Path.Combine(roaming, "Exodus"), Wallet),
            (Path.Combine(roaming, "Ethereum"), Wallet),
            (Path.Combine(roaming, "atomic"), Wallet),
            (Path.Combine(roaming, "Litecoin"), Wallet),
            (windows, "it's the Windows folder"),
            (gameSyncDataDir, "it's GameSync's own data, including its sign-in tokens"),
        ],
        [RegistryExports(gameSyncDataDir)]);
    }

    /// <summary>Returns why <paramref name="folder"/> can't be a save root, or null when it can.</summary>
    public string? CheckRoot(string folder)
    {
        var path = Normalize(folder);
        if (Path.GetPathRoot(path) is { } driveRoot && Normalize(driveRoot) == path)
        {
            return "a whole drive is too broad for a save folder";
        }

        if (_allowed.Any(a => IsSameOrInside(path, a)))
        {
            return null;
        }

        foreach (var (blocked, reason) in _blocked)
        {
            if (IsSameOrInside(path, blocked))
            {
                return $"{folder} can't hold saves: {reason}";
            }

            if (IsSameOrInside(blocked, path))
            {
                return $"{folder} is too broad: it contains {blocked}, and {reason}";
            }
        }

        return null;
    }

    /// <summary>Defense in depth for single files found while scanning.</summary>
    public bool IsBlockedFile(string fullPath)
    {
        var path = Normalize(fullPath);
        return !_allowed.Any(a => IsSameOrInside(path, a)) && _blocked.Any(b => IsSameOrInside(path, b.Path));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();

    private static bool IsSameOrInside(string path, string folder) =>
        path == folder || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
