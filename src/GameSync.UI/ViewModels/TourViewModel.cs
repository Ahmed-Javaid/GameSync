using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameSync.UI.ViewModels;

/// <summary>
/// A stop of the tour: what it points at, its name and a sentence or two. <paramref name="Target"/> is <c>rail:&lt;id&gt;</c>
/// for a rail button, <c>name:&lt;name&gt;</c> for a named part of a page, <c>hero</c> for Home's banner, or null for none.
/// </summary>
public sealed record TourStop(string? Target, string Title, string Text);

/// <summary>
/// The tour after first run (ONB-08; design system version 56, GuideScreen #tour; a friend: "Add a tutorial and a guide",
/// "Buttons on the top right are not obvious as to what they do"): a box asks first, then each stop dims the window but for
/// what it points at, with its name, its words, how far along, End the tour, Back and Next. It never comes back by itself;
/// the Guide's Take the tour again starts it at the first stop.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    public static readonly IReadOnlyList<TourStop> Stops =
    [
        new(null, "You're set up", "Your first backup is running. Want a one-minute look round? You can take it again from the Guide."),
        new("rail:library", "Your games", "Every game GameSync knows: the ones installed here, ready to play, and the ones whose saves it keeps."),
        new("rail:saves", "Your saves", "Every game's saves: what's backed up, each version from each PC, and conflicts. Restore an earlier save from a game's saves."),
        new("name:TourTopBar", "These buttons", "Sync now backs up and syncs every game at once. The cloud button says where your saves go and whether it's reachable. The last is this PC; your other PCs are listed there too."),
        new("hero", "How a game's saves are", "Each game says it in a word: Synced, Not synced, Held for review. Point at it for what it means, or see the Guide."),
        new("rail:guide", "The Guide", "How GameSync works, what each status means and where your backups are. It's always here."),
    ];

    private readonly Action _end;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stop), nameof(IsAsking), nameof(CountText), nameof(NextLabel), nameof(EndLabel), nameof(CanGoBack), nameof(HasCount))]
    private int _index;

    /// <param name="end">The tour ended, by Done, End the tour, Not now or Esc.</param>
    /// <param name="startAt">0 asks first (after first run); 1 starts at the first stop (the Guide's Take the tour again).</param>
    public TourViewModel(Action end, int startAt = 0)
    {
        _end = end;
        _index = Math.Clamp(startAt, 0, Stops.Count - 1);
        NextCommand = new RelayCommand(Next);
        BackCommand = new RelayCommand(() => Index = Math.Max(1, Index - 1), () => CanGoBack);
        EndCommand = new RelayCommand(_end);
    }

    public TourStop Stop => Stops[Index];

    /// <summary>The first box: asks whether to look round at all.</summary>
    public bool IsAsking => Index == 0;

    public bool HasCount => Index > 0;

    public string CountText => Index == 0 ? "" : $"{Index.ToString(CultureInfo.InvariantCulture)} of {(Stops.Count - 1).ToString(CultureInfo.InvariantCulture)}";

    public string NextLabel => Index == 0 ? "Show me around" : Index == Stops.Count - 1 ? "Done" : "Next";

    public string EndLabel => Index == 0 ? "Not now" : "End the tour";

    public bool CanGoBack => Index > 1;

    public ICommand NextCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand EndCommand { get; }

    private void Next()
    {
        if (Index >= Stops.Count - 1)
        {
            _end();
            return;
        }

        Index++;
        (BackCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnIndexChanged(int value) => (BackCommand as RelayCommand)?.NotifyCanExecuteChanged();
}
