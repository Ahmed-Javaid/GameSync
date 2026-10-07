using System.Globalization;

namespace GameSync.Host;

public static partial class Cli
{
    /// <summary>
    /// ACH-01 to ACH-03: a game's achievements on this PC, from Steam's own files, as its achievements page shows them
    /// (hidden ones kept secret until unlocked), from a copy's own record in a folder added with
    /// <c>add-achievement-folder</c> (KAN-123), or Steam's list at 0 when Steam here keeps none of it (KAN-111); or, with
    /// no game, how far each game here is, its Zenith and its tiers (design system version 33). <c>--fetch</c> asks Steam
    /// for the list, icons and rarity it doesn't hold here.
    /// </summary>
    private static async Task<int> AchievementsAsync(string dataDir, List<string> rest)
    {
        if (rest.FirstOrDefault() == "popup")
        {
            return await PopupAsync(dataDir, rest.Skip(1).ToList());
        }

        var fetch = rest.Remove("--fetch");
        var (games, _) = LauncherData.Read(dataDir, DateTime.Now);
        var wanted = rest.Count > 0 ? GameSync.Core.Model.GameId.Parse(rest[0]) : (GameSync.Core.Model.GameId?)null;
        var leftOut = Achievements.LeftOut(dataDir);
        var shown = 0;
        foreach (var game in games.Where(g => wanted is null ? g.Shown : g.Id == wanted).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
        {
            // One game asked for: Steam's list at 0 when Steam here keeps none of it (KAN-111), asked for with --fetch.
            if (fetch && wanted is not null)
            {
                await Achievements.FetchForAsync(dataDir, game.Id, game.Title, game.Store, game.SteamAppId, CancellationToken.None);
            }

            // Every game: only those whose achievements are tracked, as the Achievements page counts them.
            if (Achievements.For(dataDir, game) is not { } view || (wanted is null && !view.IsTracked))
            {
                if (wanted is not null)
                {
                    Console.WriteLine(game.SteamAppId is null
                        ? $"{game.Title} has no achievements GameSync can read: it reads Steam's, and copies' own records named from Steam's list."
                        : $"{game.Title}'s list of achievements comes from Steam: --fetch asks for it.");
                }

                continue;
            }

            if (view.Total == 0)
            {
                Console.WriteLine($"{game.Title} has no achievements on Steam: Steam lists none for it.");
                continue;
            }

            if (fetch && await Achievements.FetchAsync(dataDir, view, CancellationToken.None, every: wanted is not null))
            {
                view = Achievements.For(dataDir, game)!;
            }

            var from = view.From switch
            {
                AchievementsFrom.ListNotSteamCopy => " (Steam's list: this copy isn't Steam's, so what you unlock in it doesn't show)",
                AchievementsFrom.ListNotPlayedHere => " (Steam's list: Steam on this PC hasn't seen you play it)",
                AchievementsFrom.CopyRecord => leftOut.Contains(game.Id) ? " (this copy's own record; left out of your achievements)" : " (this copy's own record)",
                _ => leftOut.Contains(game.Id) ? " (left out of your achievements: no popups, not counted)" : "",
            };
            shown++;
            var progress = view.IsComplete
                ? $"Zenith, {(view.Total == 1 ? "its one achievement" : $"every one of {view.Total}")} unlocked{(view.CompletedUtc is { } done ? $" ({done.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)})" : "")}"
                : $"{view.Unlocked} of {view.Total} unlocked ({Math.Floor(view.Percent).ToString(CultureInfo.InvariantCulture)}%), {view.Total - view.Unlocked} to go for its Zenith";
            var tiers = view.All.Any(a => a.Tier is not null)
                ? $"; {view.Count(AchievementTier.Gold)} Gold, {view.Count(AchievementTier.Silver)} Silver, {view.Count(AchievementTier.Bronze)} Bronze"
                : "";
            Console.WriteLine($"{game.Title}: {progress}{tiers}{(view.Rarest is { } rarest ? $"; rarest {rarest.Name}, {rarest.Percent!.Value.ToString("0.#", CultureInfo.InvariantCulture)}% of players" : "")}{from}");
            if (wanted is not null)
            {
                foreach (var a in view.All.OrderByDescending(a => a.UnlockedUtc ?? DateTime.MinValue))
                {
                    var when = a.KnownUnlockUtc is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : a.Unlocked ? "unlocked" : "locked";
                    var name = a.Secret ? "Hidden achievement (secret until unlocked)" : a.Name;
                    var tier = a is { Unlocked: true, Tier: { } metal } ? $"  {AchievementTiers.Word(metal)}" : "";
                    Console.WriteLine($"  {when,-16}  {name}{(a.Percent is { } p ? $"  {p.ToString("0.#", CultureInfo.InvariantCulture)}%" : "")}{tier}");
                }
            }
        }

        if (wanted is null && shown == 0)
        {
            Console.WriteLine("No game here has achievements GameSync can read: Steam copies that Steam has run on this PC do, and copies with their own record in a folder added with add-achievement-folder.");
        }

        return 0;
    }

    /// <summary>
    /// ACH-09 (the owner, 3 Oct 2026: "Is there a way I can test the achievement unlocked popup in game?"): asks the
    /// running app to show the popup with its chime: one of your own unlocked achievements (of <c>--tier</c> gold, silver,
    /// bronze or hidden when given; zenith shows a Zenith's), after <c>--in</c> seconds so you can switch to a game first.
    /// The popup is the app's own window, so the app must be running.
    /// </summary>
    private static async Task<int> PopupAsync(string dataDir, List<string> rest)
    {
        string? Value(string flag)
        {
            var at = rest.IndexOf(flag);
            return at >= 0 && at + 1 < rest.Count ? rest[at + 1] : null;
        }

        var tier = Value("--tier") ?? "any";
        if (tier is not ("any" or "gold" or "silver" or "bronze" or "hidden" or "zenith"))
        {
            Console.Error.WriteLine("--tier is gold, silver, bronze, hidden or zenith.");
            return 2;
        }

        var seconds = Value("--in") is { } text ? int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1 : 0;
        if (seconds is < 0 or > 120)
        {
            Console.Error.WriteLine("--in is a number of seconds, up to 120.");
            return 2;
        }

        var reply = await AppPipe.SendAsync(dataDir, $"popup {tier} {seconds.ToString(CultureInfo.InvariantCulture)}", TimeSpan.FromSeconds(2));
        switch (reply)
        {
            case null or "unknown":
                Console.Error.WriteLine("The popup is GameSync's own window: start GameSync (GameSync.Tray.exe), then try again.");
                return 1;
            case "ok":
                var chime = AchievementPopupSettings.Read(dataDir).Sound ? ", with its chime" : " (its chime is off in Settings)";
                Console.WriteLine(seconds > 0 ? $"The popup shows in {seconds} seconds{chime}: switch to your game now." : $"The popup shows now{chime}.");
                return 0;
            default:
                Console.Error.WriteLine(reply);
                return 1;
        }
    }
}
