using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>One PC's side of a conflict on its card: where it is, the PC, its facts, and Keep.</summary>
public sealed record ConflictSideItem(string Where, string Pc, bool IsThisPc, bool Newest, bool Suggested, IReadOnlyList<Fact> Facts, ICommand? Keep)
{
    public string KeepLabel => $"Keep {Pc}'s";

    /// <summary>What keeping another PC's save asks first.</summary>
    public string ConfirmTitle => $"Carry on with {Pc}'s save?";

    public bool CanKeep => Keep is not null;

    /// <summary>Keeping another PC's save writes its files into the game's folders, so it asks first; keeping this PC's uploads it.</summary>
    public bool KeepsAtOnce => CanKeep && IsThisPc;

    public bool AsksFirst => CanKeep && !IsThisPc;

    public override string ToString() => string.Join(", ", new[] { Where, Pc, Newest ? "newest" : null, Suggested ? "suggested" : null }.OfType<string>()
        .Concat(Facts.Select(f => f.ToString())));
}

/// <summary>A row of Compare files: the file, each side's copy (size and time), and what changed.</summary>
public sealed record ConflictFileItem(string Name, string Here, string There, string What)
{
    public override string ToString() => $"{Name}: {Here}; {There}; {What}";
}

/// <summary>
/// A game's conflict in the save manager (design system → ConflictScreen, SYNC-10): what happened in a sentence, the
/// suggested side with why, and why GameSync asked; each side's card with Keep; Compare files; Decide later. Once it's
/// settled, the same screen says which save is current, with Swap to switch back (SYNC-04). Back and the breadcrumb, as
/// on every page below the rail (LIB-21). Full glass over the game's own art (LOOK-17).
/// </summary>
public sealed partial class ConflictViewModel : ObservableObject, IPageSurface
{
    private readonly LauncherActions? _actions;
    private LauncherGame _game;
    private ConflictDetail? _detail;
    private int _loads;

    /// <summary>Swap was pressed: the PC switched to, and the one switched from, for what the screen says once it's done.</summary>
    private (string To, string? From)? _swappedTo;

    [ObservableProperty]
    private string _title = "";

    /// <summary>The conflict has been read: until then only the header shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWaiting), nameof(ShowsSettled), nameof(ShowsNothing))]
    private bool _hasDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWaiting), nameof(ShowsSettled), nameof(ShowsNothing), nameof(StatusLabel))]
    private bool _waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWaiting), nameof(ShowsSettled), nameof(ShowsNothing))]
    private bool _settled;

    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    private string _sentence = "";

    /// <summary>"Suggested: keep DESKTOP's." in bold, then why, then why GameSync asked.</summary>
    [ObservableProperty]
    private string _suggestion = "";

    [ObservableProperty]
    private string _suggestionWhy = "";

    [ObservableProperty]
    private IReadOnlyList<ConflictSideItem> _sides = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    private IReadOnlyList<ConflictFileItem> _files = [];

    [ObservableProperty]
    private string _hereHeading = "THIS PC";

    [ObservableProperty]
    private string _thereHeading = "CLOUD";

    [ObservableProperty]
    private string _filesSummary = "";

    /// <summary>Compare files is open.</summary>
    [ObservableProperty]
    private bool _comparing;

    /// <summary>A choice is being carried out; the screen says so until the games refresh.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private string? _busy;

    /// <summary>Settled: the PC Swap switches to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwapLabel))]
    private string? _pinnedPc;

    /// <param name="back">Back, and Decide later: to where the person came from.</param>
    /// <param name="openSaves">The breadcrumb's game: its saves.</param>
    /// <param name="openTable">The breadcrumb's first step: every game's saves.</param>
    public ConflictViewModel(LauncherGame game, LauncherActions? actions, ICommand back, ICommand openSaves, ICommand openTable)
    {
        _actions = actions;
        _game = game;
        BackCommand = back;
        OpenSavesCommand = openSaves;
        OpenTableCommand = openTable;
        CompareCommand = new RelayCommand(() => Comparing = !Comparing);
        ChangeForGameCommand = new RelayCommand(() => _actions?.OpenProperties?.Invoke(Id, "sync"), () => _actions?.OpenProperties is not null);
        SwapCommand = new RelayCommand(() =>
        {
            if (_actions?.Swap is { } swap && PinnedPc is { } pinned)
            {
                Busy = $"Switching to {pinned}'s save…";
                _swappedTo = (pinned, _detail?.KeptPc);
                swap(Id);
            }
        }, () => _actions?.Swap is not null);
        Update(game);
    }

    public GameId Id => _game.Id;

    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => _game.HeroPath ?? _game.CoverPath;

    public ICommand BackCommand { get; }

    public ICommand OpenSavesCommand { get; }

    public ICommand OpenTableCommand { get; }

    public ICommand CompareCommand { get; }

    /// <summary>The game's Properties, on Sync: who wins when both PCs changed it (SYNC-09).</summary>
    public ICommand ChangeForGameCommand { get; }

    /// <summary>Settled: switches to the save that lost (SYNC-04). It restores, so the page asks first.</summary>
    public ICommand SwapCommand { get; }

    public bool ShowsWaiting => HasDetail && Waiting;

    public bool ShowsSettled => HasDetail && Settled;

    /// <summary>Nothing to choose any more: settled somewhere else, or on another PC.</summary>
    public bool ShowsNothing => HasDetail && !Waiting && !Settled;

    public bool NotBusy => Busy is null;

    public bool HasFiles => Files.Count > 0;

    public string StatusLabel => Waiting ? "Conflict · waiting for you" : "Conflict · resolved";

    public string SwapLabel => PinnedPc is { } pc ? $"Swap to {pc}'s" : "Swap";

    /// <summary>The note beside Decide later: what waiting means.</summary>
    public string WaitNote => $"Until you decide, {Title} won't upload or download, and each PC keeps playing its own copy.";

    /// <summary>Compare files' command line, as the console writes it.</summary>
    public string CompareCommandLine => $"compare {Id} --this-pc --cloud";

    /// <summary>The game as the library knows it now.</summary>
    public void Update(LauncherGame game)
    {
        _game = game;
        Title = game.Title;
        OnPropertyChanged(nameof(BackdropArt));
        OnPropertyChanged(nameof(WaitNote));
    }

    /// <summary>What the page reads after it opens, and again whenever the games refresh.</summary>
    public async void Reload()
    {
        if (_actions?.LoadConflict is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        try
        {
            var detail = await load(Id, CancellationToken.None);
            if (ticket == _loads)
            {
                Show(detail, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                Headline = "GameSync couldn't read this conflict";
                Sentence = e.Message;
                HasDetail = true;
            }
        }
    }

    /// <summary>The screen, from what this PC knows of the conflict; null when there's none to show.</summary>
    public void Show(ConflictDetail? detail, DateTime nowLocal)
    {
        _detail = detail;
        Busy = null;
        Waiting = detail?.Waiting == true;
        Settled = detail is { Waiting: false };
        PinnedPc = detail?.PinnedPc;
        if (detail is null)
        {
            (Headline, Sentence) = _swappedTo is { } swapped
                ? ($"Switched to {swapped.To}'s save",
                    $"{swapped.To}'s save is current now. {swapped.From ?? "The other PC"}'s stays in the history, so you can restore it from the game's saves.")
                : ("Nothing to choose", $"{Title} has no conflict waiting. Its history has every version, if you want an older one back.");
            Sides = [];
            Files = [];
            HasDetail = true;
            return;
        }

        Headline = HeadlineOf(detail);
        Sentence = SentenceOf(detail, nowLocal);
        var suggested = detail.Sides.FirstOrDefault(s => s.Suggested);
        Suggestion = !detail.Waiting ? $"Want {detail.PinnedPc}'s instead?"
            : suggested is null ? "" : $"Suggested: keep {suggested.Pc}'s.";
        SuggestionWhy = !detail.Waiting ? "Swap switches to it; the files there now are kept as a version first."
            : string.Join(' ', new[] { WhyOf(detail), AskedBecause(detail) }.OfType<string>());
        Sides = detail.Sides.Select(s => new ConflictSideItem(WhereOf(s, detail), s.Pc, s.IsThisPc, s.Newest, s.Suggested, FactsOf(s, nowLocal),
            detail.Waiting && _actions?.Resolve is { } resolve ? new RelayCommand(() => Keep(s, resolve)) : null)).ToList();

        var here = detail.Waiting ? detail.Sides.FirstOrDefault(s => s.IsThisPc) : detail.Kept;
        var there = detail.Waiting ? detail.Sides.FirstOrDefault(s => !s.IsThisPc && s.Suggested) ?? detail.Sides.FirstOrDefault(s => !s.IsThisPc) : detail.Pinned;
        HereHeading = (here?.Pc ?? "THIS PC").ToUpperInvariant();
        ThereHeading = (there?.Pc ?? "CLOUD").ToUpperInvariant();
        Files = detail.Files.Select(f => new ConflictFileItem(f.Name, Copy(f.Here, nowLocal), Copy(f.There, nowLocal), f.What)).ToList();
        FilesSummary = $"{Count(detail.Files.Count, "file differs", "files differ")} · settings files stay per PC";
        HasDetail = true;
    }

    private void Keep(ConflictSide side, Action<GameId, bool, VersionId?> resolve)
    {
        Busy = $"Keeping {side.Pc}'s save…";
        resolve(Id, side.IsThisPc, side.IsThisPc ? null : side.Version);
    }

    /// <summary>"Sekiro changed on two PCs", or, settled, which save was kept.</summary>
    public static string HeadlineOf(ConflictDetail detail)
    {
        if (!detail.Waiting)
        {
            return detail.ByHand ? $"You kept {detail.KeptPc}'s save" : $"Newest kept: {detail.KeptPc}'s save";
        }

        var pcs = detail.Sides.Select(s => s.Pc).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return pcs <= 2 ? $"{detail.Title} changed on two PCs" : $"{detail.Title} changed on {Number(pcs)} PCs";
    }

    /// <summary>
    /// What each PC did and that both copies are safe: "DESKTOP saved at 21:04 and LAPTOP at 19:30, both after the last
    /// sync." Settled: which is current and what Swap does.
    /// </summary>
    public static string SentenceOf(ConflictDetail detail, DateTime nowLocal)
    {
        if (!detail.Waiting)
        {
            return detail.ByHand
                ? $"You chose {detail.KeptPc}'s save, so it's current, and {detail.PinnedPc}'s is pinned in history. Nothing was lost."
                : $"{detail.Title} changed on {detail.KeptPc} and on {detail.PinnedPc}, and GameSync kept the newest save. {detail.PinnedPc}'s is pinned in history, so nothing was lost.";
        }

        var saved = detail.Sides.Select((s, i) => i == 0 ? $"{s.Pc} saved {At(s.SavedUtc, nowLocal)}" : $"{s.Pc} {At(s.SavedUtc, nowLocal)}").ToList();
        var who = saved.Count <= 2 ? string.Join(" and ", saved) : string.Join(", ", saved[..^1]) + " and " + saved[^1];
        var after = detail.LastSyncUtc is null ? "" : saved.Count == 2 ? ", both after the last sync" : ", all after the last sync";
        return $"{who}{after}. Both copies are safe. Pick the one to carry on with; the other stays pinned in history, and you can swap back later.";
    }

    /// <summary>Why the suggested side: the newest (and the longer play), or why the newest isn't the one.</summary>
    public static string? WhyOf(ConflictDetail detail)
    {
        if (!detail.Waiting || detail.Sides.FirstOrDefault(s => s.Suggested) is not { } suggested)
        {
            return null;
        }

        var newest = detail.Sides.FirstOrDefault(s => s.Newest) ?? suggested;
        var longest = detail.Sides.All(s => s == suggested || s.Played < suggested.Played) && suggested.Played > TimeSpan.Zero;
        if (suggested == newest)
        {
            return longest ? "It's the newest save and the longer play." : "It's the newest save.";
        }

        if (newest.LostHalf)
        {
            return $"{newest.Pc}'s is newer, but it lost more than half of its files or size, which looks like a reset.";
        }

        if (newest.OutsidePlay)
        {
            return $"{newest.Pc}'s is newer, but it changed while the game wasn't running.";
        }

        return longest ? "It's the longer play since the last sync." : null;
    }

    /// <summary>Why GameSync asked instead of choosing by itself, from the reason the engine gave when the conflict arose.</summary>
    public static string? AskedBecause(ConflictDetail detail)
    {
        if (!detail.Waiting || detail.Reason is not { } reason)
        {
            return null;
        }

        var here = detail.Sides.FirstOrDefault(s => s.IsThisPc)?.Pc ?? "this PC";
        if (reason.Contains("set to always ask", StringComparison.Ordinal))
        {
            return $"You're asked because {detail.Title} is set to Always ask.";
        }

        if (reason.Contains("uploaded different saves from the same starting point", StringComparison.Ordinal))
        {
            return "You're asked because both saves were uploaded from the same starting point.";
        }

        if (reason.Contains("'s change happened while the game wasn't running", StringComparison.Ordinal))
        {
            return $"You're asked because {detail.Title} keeps {here}'s save, but {here}'s changed while the game wasn't running.";
        }

        if (Regex.Match(reason, "clock is (?<off>.+?) Google's") is { Success: true } clock)
        {
            return $"You're asked because this PC's clock is {clock.Groups["off"].Value} Google's, so which save is newer can't be trusted.";
        }

        // Lost half, and changed outside play, are already the why of the suggestion.
        return null;
    }

    /// <summary>A side card's facts: Saved, Session, Since the last sync, Size, Changed files.</summary>
    public static IReadOnlyList<Fact> FactsOf(ConflictSide side, DateTime nowLocal)
    {
        var changed = side.Changed.Count <= 3 ? string.Join(", ", side.Changed) : $"{string.Join(", ", side.Changed.Take(3))} and {side.Changed.Count - 3} more";
        return Fact.Known(
            new Fact("Saved", Day(side.SavedUtc, nowLocal)),
            new Fact("Session", side.Session is { } session ? Duration(session.EndUtc - session.StartUtc) : side.IsThisPc ? "None since the last sync" : null),
            new Fact("Since the last sync", side.Sessions == 0 ? (side.IsThisPc ? "No play" : null)
                : $"{Count(side.Sessions, "session", "sessions")}, {Duration(side.Played)}"),
            new Fact("Size", $"{Cli.FormatSize(side.Bytes)}, {Count(side.Files, "file", "files")}"),
            new Fact("Changed files", changed.Length == 0 ? null : changed, Mono: true));
    }

    private static string WhereOf(ConflictSide side, ConflictDetail detail) =>
        !detail.Waiting ? (side.Pc == detail.KeptPc ? "CURRENT, FROM" : "PINNED, FROM")
        : side.IsThisPc ? "THIS PC" : "IN THE CLOUD, FROM";

    /// <summary>"Today 21:04", "Yesterday 19:30", "27 Sep 19:30".</summary>
    public static string Day(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime();
        return local.Date == nowLocal.Date ? $"Today {local:HH:mm}"
            : local.Date == nowLocal.Date.AddDays(-1) ? $"Yesterday {local:HH:mm}"
            : local.ToString(local.Year == nowLocal.Year ? "d MMM HH:mm" : "d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>"at 21:04" today, "yesterday at 19:30", "on 27 Sep at 19:30".</summary>
    private static string At(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime();
        return local.Date == nowLocal.Date ? $"at {local:HH:mm}"
            : local.Date == nowLocal.Date.AddDays(-1) ? $"yesterday at {local:HH:mm}"
            : $"on {local.ToString(local.Year == nowLocal.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture)} at {local:HH:mm}";
    }

    /// <summary>"1 h 52 min", "47 min", "under a minute".</summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        return minutes < 1 ? "under a minute"
            : minutes < 60 ? $"{minutes} min"
            : minutes % 60 == 0 ? $"{minutes / 60} h"
            : $"{minutes / 60} h {minutes % 60} min";
    }

    private static string Copy(FileEntry? file, DateTime nowLocal) =>
        file is null ? "—" : $"{Cli.FormatSize(file.Size)} · {file.ModifiedUtc.ToLocalTime().ToString(file.ModifiedUtc.ToLocalTime().Date == nowLocal.Date ? "HH:mm" : "d MMM HH:mm", CultureInfo.InvariantCulture)}";

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.InvariantCulture)} {many}";

    private static string Number(int n) => n switch { 3 => "three", 4 => "four", 5 => "five", _ => n.ToString(CultureInfo.InvariantCulture) };
}
