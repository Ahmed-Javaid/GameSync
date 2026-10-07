using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>A place learn mode found, on the Learn mode card: as every PC reads it, what was written, and its likeliest tag.</summary>
public sealed record LearnPlaceRow(string Portable, string Meta, string? Tag, string Path)
{
    public bool HasTag => Tag is not null;
}

/// <summary>
/// FIND-04 on a game's saves (design system version 43 → GameSavesScreen #rounds): learn mode waiting for the game's next
/// session, watching it, or what it found, with See what it found…; Turn off learn mode, or on again. The status line says
/// the same in a sentence, and See what it found is its button once there's something to pick.
/// </summary>
public sealed partial class GameSavesViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLearn), nameof(LearnSubtitle), nameof(LearnNote), nameof(HasLearnNote), nameof(LearnWatching), nameof(LearnFound),
        nameof(LearnPlaces), nameof(LearnMore), nameof(HasLearnMore), nameof(LearnCanTurnOff), nameof(LearnCanTurnOn), nameof(SeesLearnFinds),
        nameof(NothingFoundNote), nameof(LearnNothingFoundNote), nameof(ChoosesFiles))]
    private LearnView _learn = LearnView.None;

    /// <summary>The Learn mode card: while it waits, watches or found something, or for a game nothing was found for, to turn it on.</summary>
    public bool ShowsLearn => Learn.State is LearnState.Waiting or LearnState.Watching or LearnState.Found or LearnState.NothingFound
        || (NotSyncing && NothingFound && Learn.State == LearnState.Off);

    public string LearnSubtitle => Learn.State switch
    {
        LearnState.Waiting => "Watches the next time you play.",
        LearnState.Watching => $"Watching since {Learn.WatchingSinceUtc?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}.",
        LearnState.Found => $"Watched {SessionText(Learn)}.",
        LearnState.NothingFound => $"Watched {DayText(Learn)}; nothing like a save was written.",
        _ => "Off for this game.",
    };

    public string? LearnNote => Learn.State switch
    {
        LearnState.Waiting => $"Play {Title} as usual, from GameSync, its store or a shortcut. While it runs, GameSync notes the files written in your usual save folders, its own folder and Steam's; when you quit, it shows you where they went. Nothing syncs until you choose.",
        LearnState.Watching => "Watching your usual save folders, its own folder and Steam's for the files it writes. It only notes where files change; it never touches the game.",
        LearnState.NothingFound => "It saw nothing written that looks like a save, and watches again the next time you play. Add a place… sets where its saves are if you know.",
        LearnState.Off => $"Turned on, learn mode watches the next time you play {Title} and shows you the places it wrote to.",
        _ => null,
    };

    public bool HasLearnNote => LearnNote is not null;

    /// <summary>It watches now: the card shows its spinner.</summary>
    public bool LearnWatching => Learn.State == LearnState.Watching;

    public bool LearnFound => Learn.State == LearnState.Found;

    /// <summary>The likeliest two places it found, on the card.</summary>
    public IReadOnlyList<LearnPlaceRow> LearnPlaces => Learn.State == LearnState.Found && Learn.Finds is { } finds
        ? finds.Places.Take(2).Select(p => new LearnPlaceRow(p.Portable,
            $"{(p.IsFile ? "One file" : p.Files == 1 ? "1 file" : $"{p.Files.ToString(CultureInfo.InvariantCulture)} files")} · {Cli.FormatSize(p.Bytes)} · newest {p.NewestUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}",
            p.Tags.FirstOrDefault(), p.Path)).ToList()
        : [];

    public string? LearnMore => Learn.State == LearnState.Found && Learn.Finds is { Places.Count: > 2 } finds
        ? $"And {(finds.Places.Count - 2 == 1 ? "1 more place" : $"{(finds.Places.Count - 2).ToString(CultureInfo.InvariantCulture)} more places")}. See what it found… lists them all."
        : null;

    public bool HasLearnMore => LearnMore is not null;

    /// <summary>Turn off learn mode: while it waits or found something; a session it's watching runs to its end.</summary>
    public bool LearnCanTurnOff => Learn.State is LearnState.Waiting or LearnState.Found or LearnState.NothingFound;

    public bool LearnCanTurnOn => Learn.State == LearnState.Off;

    /// <summary>The status line's See what it found: a game not syncing whose finds wait for the person.</summary>
    public bool SeesLearnFinds => NotSyncing && Kept is null && Learn.State == LearnState.Found;

    /// <summary>The Saves found card's Choose files…: only once there are files to choose from.</summary>
    public bool ChoosesFiles => NotSyncingPlain && HasPlaces;

    /// <summary>The Saves found card's note for a game nothing was found for, when learn mode isn't on it.</summary>
    public bool NothingFoundNote => NotSyncing && NothingFound && !ShowsLearn;

    /// <summary>The same card's note when learn mode is on it: where the saves are isn't known yet.</summary>
    public bool LearnNothingFoundNote => NotSyncing && NothingFound && ShowsLearn;

    [RelayCommand]
    private void SeeLearnFinds() => _actions?.OpenLearnFinds?.Invoke(Id);

    [RelayCommand]
    private void TurnOffLearn() => _actions?.SetLearn?.Invoke(Id, false);

    [RelayCommand]
    private void TurnOnLearn() => _actions?.SetLearn?.Invoke(Id, true);

    /// <summary>The status line for a game not syncing yet, as learn mode has it; null when it has nothing to say.</summary>
    private string? LearnSentence() => _game.Syncs || !HasDetail ? null : LearnLine(Title, Learn, NothingFound);

    /// <summary>
    /// What learn mode means for a game not syncing yet, in a sentence (the saves page's status, the game page's Saves card);
    /// null when it has nothing to say: the game's saves were found another way.
    /// </summary>
    public static string? LearnLine(string title, LearnView learn, bool nothingFound) => learn.State switch
    {
        LearnState.Watching => $"Learn mode is watching where {title} saves. When you quit, GameSync shows you the places it wrote to; nothing syncs until you choose.",
        LearnState.Found => $"Learn mode watched you play on {SessionText(learn)} and found where {title} saves. Nothing is backed up yet: choose what to sync.",
        _ when !nothingFound => null,
        LearnState.Waiting => $"GameSync hasn't found where {title} saves. Learn mode watches the next time you play it, from anywhere, and shows you the places it wrote to.",
        LearnState.NothingFound => $"Learn mode watched you play on {DayText(learn)} and saw nothing written that looks like a save. It watches again next time; Add a place… sets them if you know where they are.",
        LearnState.AntiCheat => $"GameSync hasn't found where {title} saves. It ships an anti-cheat, so learn mode stays off; Add a place… sets them if you know where they are.",
        _ => $"GameSync hasn't found where {title} saves. Add a place… sets them if you know where they are, or learn mode can watch your next session.",
    };

    /// <summary>"4 Oct, 21:04 to 21:52".</summary>
    private static string SessionText(LearnView learn)
    {
        if (learn.Finds is not { } finds)
        {
            return "your last session";
        }

        var start = finds.StartUtc.ToLocalTime();
        var end = finds.EndUtc.ToLocalTime();
        return $"{start.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture)} to {(end.Date == start.Date ? end.ToString("HH:mm", CultureInfo.InvariantCulture) : end.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture))}";
    }

    private static string DayText(LearnView learn) => learn.Finds is { } finds ? finds.EndUtc.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture) : "your last session";
}
