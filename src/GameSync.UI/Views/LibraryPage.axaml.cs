using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.UI.Controls;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// The library's keys (LIB-15, A11Y-02): Ctrl+F puts the cursor in the search, Esc clears it or goes back from a game's
/// page, as Alt+Left and the mouse's back button do, and Up and Down move through the list, where Enter opens a game.
/// </summary>
public partial class LibraryPage : UserControl
{
    private LibraryViewModel? _model;

    public LibraryPage()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey, handledEventsToo: false);
        AddHandler(PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= ModelChanged;
        }

        _model = DataContext as LibraryViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += ModelChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The rail's Search opened the library: the cursor goes to the search once the page is on screen.
        if (_model?.SearchFocusRequests > 0)
        {
            Dispatcher.UIThread.Post(() => SearchBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SearchFocusRequests))
        {
            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus(NavigationMethod.Tab);
                SearchBox.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus(NavigationMethod.Tab);
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused && !string.IsNullOrEmpty(_model.Search))
        {
            _model.Search = "";
            e.Handled = true;
        }
        else if ((e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) && !_model.ShowsCovers && (e.Source is not TextBox || e.Source == SearchBox))
        {
            _model.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up && (e.Source as Visual)?.FindAncestorOfType<ItemsControl>(includeSelf: true) == List)
        {
            e.Handled = MoveInList(e.Source as Control, e.Key == Key.Down ? 1 : -1);
        }
        else if (e.Key == Key.Down && e.Source == SearchBox)
        {
            e.Handled = MoveInList(null, 1);
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && e.Source is GsGameTile tile)
        {
            e.Handled = MoveInCovers(tile, e.Key);
        }
    }

    /// <summary>
    /// A11Y-02: the covers as one grid, favourites first: Left and Right to the cover beside, Up and Down to the one
    /// above or below in the same column, from the favourites' last row to the other games' first.
    /// </summary>
    private bool MoveInCovers(GsGameTile from, Key key)
    {
        var lists = new[] { FavouriteCovers, OtherCovers }.Where(l => l.IsEffectivelyVisible).ToList();
        var grids = lists.Select(l => l.GetRealizedContainers().Select(c => c.GetVisualDescendants().OfType<GsGameTile>().FirstOrDefault()).OfType<GsGameTile>().ToList()).ToList();
        var list = grids.FindIndex(g => g.Contains(from));
        if (list < 0)
        {
            return false;
        }

        var tiles = grids[list];
        var at = tiles.IndexOf(from);
        var columns = TilesPanel.Columns(lists[list].Bounds.Width, 150, 16);
        GsGameTile? to = key switch
        {
            Key.Left => tiles.ElementAtOrDefault(at - 1),
            Key.Right => tiles.ElementAtOrDefault(at + 1),
            Key.Down when at + columns < tiles.Count => tiles[at + columns],
            Key.Down when (at / columns) < ((tiles.Count - 1) / columns) => tiles[^1],
            Key.Down when grids.ElementAtOrDefault(list + 1) is { Count: > 0 } below => below[Math.Min(at % columns, below.Count - 1)],
            Key.Up when at - columns >= 0 => tiles[at - columns],
            Key.Up when list > 0 && grids[list - 1] is { Count: > 0 } above => above[Math.Min((((above.Count - 1) / columns) * columns) + (at % columns), above.Count - 1)],
            _ => null,
        };

        if (to is null)
        {
            return false;
        }

        to.Focus(NavigationMethod.Directional);
        to.BringIntoView();
        return true;
    }

    /// <summary>Scan a folder for games…: the folder, picked in Windows' own folder picker (LIB-23).</summary>
    private async void ScanFolder(object? sender, RoutedEventArgs e)
    {
        if (_model is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "A folder your games are in, such as G:\\ or E:\\Games",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _model.ScanFolder(path);
        }
    }

    /// <summary>The mouse's back button leaves a game's page for the covers.</summary>
    private void OnPointer(object? sender, PointerPressedEventArgs e)
    {
        if (_model is { ShowsCovers: false } && e.GetCurrentPoint(this).Properties.IsXButton1Pressed)
        {
            _model.BackCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Focus to the next or previous heading or game in the list; from the search, the first.</summary>
    private bool MoveInList(Control? from, int step)
    {
        var stops = List.GetRealizedContainers()
            .Select(c => c.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b is GsGameRow or GsListHeading))
            .OfType<Button>()
            .ToList();
        if (stops.Count == 0)
        {
            return false;
        }

        var at = from is null ? -1 : stops.FindIndex(b => b == from || from.FindAncestorOfType<Button>(includeSelf: true) == b);
        var next = at < 0 ? (step > 0 ? 0 : -1) : at + step;
        if (next < 0)
        {
            if (at == 0)
            {
                SearchBox.Focus(NavigationMethod.Directional);
                return true;
            }

            return false;
        }

        if (next >= stops.Count)
        {
            return false;
        }

        stops[next].Focus(NavigationMethod.Directional);
        stops[next].BringIntoView();
        return true;
    }
}
