using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Discovery;
using GameSync.Host;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>A program in What's in a folder (LIB-30): ticked to join the library, with why it looks like a game or doesn't.</summary>
public sealed partial class FolderPickRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isChecked;

    public FolderPickRow(FolderCandidate candidate, Action changed)
    {
        Candidate = candidate;
        _isChecked = candidate.Ticked;
        _changed = changed;
    }

    public FolderCandidate Candidate { get; }

    public string Title => Candidate.Title;

    public string Program => Candidate.Program ?? "";

    public bool HasProgram => Candidate.Program is not null;

    public string Why => Candidate.LeftOutBefore ? "you left it out before" : Candidate.Why;

    public string Initial => GsGameTile.InitialOf(Title);

    partial void OnIsCheckedChanged(bool value) => _changed();

    public override string ToString() => $"{Title}, {(IsChecked ? "added" : "left out")}: {Why}";
}

/// <summary>
/// What's in a folder (LIB-30; design system version 56, OnboardingScreen #scan-folder; the friend: "When someone scans a
/// folder, a small popup should show which games it has detected and then user can select deselect those games"): every
/// program GameSync found in a folder of games, as Games, ticked, and Probably not games, unticked, each with why; the
/// primary counts what's ticked. Over first run, the library's Scan a folder for games… and Settings' game folders.
/// </summary>
public sealed partial class FolderPickViewModel : ObservableObject
{
    private readonly Action<FolderPicks?> _done;
    private bool _finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddLabel), nameof(CanAdd))]
    private int _chosen;

    public FolderPickViewModel(FolderLookResult look, Action<FolderPicks?> done)
    {
        Folder = look.Folder;
        _done = done;
        var rows = look.Candidates.Select(c => new FolderPickRow(c, Recount)).ToList();
        Games = rows.Where(r => r.Candidate.Ticked).ToList();
        Others = rows.Where(r => !r.Candidate.Ticked).ToList();
        Recount();
        AddCommand = new RelayCommand(() => Finish(Picks()), () => CanAdd);
        AddAnywayCommand = new RelayCommand(() => Finish(FolderPicks.All(Folder)));
        CancelCommand = new RelayCommand(() => Finish(null));
    }

    public string Folder { get; }

    public string Title => $"What's in {Folder}";

    public string Subtitle => Count == 0
        ? "GameSync found no programs here."
        : $"GameSync found {Plural(Count, "program", "programs")} here. Tick the games; the rest stay out of your library.";

    public IReadOnlyList<FolderPickRow> Games { get; }

    public IReadOnlyList<FolderPickRow> Others { get; }

    public int Count => Games.Count + Others.Count;

    public bool HasGames => Games.Count > 0;

    public bool HasOthers => Others.Count > 0;

    public bool IsEmpty => Count == 0;

    public bool HasCandidates => Count > 0;

    public string GamesHeading => $"GAMES · {Games.Count.ToString(CultureInfo.InvariantCulture)}";

    public string OthersHeading => $"PROBABLY NOT GAMES · {Others.Count.ToString(CultureInfo.InvariantCulture)}";

    public string EmptyText =>
        $"GameSync looks in a folder of games for the folders inside it that hold a game's program, such as {Path.Combine(Folder, "Hollow Knight")}, and in folders of several games. Add the folder anyway, and games you install there later are found.";

    public string AddLabel => Chosen == 1 ? "Add 1 game" : $"Add {Chosen.ToString(CultureInfo.InvariantCulture)} games";

    public bool CanAdd => Chosen > 0;

    public string Note =>
        $"GameSync looks in {Folder} again at every scan. What you leave out stays out; anything new there that doesn't look like a game waits in the library's Hidden tab.";

    public ICommand AddCommand { get; }

    /// <summary>Nothing found: the folder still joins every scan, for games installed there later.</summary>
    public ICommand AddAnywayCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>What the person picked: the folders left out, and ones ticked that were left out before.</summary>
    public FolderPicks Picks()
    {
        var all = Games.Concat(Others).ToList();
        var leaveOut = all.Where(r => !r.IsChecked).Select(r => FolderLook.Key(r.Candidate.Folder)).ToHashSet(StringComparer.Ordinal);
        var takeBack = all.Where(r => r.IsChecked && r.Candidate.LeftOutBefore).Select(r => FolderLook.Key(r.Candidate.Folder)).ToHashSet(StringComparer.Ordinal);
        return new FolderPicks(Folder, leaveOut, takeBack, all.Where(r => !r.IsChecked).Select(r => r.Title).ToList());
    }

    private void Finish(FolderPicks? picks)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _done(picks);
    }

    private void Recount()
    {
        Chosen = Games.Concat(Others).Count(r => r.IsChecked);
        (AddCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.InvariantCulture)} {many}";
}
