using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>A place learn mode saw the game write to, in the finds dialog: ticked to sync it, or locked with why not.</summary>
public sealed partial class LearnFindItem : ObservableObject
{
    public LearnFindItem(LearnPlace place, string? refused, DateTime nowLocal)
    {
        Path = place.Path;
        Portable = place.Portable;
        Tags = place.Tags;
        Refused = refused;
        var newest = place.NewestUtc.ToLocalTime();
        var when = newest.Date == nowLocal.Date ? newest.ToString("HH:mm", CultureInfo.InvariantCulture) : newest.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        Meta = refused ?? string.Join(" · ", new[]
        {
            place.IsFile ? "One file" : place.Files == 1 ? "1 file" : $"{place.Files.ToString(CultureInfo.InvariantCulture)} files",
            Cli.FormatSize(place.Bytes),
            $"newest {when}",
            place.IsFile || place.Examples.Count == 0 ? null : string.Join(", ", place.Examples),
        }.OfType<string>());
    }

    /// <summary>This PC's path, for its tooltip and for adding it.</summary>
    public string Path { get; }

    /// <summary>As every PC reads it: "&lt;localLow&gt;/Landfall Games/ROUNDS".</summary>
    public string Portable { get; }

    /// <summary>"3 files · 48 KB · newest 21:51 · Player.sav, Unlocks.sav", or why GameSync won't take it.</summary>
    public string Meta { get; }

    public IReadOnlyList<string> Tags { get; }

    /// <summary>Why Add a place would refuse it now; such a place can't be ticked.</summary>
    public string? Refused { get; }

    public bool CanPick => Refused is null;

    public bool IsRefused => Refused is not null;

    [ObservableProperty]
    private bool _picked;

    public string PickName => $"Sync {Portable}";

    public override string ToString() => $"{Portable}, {Meta}";
}

/// <summary>
/// FIND-04 (design system version 43 → LearnModeDialog): where a game saved during the session learn mode watched, the
/// likeliest place ticked. Sync these saves adds each ticked place as Add a place does, so the game starts syncing with
/// them; Watch again keeps learn mode on for the next session; Not these forgets them and turns learn mode off; with
/// nothing found, Add a place… and Done. Esc closes and keeps what was found for later.
/// </summary>
public sealed partial class LearnFindsViewModel : ObservableObject
{
    private readonly GameId _game;
    private readonly LauncherActions? _actions;
    private readonly Action _close;

    public LearnFindsViewModel(GameId game, string title, LearnFinds finds, IReadOnlyDictionary<string, string?> refusals, LauncherActions? actions, Action close,
        DateTime? nowLocal = null)
    {
        _game = game;
        GameTitle = title;
        _actions = actions;
        _close = close;
        var now = nowLocal ?? DateTime.Now;
        Items = finds.Places.Select(p => new LearnFindItem(p, refusals.GetValueOrDefault(p.Path), now)).ToList();
        if (finds.Places.FirstOrDefault() is { LooksLikeSaves: true } && Items[0].CanPick)
        {
            Items[0].Picked = true;
        }

        foreach (var item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LearnFindItem.Picked))
                {
                    OnPropertyChanged(nameof(PickedCount));
                    OnPropertyChanged(nameof(CanSync));
                    OnPropertyChanged(nameof(SyncLabel));
                    OnPropertyChanged(nameof(SyncTip));
                }
            };
        }

        var start = finds.StartUtc.ToLocalTime();
        var end = finds.EndUtc.ToLocalTime();
        Session = $"{start.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture)} to {(end.Date == start.Date ? end.ToString("HH:mm", CultureInfo.InvariantCulture) : end.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture))}";
        Overflowed = finds.Overflowed;
    }

    public string GameTitle { get; }

    public string Title => $"Where {GameTitle} saves";

    /// <summary>"4 Oct, 21:04 to 21:52".</summary>
    public string Session { get; }

    public string Subtitle => IsEmpty
        ? $"Learn mode watched you play on {Session} and saw nothing written that looks like a save."
        : $"Learn mode watched you play on {Session}. These are the places it wrote to while it ran.";

    public IReadOnlyList<LearnFindItem> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => Items.Count > 0;

    /// <summary>Windows reported changes faster than GameSync could take them in: the list may miss a place.</summary>
    public bool Overflowed { get; }

    public int PickedCount => Items.Count(i => i.Picked && i.CanPick);

    public bool CanSync => PickedCount > 0 && !Adding;

    public string SyncLabel => PickedCount > 1 ? $"Sync these {PickedCount.ToString(CultureInfo.InvariantCulture)} places" : "Sync these saves";

    public string SyncTip => PickedCount == 0 ? "Tick a place first" : "Backs up the ticked places and keeps them in step on your PCs";

    public string CloseName => $"Close Where {GameTitle} saves";

    public string NotTheseLabel => IsEmpty ? "Stop watching" : "Not these";

    public string NotTheseTip => $"Forgets what it found and stops watching {GameTitle}; Add a place… still sets where its saves are";

    /// <summary>While the picked places are added and the game starts syncing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSync))]
    private bool _adding;

    /// <summary>Why it couldn't add them, said in the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>Sync these saves: each ticked place is added, then the dialog closes on the game's saves.</summary>
    [RelayCommand]
    private async Task Sync()
    {
        if (!CanSync || _actions?.AddLearned is not { } add)
        {
            return;
        }

        Adding = true;
        Error = null;
        try
        {
            if (await add(_game, Items.Where(i => i.Picked && i.CanPick).Select(i => i.Path).ToList()) is { } problem)
            {
                Error = problem;
                return;
            }

            _close();
        }
        finally
        {
            Adding = false;
        }
    }

    /// <summary>Watch again: learn mode watches the next session too, adding what it finds then.</summary>
    [RelayCommand]
    private void Again()
    {
        _actions?.SetLearn?.Invoke(_game, true);
        _close();
    }

    /// <summary>Not these (or Stop watching): forgets what it found and turns learn mode off for the game.</summary>
    [RelayCommand]
    private void NotThese()
    {
        _actions?.SetLearn?.Invoke(_game, false);
        _close();
    }

    /// <summary>Add a place… instead: the dialog gives way to it.</summary>
    [RelayCommand]
    private void AddPlace()
    {
        _close();
        _actions?.OpenAddPlace?.Invoke(_game);
    }

    /// <summary>Done, the close button or Esc: what was found stays for later.</summary>
    [RelayCommand]
    private void Close() => _close();
}
