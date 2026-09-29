using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>What Add a game or folder asks of the app: look at a folder, check a program, add, open a folder, and close.</summary>
/// <param name="CheckProgram">The program's game folder, or a <see cref="UsageException"/> saying why it can't be the one.</param>
/// <param name="Add">Adds it; a <see cref="UsageException"/> says why it couldn't be.</param>
public sealed record OwnActions(
    Func<string, CancellationToken, Task<NewPlaceLook>> Look,
    Func<string, CancellationToken, Task<string>> CheckProgram,
    Func<OwnGame, CancellationToken, Task<OwnGameAdded>> Add,
    Action Close)
{
    /// <summary>Opens a folder in Explorer.</summary>
    public Action<string>? OpenFolder { get; init; }
}

/// <summary>
/// Add a game or folder (design system → AddGameDialog, LIB-13): a name, a folder picked in Windows' picker, and the
/// program that uses it, if there is one. From the library's Add game it's Add and sync; over first run (<see cref="Setup"/>)
/// it's Add, and the game joins Choose games, ticked.
/// </summary>
public sealed partial class AddOwnViewModel : ObservableObject
{
    private readonly OwnActions _actions;
    private int _looks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(AddTip))]
    private string _name = "";

    /// <summary>The folder picked, as it was looked at; null until one is picked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(NoFolder), nameof(IsRefused), nameof(Refused), nameof(Meta), nameof(ProgramsNote), nameof(Warning),
        nameof(CanAdd), nameof(AddTip))]
    private NewPlaceLook? _folder;

    /// <summary>The program that uses it, in full; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgram), nameof(NoProgram), nameof(ProgramHelp))]
    private string? _program;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(AddTip))]
    private bool _looking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(AddLabel))]
    private bool _adding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public AddOwnViewModel(OwnActions actions, bool setup = false)
    {
        _actions = actions;
        Setup = setup;
        AddCommand = new AsyncRelayCommand(AddAsync);
        CancelCommand = new RelayCommand(actions.Close);
        RemoveProgramCommand = new RelayCommand(() => Program = null);
        OpenCommand = new RelayCommand(() =>
        {
            if (Folder is { } folder)
            {
                _actions.OpenFolder?.Invoke(folder.Folder);
            }
        }, () => actions.OpenFolder is not null);
    }

    /// <summary>Over first run: Add rather than Add and sync, since nothing syncs until first run ends.</summary>
    public bool Setup { get; }

    public bool HasFolder => Folder is not null;

    public bool NoFolder => Folder is null;

    public bool IsRefused => Folder?.Refused is not null;

    public string? Refused => Folder?.Refused;

    public bool HasProgram => Program is not null;

    public bool NoProgram => Program is null;

    public bool HasError => Error is not null;

    /// <summary>"142 files · 38.4 MB · last changed today 20:55".</summary>
    public string? Meta => Folder is { Refused: null } folder ? MetaOf(folder, DateTime.Now) : null;

    public string ProgramHelp => Program is null
        ? "Optional. With one, GameSync syncs when it closes; without one, once the folder has been quiet for 5 minutes."
        : "GameSync syncs it when this program closes, and Play starts it.";

    /// <summary>Program files are never copied (R1): how many the folder holds, once it's picked.</summary>
    public string? ProgramsNote => Folder switch
    {
        { Refused: not null } => null,
        { Programs: > 0 } folder =>
            $"It holds {(folder.Programs == 1 ? "1 program file" : $"{folder.Programs.ToString(CultureInfo.InvariantCulture)} program files")} (.exe, .jar and the like), and they're never copied: pick the save or world folder rather than the whole game or server.",
        _ => "Programs in the folder (.exe, .dll, .jar and the like) are never copied, so pick the save or world folder rather than the whole game or server.",
    };

    /// <summary>It can be added, but another game keeps saves there too (FOLD-11).</summary>
    public string? Warning => Folder is { Refused: null } folder ? folder.Warning : null;

    public bool CanAdd => Name.Trim().Length > 0 && Folder is { Refused: null } && !Looking && !Adding;

    /// <summary>Why Add can't be pressed yet (A11Y-05: nothing that does nothing without saying why).</summary>
    public string? AddTip => CanAdd || Adding ? null
        : Folder is null ? "Choose its folder first"
        : Folder.Refused is not null ? "Choose another folder"
        : Looking ? "Looking at the folder"
        : "Give it a name first";

    public string AddLabel => Adding ? "Adding…" : Setup ? "Add" : "Add and sync";

    public string FootNote => Setup
        ? "It joins Choose games, ticked. Nothing syncs until you start using GameSync."
        : "It's backed up now and synced from then on. On your other PC, add its folder under the same name.";

    public ICommand AddCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RemoveProgramCommand { get; }

    public ICommand OpenCommand { get; }

    /// <summary>A folder picked in Windows' picker: looked at off the UI thread, and it names the game when it has no name yet. The last pick wins.</summary>
    public async void LookAt(string path)
    {
        var ticket = ++_looks;
        Looking = true;
        Error = null;
        try
        {
            var folder = await _actions.Look(path, CancellationToken.None);
            if (ticket != _looks)
            {
                return;
            }

            Folder = folder;
            if (folder.Refused is null && Name.Trim().Length == 0)
            {
                Name = OwnGames.SuggestName(folder.Folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            if (ticket == _looks)
            {
                Error = $"GameSync couldn't look at {path}: {e.Message}";
            }
        }
        finally
        {
            if (ticket == _looks)
            {
                Looking = false;
            }
        }
    }

    /// <summary>A program picked in Windows' picker: checked, then it's the one that uses the folder.</summary>
    public async void ChooseProgram(string path)
    {
        Error = null;
        try
        {
            await _actions.CheckProgram(path, CancellationToken.None);
            Program = path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            Error = e.Message;
        }
    }

    private async Task AddAsync()
    {
        if (!CanAdd || Folder is not { } folder)
        {
            return;
        }

        Adding = true;
        Error = null;
        try
        {
            await _actions.Add(new OwnGame(Name.Trim(), folder.Folder, Program), CancellationToken.None);
            _actions.Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or UsageException)
        {
            Error = e.Message;
        }
        finally
        {
            Adding = false;
        }
    }

    public static string MetaOf(NewPlaceLook folder, DateTime nowLocal)
    {
        var files = folder.Files switch
        {
            0 => "Empty for now",
            1 => "1 file",
            var n => $"{(folder.More ? "Over " : "")}{n.ToString(CultureInfo.InvariantCulture)} files",
        };
        if (folder.Files == 0)
        {
            return files;
        }

        var changed = folder.NewestUtc is { } at && ConflictViewModel.Day(at, nowLocal) is var day
            ? day.StartsWith("Today", StringComparison.Ordinal) || day.StartsWith("Yesterday", StringComparison.Ordinal) ? char.ToLowerInvariant(day[0]) + day[1..] : day
            : null;
        return string.Join(" · ", new[] { files, Cli.FormatSize(folder.Bytes), changed is null ? null : $"last changed {changed}" }.OfType<string>());
    }

    public override string ToString() => "Add a game or folder";
}
