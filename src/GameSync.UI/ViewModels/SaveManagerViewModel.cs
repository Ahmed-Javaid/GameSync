using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>A row of the save manager's table: the game, its status, where its saves are, how many versions, their size, the last backup.</summary>
public sealed record SaveRow(GameId Id, string Title, GameStatus? Status, string? StatusLabel, string Path, string Versions, string Size, string LastBackup)
{
    public override string ToString() => string.Join(", ", new[] { Title, StatusLabel ?? GsStatusBadge.Describe(Status).Word, Versions == "" ? null : $"{Versions} versions", LastBackup == "" ? null : $"last backup {LastBackup}" }.OfType<string>());
}

/// <summary>A number in the save manager's strip: GAMES 12.</summary>
public sealed record SaveStat(string Label, string Value)
{
    public override string ToString() => $"{Label}: {Value}";
}

/// <summary>
/// The save manager (design system → SaveManagerScreen, GameSavesScreen): every game's saves in one console-style
/// table, a strip of numbers and the live log; a game's name opens its saves, where the save work is (MGR-07). It lives
/// as long as the window, so the game open in it stays when the games refresh.
/// </summary>
public sealed partial class SaveManagerViewModel : ObservableObject, IPageSurface
{
    private readonly LauncherActions? _actions;
    private IReadOnlyList<LauncherGame> _games = [];
    private IReadOnlyDictionary<GameId, GameSaveSummary> _summaries = new Dictionary<GameId, GameSaveSummary>();
    private int _loads;

    [ObservableProperty]
    private IReadOnlyList<SaveRow> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<SaveStat> _stats = [];

    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>The game whose saves are open; null shows every game's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(Strength), nameof(BackdropArt))]
    private GameSavesViewModel? _game;

    /// <param name="log">The agent's log, which the page shows as it happens.</param>
    public SaveManagerViewModel(LauncherActions? actions = null, ObservableCollection<LogLine>? log = null)
    {
        _actions = actions;
        Log = log ?? [];
        OpenCommand = new RelayCommand<GameId>(id => Open(id));
        SyncNowCommand = new RelayCommand(() => _actions?.SyncNow(), () => _actions is not null);
        BackCommand = new RelayCommand(Back);
    }

    public ObservableCollection<LogLine> Log { get; }

    public string Title => "Save manager";

    public bool ShowsTable => Game is null;

    /// <summary>Full glass (LOOK-17); a game's saves show that game's own art.</summary>
    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => Game?.BackdropArt;

    /// <summary>A game's name in the table: its saves.</summary>
    public ICommand OpenCommand { get; }

    public ICommand SyncNowCommand { get; }

    /// <summary>Back from a game's saves: to where the person came from (its page in the library), or to every game's saves.</summary>
    public ICommand BackCommand { get; }

    /// <summary>The page Back returns to from a game's saves, when they were opened from another page (a game's page).</summary>
    public string? ReturnTo { get; private set; }

    /// <summary>Opens a game's saves; <paramref name="returnTo"/> is the page Back goes back to, when it isn't every game's saves.</summary>
    public void Open(GameId id, string? returnTo = null)
    {
        ReturnTo = returnTo;
        if (_games.FirstOrDefault(g => g.Id == id) is not { } game)
        {
            Game = null;
            return;
        }

        if (Game?.Id != id)
        {
            Game = new GameSavesViewModel(game, _actions, BackCommand, returnTo == "library" ? "My games" : "Save manager");
        }

        Game.Reload();
    }

    /// <summary>The games as they are now; the table is read again, off the UI thread, and the open game's saves too.</summary>
    public void Update(IReadOnlyList<LauncherGame> games)
    {
        _games = games;
        if (Game is { } open)
        {
            if (games.FirstOrDefault(g => g.Id == open.Id) is { } game)
            {
                open.Update(game);
                open.Reload();
            }
            else
            {
                Game = null;
            }
        }

        Rebuild(DateTime.Now);
        LoadSummaries();
    }

    /// <summary>The table from what's known: with the saves at a glance once they're read.</summary>
    public void Show(IReadOnlyList<GameSaveSummary> summaries, DateTime nowLocal)
    {
        _summaries = summaries.ToDictionary(s => s.Id);
        Rebuild(nowLocal);
    }

    private async void LoadSummaries()
    {
        if (_actions?.LoadSaves is not { } load)
        {
            return;
        }

        var ticket = ++_loads;
        try
        {
            var summaries = await load(CancellationToken.None);
            if (ticket == _loads)
            {
                Show(summaries, DateTime.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _loads)
            {
                Subtitle = $"GameSync couldn't read the saves on this PC: {e.Message}";
            }
        }
    }

    private void Back()
    {
        var returnTo = ReturnTo;
        ReturnTo = null;
        Game = null;
        if (returnTo is not null)
        {
            _actions?.Show(returnTo, null);
        }
    }

    private void Rebuild(DateTime nowLocal)
    {
        // Games that sync first, the ones that need you at the top, then the ones found but not syncing yet; each by last play.
        var shown = _games.Where(g => !g.IsSoftware || g.Syncs)
            .OrderByDescending(g => g.NeedsYou)
            .ThenByDescending(g => g.Syncs)
            .ThenByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Rows = shown.Select(g =>
        {
            var summary = _summaries.GetValueOrDefault(g.Id);
            return new SaveRow(g.Id, g.Title, g.Syncs ? g.Status ?? GameStatus.Synced : null, g.Syncs ? HomeViewModel.StatusLabel(g) : null,
                summary?.Folder ?? "",
                g.Syncs && summary is not null ? summary.Versions.ToString(CultureInfo.InvariantCulture) : "",
                summary is { HistoryBytes: > 0 } ? Cli.FormatSize(summary.HistoryBytes) : "",
                summary?.LastBackupUtc is { } at ? Launcher.WhenText(at, nowLocal) ?? "" : "");
        }).ToList();

        var syncing = shown.Count(g => g.Syncs);
        var latest = _summaries.Values.Select(s => s.LastBackupUtc).Max();
        Stats =
        [
            new SaveStat("Games", syncing.ToString(CultureInfo.InvariantCulture)),
            new SaveStat("History", Cli.FormatSize(_summaries.Values.Sum(s => s.HistoryBytes))),
            new SaveStat("Versions", _summaries.Values.Sum(s => s.Versions).ToString(CultureInfo.InvariantCulture)),
            new SaveStat("Last backup", latest is { } at ? Launcher.WhenText(at, nowLocal) ?? "None yet" : "None yet"),
            new SaveStat("Needs you", shown.Count(g => g.NeedsYou).ToString(CultureInfo.InvariantCulture)),
        ];
        var found = shown.Count - syncing;
        Subtitle = syncing == 0 ? $"No games syncing yet · {found} found with saves" : $"{syncing} {(syncing == 1 ? "game" : "games")} syncing · {found} found, not syncing yet";
    }
}
