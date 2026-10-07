using Avalonia.Media;
using GameSync.Core.Art;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI;
using GameSync.UI.Controls;
using GameSync.UI.Views;
using GameSync.Windows;

namespace GameSync.Tray;

/// <summary>
/// ACH-09, ACH-10 (the owner, 3 Oct 2026: "as someone is playing a game they need to be able to see a popup and a nice
/// sound to show they've unlocked an achievement"): what an unlock seen by <see cref="AchievementWatch"/> becomes. The
/// popup over the game in the corner picked, one at a time, with its chime; a game left out gets nothing; in a game
/// running in exclusive fullscreen, where no window can show over it, or one with an anti-cheat, over which GameSync draws
/// nothing (R13), only the chime plays and the unlocks wait for one notification when the game closes. Settings' Try the popup and <c>gamesync achievements popup</c> show one on
/// purpose. Everything here runs on the UI thread.
/// </summary>
internal sealed class AchievementPopups
{
    /// <summary>How long a popup stays; a Zenith's longer.</summary>
    public static readonly TimeSpan Shown = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan ZenithShown = TimeSpan.FromSeconds(8);

    private readonly string _dataDir;
    private readonly Func<IReadOnlyList<LauncherGame>> _games;
    private readonly Queue<PopupItem> _queue = new();
    private readonly Dictionary<long, List<AchievementUnlock>> _held = [];
    private AchievementPopupSettings _settings = new();
    private IReadOnlySet<GameId> _leftOut = new HashSet<GameId>();
    private IReadOnlyDictionary<GameId, bool> _popupChoices = new Dictionary<GameId, bool>();
    private IReadOnlySet<GameId> _antiCheat = new HashSet<GameId>();
    private bool _showing;

    public AchievementPopups(string dataDir, Func<IReadOnlyList<LauncherGame>> games)
    {
        _dataDir = dataDir;
        _games = games;
        Reload();
    }

    /// <summary>What a popup is: its words, its metal and its icon, and the chime it plays.</summary>
    private sealed record PopupItem(string Eyebrow, string Title, string? Meta, string? Tier, IImage? Icon, int Done, int Total, string Chime, bool IsZenith);

    /// <summary>The settings, the games left out, each game's popup and the games with an anti-cheat, read again after Settings changes them.</summary>
    public void Reload()
    {
        _settings = AchievementPopupSettings.Read(_dataDir);
        _leftOut = Achievements.LeftOut(_dataDir);
        _popupChoices = Achievements.PopupChoices(_dataDir);
        _antiCheat = Achievements.AntiCheatGames(_dataDir);
    }

    /// <summary>An achievement just unlocked in a running game.</summary>
    public async void Unlocked(AchievementUnlock unlock)
    {
        // A launcher shows its own popup for the games it runs, so GameSync's is off for them unless turned on for that game
        // in Achievements by game (KAN-122, KAN-131). An unlock in a copy's own record (KAN-123) has no launcher showing one,
        // so GameSync's is on unless turned off.
        if (_leftOut.Contains(unlock.Game) || (!_settings.Popups && !_settings.Sound) || !Achievements.PopupOn(_popupChoices, unlock.Game, unlock.BySteam))
        {
            return;
        }

        // R13 (design system version 51): nothing is drawn over a game with an anti-cheat, as over one in exclusive
        // fullscreen: the chime plays, and its unlocks wait for one notification when it closes.
        var chime = unlock.IsZenith ? "zenith" : unlock.Tier ?? "bronze";
        if (!_settings.Popups || _antiCheat.Contains(unlock.Game) || FullScreen.IsExclusive())
        {
            if (_settings.Sound)
            {
                Chime.Play(chime, _settings.Chime, _settings.Volume);
            }

            if (_settings.Popups)
            {
                // Exclusive fullscreen, or an anti-cheat: told when the game closes.
                (_held.TryGetValue(unlock.AppId, out var held) ? held : _held[unlock.AppId] = []).Add(unlock);
            }

            return;
        }

        var icon = await IconOf(unlock.AppId, unlock.Achievement);
        Enqueue(ItemOf(unlock, icon));
        if (unlock.IsZenith)
        {
            Enqueue(ZenithOf(unlock.GameTitle, unlock.Total));
        }
    }

    /// <summary>A game closed: the unlocks held while it ran in exclusive fullscreen, in one notification.</summary>
    public void Ended(long app)
    {
        if (!_held.Remove(app, out var held) || held.Count == 0)
        {
            return;
        }

        var game = held[0].GameTitle;
        var names = string.Join(", ", held.Select(u => u.Achievement.Name));
        Toasts.Show(held.Count == 1 ? $"Achievement unlocked in {game}" : $"{held.Count} achievements unlocked in {game}",
            held.Any(u => u.IsZenith) ? $"{names}. Every achievement: its Zenith." : names);
    }

    /// <summary>
    /// Try the popup (KAN-110): after <paramref name="delay"/>, so there's time to switch to a game, one of the person's
    /// own unlocked achievements, of <paramref name="tier"/> when given (<c>gold</c>, <c>silver</c>, <c>bronze</c>,
    /// <c>hidden</c>, <c>zenith</c>), or a made-up one. It shows whatever the popup's setting, as it was asked for.
    /// </summary>
    public async void Test(string? tier, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

        var games = _games();
        var (item, appId, achievement) = await Task.Run(() => Sample(games, tier));
        if (achievement is not null && appId is { } app)
        {
            item = item with { Icon = await IconOf(app, achievement) };
        }

        Enqueue(item);
    }

    /// <summary>One of the person's unlocked achievements to show as a sample, or a made-up one.</summary>
    private (PopupItem Item, long? AppId, AchievementShown? Achievement) Sample(IReadOnlyList<LauncherGame> games, string? tier)
    {
        var views = Achievements.ForAll(_dataDir, games).Where(v => v.Unlocked > 0).OrderByDescending(v => v.LastUnlockUtc ?? DateTime.MinValue).ToList();
        if (tier == "zenith")
        {
            var finished = views.FirstOrDefault(v => v.IsComplete);
            return (ZenithOf(finished?.Title ?? "Your game", finished?.Total ?? 50), null, null);
        }

        foreach (var view in views)
        {
            var pick = view.All.Where(a => a.Unlocked && (tier switch
            {
                "gold" => a.Tier == AchievementTier.Gold,
                "silver" => a.Tier == AchievementTier.Silver,
                "bronze" => a.Tier == AchievementTier.Bronze,
                "hidden" => a.Hidden,
                _ => true,
            })).OrderBy(a => a.Percent ?? 100).FirstOrDefault();
            if (pick is not null)
            {
                var title = games.FirstOrDefault(g => g.Id == view.Game)?.Title ?? view.Title;
                return (ItemOf(new AchievementUnlock(view.Game, title, view.AppId, pick, view.Unlocked, view.Total), null), view.AppId, pick);
            }
        }

        var made = tier is "silver" or "bronze" ? tier : "gold";
        return (new PopupItem("ACHIEVEMENT UNLOCKED", "Popups work", $"{AchievementTiers.Word(made == "silver" ? AchievementTier.Silver : made == "bronze" ? AchievementTier.Bronze : AchievementTier.Gold)} · how one shows while you play",
            made, null, 124, 171, made, false), null, null);
    }

    private static PopupItem ItemOf(AchievementUnlock unlock, IImage? icon)
    {
        var a = unlock.Achievement;
        var meta = a.Percent is { } percent && a.Tier is { } tier
            ? $"{AchievementTiers.Word(tier)} · {AchievementTiers.Rarity(percent)} · {PercentOf(percent)} of players"
            : unlock.GameTitle;
        return new PopupItem(a.Hidden ? "HIDDEN ACHIEVEMENT UNLOCKED" : "ACHIEVEMENT UNLOCKED", a.Name, meta, unlock.Tier, icon, unlock.Done, unlock.Total,
            unlock.Tier ?? "bronze", false);
    }

    private static PopupItem ZenithOf(string game, int total) =>
        new("ZENITH REACHED", game, $"Every achievement, {total} of {total}", "zenith", null, total, total, "zenith", true);

    private static string PercentOf(double value) =>
        (value < 10 ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture)) + "%";

    /// <summary>Its icon, kept on this PC; one not kept yet is asked of Steam, for a second and a half at most, so the popup isn't late.</summary>
    private async Task<IImage?> IconOf(long app, AchievementShown achievement)
    {
        if (achievement.Icon is { } kept)
        {
            return ArtImages.Load(kept, 64);
        }

        if (achievement.IconFile is not { } file)
        {
            return null;
        }

        return await Task.Run(async () =>
        {
            try
            {
                using var soon = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                using var cache = new AchievementCache(_dataDir);
                await cache.FetchIconsAsync(app, [file], soon.Token);
                return ArtImages.Load(cache.Icon(app, file), 64);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
            {
                return null;
            }
        });
    }

    private void Enqueue(PopupItem item)
    {
        _queue.Enqueue(item);
        if (!_showing)
        {
            _ = ShowAll();
        }
    }

    /// <summary>Each popup in turn: shown with its chime, held, then slid away; the next follows.</summary>
    private async Task ShowAll()
    {
        _showing = true;
        try
        {
            while (_queue.TryDequeue(out var item))
            {
                var popup = new GsAchievementPopup
                {
                    Eyebrow = item.Eyebrow,
                    Title = item.Title,
                    Meta = item.Meta,
                    Tier = item.Tier,
                    Icon = item.Icon,
                    Done = item.Done,
                    Total = item.Total,
                };
                var window = new AchievementPopupWindow(popup, _settings.Corner);
                if (_settings.Sound)
                {
                    Chime.Play(item.Chime, _settings.Chime, _settings.Volume);
                }

                await window.ShowInCornerAsync();
                await Task.Delay(item.IsZenith ? ZenithShown : Shown);
                await window.HideAsync();
                await Task.Delay(250);
            }
        }
        finally
        {
            _showing = false;
        }
    }
}
