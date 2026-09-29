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

/// <summary>A number in the save manager's strip: GAMES 12; with a line under it when it needs one ("E: · 312 GB free").</summary>
public sealed record SaveStat(string Label, string Value, string? Sub = null)
{
    public bool HasSub => Sub is not null;

    public override string ToString() => Sub is null ? $"{Label}: {Value}" : $"{Label}: {Value}, {Sub}";
}

/// <summary>
/// The save manager (design system → SaveManagerScreen, GameSavesScreen): every game's saves in one console-style
/// table, a strip of numbers and the live log; a game's name opens its saves, where the save work is (MGR-07). It lives
/// as long as the window, so the game open in it stays when the games refresh.
/// </summary>
public sealed partial class SaveManagerViewModel : ObservableObject, IPageSurface, IRailPage
{
    private readonly LauncherActions? _actions;
    private IReadOnlyList<LauncherGame> _games = [];
    private IReadOnlyDictionary<GameId, GameSaveSummary> _summaries = new Dictionary<GameId, GameSaveSummary>();
    private BackupSpace? _space;
    private bool _spaceRead;
    private int _loads;

    [ObservableProperty]
    private IReadOnlyList<SaveRow> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<SaveStat> _stats = [];

    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>The game whose saves are open; null shows every game's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(Page), nameof(BackdropArt))]
    private GameSavesViewModel? _game;

    /// <summary>A game's conflict, open over its saves or on its own (SYNC-10).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTable), nameof(Page), nameof(BackdropArt))]
    private ConflictViewModel? _conflict;

    /// <param name="log">The agent's log, which the page shows as it happens.</param>
    public SaveManagerViewModel(LauncherActions? actions = null, ObservableCollection<LogLine>? log = null)
    {
        _actions = actions;
        Log = log ?? [];
        OpenCommand = new RelayCommand<GameId>(id => Open(id));
        SyncNowCommand = new RelayCommand(() => _actions?.SyncNow(), () => _actions is not null);
        BackCommand = new RelayCommand(Back);
        ConflictBackCommand = new RelayCommand(ConflictBack);
        TableCommand = new RelayCommand(() =>
        {
            ReturnTo = null;
            _conflictReturnTo = null;
            Conflict = null;
            Game = null;
        });
        ConflictSavesCommand = new RelayCommand(() =>
        {
            if (Conflict is { } conflict)
            {
                Conflict = null;
                Open(conflict.Id);
            }
        });
    }

    private string? _conflictReturnTo;

    public ObservableCollection<LogLine> Log { get; }

    public string Title => "Save manager";

    public bool ShowsTable => Game is null && Conflict is null;

    /// <summary>What shows instead of the table: a game's conflict, or its saves.</summary>
    public object? Page => (object?)Conflict ?? Game;

    /// <summary>Full glass (LOOK-17); a game's saves and its conflict show that game's own art.</summary>
    public GlassStrength Strength => GlassStrength.Glass;

    public string? BackdropArt => Conflict?.BackdropArt ?? Game?.BackdropArt;

    /// <summary>A game's name in the table: its saves.</summary>
    public ICommand OpenCommand { get; }

    public ICommand SyncNowCommand { get; }

    /// <summary>Back from a game's saves: to where the person came from (its page in the library), or to every game's saves.</summary>
    public ICommand BackCommand { get; }

    /// <summary>Back from a conflict, and Decide later: to the game's saves when it was opened there, or where the person came from.</summary>
    public ICommand ConflictBackCommand { get; }

    /// <summary>The breadcrumb's first step: every game's saves.</summary>
    public ICommand TableCommand { get; }

    /// <summary>The breadcrumb's game, from its conflict: its saves.</summary>
    public ICommand ConflictSavesCommand { get; }

    /// <summary>The page Back returns to from a game's saves, when they were opened from another page (a game's page).</summary>
    public string? ReturnTo { get; private set; }

    /// <summary>Opens a game's saves; <paramref name="returnTo"/> is the page Back goes back to, when it isn't every game's saves.</summary>
    public void Open(GameId id, string? returnTo = null)
    {
        ReturnTo = returnTo;
        Conflict = null;
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

    /// <summary>
    /// Opens a game's conflict (SYNC-10). Opened from its saves, Back returns there; from another page
    /// (<paramref name="returnTo"/>: Home, the library), Back returns to that page.
    /// </summary>
    public void OpenConflict(GameId id, string? returnTo = null)
    {
        if (_games.FirstOrDefault(g => g.Id == id) is not { } game)
        {
            return;
        }

        _conflictReturnTo = returnTo;
        if (Game?.Id != id)
        {
            // Not over its own saves: Back goes back where it came from, or to every game's saves.
            Game = null;
            ReturnTo = null;
        }

        if (Conflict?.Id != id)
        {
            Conflict = new ConflictViewModel(game, _actions, ConflictBackCommand, ConflictSavesCommand, TableCommand);
        }

        Conflict.Reload();
    }

    /// <summary>The rail's Save manager: a game's saves or conflict still open keeps showing, and Back leads to every game's saves.</summary>
    public void ReachedFromRail()
    {
        ReturnTo = null;
        _conflictReturnTo = null;
    }

    private void ConflictBack()
    {
        var returnTo = _conflictReturnTo;
        _conflictReturnTo = null;
        Conflict = null;
        if (Game is null && returnTo is not null)
        {
            _actions?.Show(returnTo, null);
        }
    }

    /// <summary>The games as they are now; the table is read again, off the UI thread, and the open game's saves too.</summary>
    public void Update(IReadOnlyList<LauncherGame> games)
    {
        _games = games;
        if (Conflict is { } conflict)
        {
            if (games.FirstOrDefault(g => g.Id == conflict.Id) is { } game)
            {
                conflict.Update(game);
                conflict.Reload();
            }
            else
            {
                Conflict = null;
            }
        }

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

    /// <summary>The space the backups take on this PC's drive, once it's been counted (MGR-03).</summary>
    public void ShowSpace(BackupSpace? space, DateTime nowLocal)
    {
        (_space, _spaceRead) = (space, true);
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

            if (_actions.LoadSpace is { } space && await space(CancellationToken.None) is var counted && ticket == _loads)
            {
                ShowSpace(counted, DateTime.Now);
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
        Conflict = null;
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
            // MGR-03: the backup folder as it is on its drive, not the versions' own sizes added up.
            new SaveStat("Backups on this PC", _space is { } space ? Cli.FormatSize(space.Bytes) : _spaceRead ? "None yet" : "…",
                _space is { } on ? on.FreeBytes is { } free ? $"{on.Drive} · {Cli.FormatSize(free)} free" : on.Drive : null),
            new SaveStat("Versions", _summaries.Values.Sum(s => s.Versions).ToString(CultureInfo.InvariantCulture)),
            new SaveStat("Last backup", latest is { } at ? Launcher.WhenText(at, nowLocal) ?? "None yet" : "None yet"),
            new SaveStat("Needs you", shown.Count(g => g.NeedsYou).ToString(CultureInfo.InvariantCulture)),
        ];
        var found = shown.Count - syncing;
        Subtitle = syncing == 0 ? $"No games syncing yet · {found} found with saves" : $"{syncing} {(syncing == 1 ? "game" : "games")} syncing · {found} found, not syncing yet";
    }
}
