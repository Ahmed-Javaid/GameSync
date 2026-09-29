using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>A change in the plan: ticked to run, the game, what it does and why, and how much would move.</summary>
public sealed partial class PlanChangeRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isChecked = true;

    public PlanChangeRow(PlannedChange change, Action changed)
    {
        Change = change;
        _changed = changed;
    }

    public PlannedChange Change { get; }

    public string Title => Change.Title;

    public string Why => Change.Why;

    public string Size => Cli.FormatSize(Change.Bytes);

    public string ActionLabel => Change.Step switch
    {
        PlanStep.Download => "Download",
        PlanStep.BackUp => "Back up",
        PlanStep.Hold => "Hold for review",
        _ => "Upload",
    };

    public string ActionIcon => Change.Step switch
    {
        PlanStep.Download => "download",
        PlanStep.BackUp => "archive",
        PlanStep.Hold => "pause",
        _ => "upload",
    };

    partial void OnIsCheckedChanged(bool value) => _changed();

    public override string ToString() => $"{Title}: {ActionLabel}, {Size}{(IsChecked ? "" : ", skipped this time")}. {Why}";
}

/// <summary>A game the next sync leaves alone: its status, why, and the status's own button when it has one.</summary>
public sealed record PlanWaitRow(PlannedWait Wait)
{
    public string Title => Wait.Title;

    public GameStatus Status => Wait.Status;

    public string Why => Wait.Why;

    public string? Action => Wait.Action;

    public bool HasAction => Wait.Action is not null;

    public override string ToString() => $"{Title}: {Controls.GsStatusBadge.Describe(Status).Word}. {Why}";
}

/// <summary>
/// The save manager's Plan tab (design system → PlanScreen, SYNC-14): what the next sync would do for each game and why,
/// with nothing done until Run. Each change starts ticked; unticking one skips that game this time only. The games that
/// won't run say why, with their status's button; those already in sync fold away behind one button.
/// </summary>
public sealed partial class SyncPlanViewModel : ObservableObject
{
    private readonly LauncherActions? _actions;
    private readonly Action<GameId, bool> _open;
    private bool _checked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun), nameof(RunLabel), nameof(Busy))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun), nameof(RunLabel), nameof(Busy))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>What the last run did, in a sentence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(NoChanges))]
    private IReadOnlyList<PlanChangeRow> _changes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaits), nameof(WaitsToolbar))]
    private IReadOnlyList<PlanWaitRow> _waits = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInSync), nameof(InSyncLabel), nameof(InSyncText))]
    private IReadOnlyList<string> _inSync = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InSyncLabel), nameof(InSyncIcon))]
    private bool _showsInSync;

    [ObservableProperty]
    private IReadOnlyList<SaveStat> _stats = [];

    [ObservableProperty]
    private string _checkedText = "";

    /// <param name="open">Opens a game's saves in the save manager, or its conflict (true).</param>
    public SyncPlanViewModel(LauncherActions? actions, Action<GameId, bool> open)
    {
        _actions = actions;
        _open = open;
        CheckCommand = new AsyncRelayCommand(CheckAsync);
        RunCommand = new AsyncRelayCommand(RunAsync);
        ShowInSyncCommand = new RelayCommand(() => ShowsInSync = !ShowsInSync);
        WaitCommand = new RelayCommand<PlanWaitRow>(row =>
        {
            if (row is not null)
            {
                _open(row.Wait.Game, row.Status == GameStatus.Conflict);
            }
        });
    }

    public ICommand CheckCommand { get; }

    public ICommand RunCommand { get; }

    public ICommand ShowInSyncCommand { get; }

    /// <summary>A waiting game's button: Resolve opens its conflict; the others, its saves.</summary>
    public ICommand WaitCommand { get; }

    public bool Busy => IsChecking || IsRunning;

    public bool HasError => Error is not null;

    public bool HasResult => Result is not null;

    public bool HasChanges => Changes.Count > 0;

    public bool NoChanges => Changes.Count == 0 && _checked && !IsChecking;

    public bool HasWaits => Waits.Count > 0;

    public bool HasInSync => InSync.Count > 0;

    public int Ticked => Changes.Count(c => c.IsChecked);

    public bool CanRun => Ticked > 0 && !Busy;

    public string RunLabel => IsRunning ? "Running…" : Ticked switch
    {
        0 => "Nothing to run",
        1 => "Run 1 change",
        var n => $"Run {n.ToString(CultureInfo.InvariantCulture)} changes",
    };

    public string ChangesToolbar => $"{Ticked.ToString(CultureInfo.InvariantCulture)} of {Changes.Count.ToString(CultureInfo.InvariantCulture)} will run · untick a game to skip it this time";

    public string WaitsToolbar => Waits.Count == 1 ? "1 game won't run this time" : $"{Waits.Count.ToString(CultureInfo.InvariantCulture)} games won't run this time";

    public string InSyncLabel => ShowsInSync
        ? "Hide the games already in sync"
        : InSync.Count == 1 ? "Show the 1 game already in sync" : $"Show the {InSync.Count.ToString(CultureInfo.InvariantCulture)} games already in sync";

    public string InSyncIcon => ShowsInSync ? "chevronDown" : "chevronRight";

    public string InSyncText => string.Join(" · ", InSync);

    /// <summary>The first look at the tab: the plan is made once, and Check again makes it again.</summary>
    public void CheckIfFirst()
    {
        if (!_checked && !IsChecking)
        {
            _ = CheckAsync();
        }
    }

    public async Task CheckAsync()
    {
        if (_actions?.CheckPlan is not { } check || Busy)
        {
            return;
        }

        IsChecking = true;
        Error = null;
        try
        {
            Show(await check(CancellationToken.None));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException or Core.Storage.CloudException)
        {
            Error = $"GameSync couldn't make the plan: {e.Message}";
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(NoChanges));
        }
    }

    /// <summary>A plan made, as the tab shows it; ticks the person set on a game still in the plan stay.</summary>
    public void Show(SyncPlanView plan)
    {
        var skipped = Changes.Where(c => !c.IsChecked).Select(c => c.Change.Id).ToHashSet();
        Changes = plan.Changes.Select(c => new PlanChangeRow(c, Recount) { IsChecked = !skipped.Contains(c.Id) }).ToList();
        Waits = plan.Waits.Select(w => new PlanWaitRow(w)).ToList();
        InSync = plan.InSync;
        _checked = true;
        CheckedText = $"Checked {plan.CheckedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}. Nothing changes until you run it; the sync after each game and the daily backup follow the same rules.";
        Recount();
    }

    private async Task RunAsync()
    {
        if (_actions?.RunPlan is not { } run || !CanRun)
        {
            return;
        }

        IsRunning = true;
        Error = null;
        Result = null;
        try
        {
            var outcome = await run(Changes.Where(c => c.IsChecked).Select(c => c.Change).ToList(), CancellationToken.None);
            Result = outcome.Sentence;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException or Core.Storage.CloudException)
        {
            Error = $"The plan didn't run: {e.Message}";
        }
        finally
        {
            IsRunning = false;
        }

        // What's left to do now, from the new state.
        await CheckAsync();
    }

    private void Recount()
    {
        var needYou = Waits.Count(w => SyncCounts.NeedsYou(w.Status));
        Stats =
        [
            new SaveStat("Will run", Ticked.ToString(CultureInfo.InvariantCulture)),
            new SaveStat("Needs you", needYou.ToString(CultureInfo.InvariantCulture)),
            new SaveStat("Waiting", (Waits.Count - needYou).ToString(CultureInfo.InvariantCulture)),
            new SaveStat("In sync", InSync.Count.ToString(CultureInfo.InvariantCulture)),
            new SaveStat("To move", Cli.FormatSize(Changes.Where(c => c.IsChecked).Sum(c => c.Change.Bytes))),
        ];
        OnPropertyChanged(nameof(Ticked));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(RunLabel));
        OnPropertyChanged(nameof(ChangesToolbar));
    }
}
