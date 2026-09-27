using System.Globalization;

namespace GameSync.Core.Discovery;

/// <summary>When a Steam game was last played on this PC's accounts, and for how long in all.</summary>
public sealed record SteamPlay(long AppId, DateTime? LastPlayedUtc, TimeSpan Playtime);

/// <summary>
/// Steam's own record of play, from each account's <c>userdata\&lt;id&gt;\config\localconfig.vdf</c>: last played and
/// total playtime per app. It fills "last played" and hours for Steam games before GameSync has seen a session of its
/// own (PLAY-09). Across accounts, the latest play counts and the times add up. Only these two values are read.
/// </summary>
public static class SteamActivity
{
    public static IReadOnlyDictionary<long, SteamPlay> Read(string steamRoot)
    {
        var plays = new Dictionary<long, SteamPlay>();
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
        {
            return plays;
        }

        foreach (var account in Directory.EnumerateDirectories(userdata))
        {
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

                plays[appId] = plays.TryGetValue(appId, out var seen)
                    ? new SteamPlay(appId, Latest(seen.LastPlayedUtc, lastPlayed), seen.Playtime + TimeSpan.FromMinutes(minutes))
                    : new SteamPlay(appId, lastPlayed, TimeSpan.FromMinutes(minutes));
            }
        }

        return plays;
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
