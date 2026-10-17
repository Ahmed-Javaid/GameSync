using System.Globalization;

namespace GameSync.Core.Discovery;

/// <summary>When a Steam game was last played on this PC's accounts, and for how long in all.</summary>
public sealed record SteamPlay(long AppId, DateTime? LastPlayedUtc, TimeSpan Playtime);

/// <summary>
/// Steam's own record of play, from each account's <c>userdata\&lt;id&gt;\config\localconfig.vdf</c>: last played and
/// total playtime per app. It fills "last played" and hours for Steam games before GameSync has seen a session of its
/// own (PLAY-09). Only these two values are read.
/// </summary>
/// <remarks>
/// A PC can hold several accounts (the owner's friend, 8 Oct 2026: Deadlock showed 1,810 hours for 868 played, two
/// accounts' times added up). A game's is the account Steam signs in with, as Steam's own library shows it, or when that
/// one has never played it, the account that played it last; never a sum.
/// </remarks>
public static class SteamActivity
{
    public static IReadOnlyDictionary<long, SteamPlay> Read(string steamRoot)
    {
        var plays = new Dictionary<long, (SteamPlay Play, bool SignedIn)>();
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
        {
            return new Dictionary<long, SteamPlay>();
        }

        var signedIn = SignedIn(steamRoot);
        foreach (var account in Directory.EnumerateDirectories(userdata))
        {
            var isSignedIn = signedIn is not null && string.Equals(Path.GetFileName(account), signedIn, StringComparison.Ordinal);
            if (Vdf.TryReadRoot(Path.Combine(account, "config", "localconfig.vdf"))?.Block("Software")?.Block("Valve")?.Block("Steam")?.Block("apps") is not { } apps)
            {
                continue;
            }

            foreach (var (key, app) in apps.Blocks())
            {
                if (!long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
                {
                    continue;
                }

                DateTime? lastPlayed = long.TryParse(app["LastPlayed"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                    : null;
                var minutes = long.TryParse(app["Playtime"], NumberStyles.None, CultureInfo.InvariantCulture, out var played) ? played : 0;
                if (lastPlayed is null && minutes == 0)
                {
                    continue;
                }

                var play = new SteamPlay(appId, lastPlayed, TimeSpan.FromMinutes(minutes));
                if (!plays.TryGetValue(appId, out var seen) || Better((play, isSignedIn), seen))
                {
                    plays[appId] = (play, isSignedIn);
                }
            }
        }

        return plays.ToDictionary(p => p.Key, p => p.Value.Play);
    }

    /// <summary>The account Steam signs in with, by its <c>userdata</c> folder's name; null when Steam doesn't say.</summary>
    private static string? SignedIn(string steamRoot) => SteamReader.Accounts(steamRoot).FirstOrDefault(a => a.MostRecent)?.AccountId;

    /// <summary>The signed-in account's record first, then the one played last, then the one played longest.</summary>
    private static bool Better((SteamPlay Play, bool SignedIn) a, (SteamPlay Play, bool SignedIn) b)
    {
        if (a.SignedIn != b.SignedIn)
        {
            return a.SignedIn;
        }

        var (x, y) = (a.Play.LastPlayedUtc ?? DateTime.MinValue, b.Play.LastPlayedUtc ?? DateTime.MinValue);
        return x != y ? x > y : a.Play.Playtime > b.Play.Playtime;
    }

    /// <summary>
    /// The launch options the person set for a game in Steam (its Properties, General), which Steam adds to every start:
    /// the signed-in account's, else those of the account that played it last. Read only; they're Steam's to change (PLAY-11).
    /// </summary>
    public static string? LaunchOptions(string steamRoot, long appId)
    {
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
        {
            return null;
        }

        var key = appId.ToString(CultureInfo.InvariantCulture);
        var signedIn = SignedIn(steamRoot);
        (string Options, long Played)? best = null;
        foreach (var account in Directory.EnumerateDirectories(userdata))
        {
            var app = Vdf.TryReadRoot(Path.Combine(account, "config", "localconfig.vdf"))?.Block("Software")?.Block("Valve")?.Block("Steam")?.Block("apps")?.Block(key);
            if (app?["LaunchOptions"] is not { Length: > 0 } options)
            {
                continue;
            }

            // The signed-in account's own, whatever the others played.
            var played = signedIn is not null && string.Equals(Path.GetFileName(account), signedIn, StringComparison.Ordinal) ? long.MaxValue
                : long.TryParse(app["LastPlayed"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
            if (best is null || played > best.Value.Played)
            {
                best = (options.Trim(), played);
            }
        }

        return best?.Options is { Length: > 0 } found ? found : null;
    }

}
