using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Core.Model;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>
/// Add a place (design system → AddPlaceDialog, FOLD-01): a folder or one save file picked in Windows' own picker,
/// looked at before anything is added (how every PC reads it, what's there, whether it can be a place), what it holds,
/// and Add this place. Over the page, like Properties.
/// </summary>
public sealed partial class AddPlaceViewModel : ObservableObject
{
    /// <summary>What a place can hold, as a game's other places do.</summary>
    public static readonly IReadOnlyList<Choice> Categories =
    [
        new("save", "Game saves: synced between PCs"),
        new("config", "Settings: each PC keeps its own"),
        new("screenshots", "Screenshots: backed up on this PC"),
    ];

    private readonly LauncherActions? _actions;
    private int _looks;

    /// <summary>The place picked, as it was looked at; null until one is picked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlace), nameof(NoPlace), nameof(CanAdd), nameof(IsRefused), nameof(Refused), nameof(Meta), nameof(Portable),
        nameof(PortableNote), nameof(ProgramsNote), nameof(Warning), nameof(PlaceIcon))]
    private NewPlaceLook? _place;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private bool _looking;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryLabel))]
    private string _category = "save";

    public AddPlaceViewModel(GameId id, string title, LauncherActions? actions)
    {
        Id = id;
        Title = title;
        _actions = actions;
        AddCommand = new RelayCommand(Add);
        CancelCommand = new RelayCommand(() => _actions?.CloseDialog?.Invoke());
        SelectCommand = new RelayCommand<string>(category =>
        {
            if (Categories.Any(c => c.Id == category))
            {
                Category = category!;
            }
        });
        OpenCommand = new RelayCommand(() =>
        {
            if (Place is { } place)
            {
                _actions?.OpenFolder?.Invoke(place.Folder);
            }
        }, () => _actions?.OpenFolder is not null);
    }

    public GameId Id { get; }

    public string Title { get; }

    public string Subtitle => $"Where {Title} keeps saves GameSync didn't find";

    public string Intro => $"Pick the folder where {Title} keeps its saves, or one save file. You see what's there before anything is added.";

    public bool HasPlace => Place is not null;

    public bool NoPlace => Place is null;

    public bool IsRefused => Place?.Refused is not null;

    public string? Refused => Place?.Refused;

    public bool CanAdd => Place is { Refused: null } && !Looking;

    public string PlaceIcon => Place?.IsFile == true ? "file" : "folder";

    public string? Portable => Place?.Portable;

    /// <summary>"6 files · 1.1 MB · newest today 21:04", or for one file "1.1 MB · saved today 21:04".</summary>
    public string? Meta => Place is not { Refused: null } place ? null : MetaOf(place, DateTime.Now);

    /// <summary>A place under none of Windows' own folders needs the same drive and folder on the other PCs.</summary>
    public string? PortableNote => Place is { Refused: null, Travels: false } ? "It's under none of Windows' own folders, so your other PCs need the same drive and folder." : null;

    public string? ProgramsNote => Place is { Refused: null, Programs: > 0 } place
        ? $"{(place.Programs == 1 ? "1 program file is" : $"{place.Programs.ToString(CultureInfo.InvariantCulture)} program files are")} there too, and never taken: only save data moves."
        : null;

    public string? Warning => Place is { Refused: null } place ? place.Warning : null;

    public string CategoryLabel => Categories.First(c => c.Id == Category).Label;

    public ICommand AddCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SelectCommand { get; }

    public ICommand OpenCommand { get; }

    /// <summary>Looks at the folder or file picked, off the UI thread; the last pick wins.</summary>
    public async void Look(string path)
    {
        if (_actions?.LookPlace is not { } look)
        {
            return;
        }

        var ticket = ++_looks;
        Looking = true;
        Error = null;
        try
        {
            var place = await look(Id, path, CancellationToken.None);
            if (ticket == _looks)
            {
                Place = place;
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

    private void Add()
    {
        if (Place is not { Refused: null } place || _actions?.AddPlace is not { } add)
        {
            return;
        }

        add(Id, place.Path, Category switch
        {
            "config" => SaveCategory.Config,
            "screenshots" => SaveCategory.Screenshots,
            _ => SaveCategory.Save,
        });
        _actions.CloseDialog?.Invoke();
    }

    public static string MetaOf(NewPlaceLook place, DateTime nowLocal)
    {
        // "today 21:04" inside the line, as "newest 27 Sep 19:30" is.
        var newest = place.NewestUtc is { } at && ConflictViewModel.Day(at, nowLocal) is var day
            ? day.StartsWith("Today", StringComparison.Ordinal) || day.StartsWith("Yesterday", StringComparison.Ordinal) ? char.ToLowerInvariant(day[0]) + day[1..] : day
            : null;
        if (place.IsFile)
        {
            return newest is null ? Cli.FormatSize(place.Bytes) : $"{Cli.FormatSize(place.Bytes)} · saved {newest}";
        }

        var files = place.Files switch
        {
            0 => "Empty for now",
            1 => "1 file",
            var n => $"{(place.More ? "Over " : "")}{n.ToString(CultureInfo.InvariantCulture)} files",
        };
        return place.Files == 0 ? files : string.Join(" · ", new[] { files, Cli.FormatSize(place.Bytes), newest is null ? null : $"newest {newest}" }.OfType<string>());
    }
}
