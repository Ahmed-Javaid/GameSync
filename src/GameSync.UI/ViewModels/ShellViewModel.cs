using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.UI.ViewModels;

/// <summary>
/// The window's frame: the side rail and the page it opens. Home and the game library are the launcher; the save
/// manager and the console hold the save data; settings hold folders and everything else.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>The rail's Search: the library, with the cursor in its search field (LIB-15).</summary>
    public const string SearchId = "search";

    private Func<string, object?> _makePage;

    [ObservableProperty]
    private string _current;

    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private IReadOnlyList<RailItem> _rail = DefaultRail(null, null);

    /// <summary>How much of Glossy's backdrop the page shown lets through, and the rail with it (LOOK-17).</summary>
    [ObservableProperty]
    private GlassStrength _strength;

    /// <summary>The art Glossy shows behind the page, when the page has its own game (a game's page); null takes the last-played game's.</summary>
    [ObservableProperty]
    private string? _backdropArt;

    /// <summary>A dialog over the page, such as a game's Properties; the page waits behind it until it closes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private object? _dialog;

    public bool HasDialog => Dialog is not null;

    public ShellViewModel(Dictionary<string, Func<object?>> pages, string current = "home")
        : this(id => pages.TryGetValue(id, out var make) ? make() : null, current)
    {
    }

    /// <param name="makePage">Makes a page's view model from its rail id.</param>
    public ShellViewModel(Func<string, object?> makePage, string current = "home")
    {
        _makePage = makePage;
        _current = current == SearchId ? "library" : current;
        _page = makePage(_current);
        Follow(null, _page);
    }

    /// <summary>A page's strength; pages that don't say take the glow, the calmest.</summary>
    public static GlassStrength StrengthOf(object? page) => (page as IPageSurface)?.Strength ?? GlassStrength.Glow;

    partial void OnPageChanged(object? oldValue, object? newValue) => Follow(oldValue, newValue);

    /// <summary>Search at the top; Home, the library, the save manager, the console, and settings at the bottom; dots say a game runs or needs you.</summary>
    public static IReadOnlyList<RailItem> DefaultRail(string? playing, int? needYou) =>
    [
        new RailItem(SearchId, "search", "Search"),
        new RailItem("home", "home", "Home", Separator: true),
        new RailItem("library", "library", "Game library", Dot: playing is null ? null : "play", DotLabel: playing is null ? null : $"{playing} is running"),
        new RailItem("saves", "saves", "Save manager", Separator: true, Dot: needYou > 0 ? "warn" : null,
            DotLabel: needYou > 0 ? (needYou == 1 ? "1 game needs you" : $"{needYou} games need you") : null),
        new RailItem("log", "terminal", "Console"),
        new RailItem("settings", "settings", "Settings", Bottom: true),
    ];

    /// <summary>Makes the page shown now again, from fresh data; with <paramref name="makePage"/>, every page from now on too.</summary>
    public void Reload(Func<string, object?>? makePage = null)
    {
        if (makePage is not null)
        {
            _makePage = makePage;
        }

        Page = _makePage(Current);
    }

    /// <summary>Opens a page as its rail button does, as when a page's own button leads to another.</summary>
    public void Open(string id) => Navigate(id);

    [RelayCommand]
    private void Navigate(string id)
    {
        if (id == SearchId)
        {
            Navigate("library");
            (Page as ISearchablePage)?.FocusSearch();
            return;
        }

        Current = id;
        Page = _makePage(id);
    }

    /// <summary>A page that changes its own strength or art (the library opening a game's page) takes the frame with it.</summary>
    private void Follow(object? old, object? page)
    {
        if (old is INotifyPropertyChanged before)
        {
            before.PropertyChanged -= PageChanged;
        }

        if (page is INotifyPropertyChanged after)
        {
            after.PropertyChanged += PageChanged;
        }

        Strength = StrengthOf(page);
        BackdropArt = (page as IPageSurface)?.BackdropArt;
    }

    private void PageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Page && e.PropertyName is nameof(IPageSurface.Strength) or nameof(IPageSurface.BackdropArt) or null)
        {
            Strength = StrengthOf(Page);
            BackdropArt = (Page as IPageSurface)?.BackdropArt;
        }
    }
}
