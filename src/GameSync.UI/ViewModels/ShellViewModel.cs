using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.UI.Controls;

namespace GameSync.UI.ViewModels;

/// <summary>
/// The window's frame: the side rail and the page it opens. Home and the game library are the launcher; the save
/// manager and the console hold the save data; settings hold folders and everything else.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private Func<string, object?> _makePage;

    [ObservableProperty]
    private string _current;

    [ObservableProperty]
    private object? _page;

    [ObservableProperty]
    private IReadOnlyList<RailItem> _rail = DefaultRail(null, null);

    public ShellViewModel(Dictionary<string, Func<object?>> pages, string current = "home")
        : this(id => pages.TryGetValue(id, out var make) ? make() : null, current)
    {
    }

    /// <param name="makePage">Makes a page's view model from its rail id.</param>
    public ShellViewModel(Func<string, object?> makePage, string current = "home")
    {
        _makePage = makePage;
        _current = current;
        _page = makePage(current);
    }

    /// <summary>Home, the library, the save manager, the console, and settings at the bottom; dots say a game runs or needs you.</summary>
    public static IReadOnlyList<RailItem> DefaultRail(string? playing, int? needYou) =>
    [
        new RailItem("home", "home", "Home"),
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
        Current = id;
        Page = _makePage(id);
    }
}
