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
    private readonly Dictionary<string, Func<object?>> _pages;

    [ObservableProperty]
    private string _current;

    [ObservableProperty]
    private object? _page;

    public ShellViewModel(Dictionary<string, Func<object?>> pages, string current = "home")
    {
        _pages = pages;
        _current = current;
        _page = pages.TryGetValue(current, out var make) ? make() : null;
    }

    public IReadOnlyList<RailItem> Rail { get; init; } = DefaultRail(null, null);

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

    [RelayCommand]
    private void Navigate(string id)
    {
        Current = id;
        Page = _pages.TryGetValue(id, out var make) ? make() : null;
    }
}
