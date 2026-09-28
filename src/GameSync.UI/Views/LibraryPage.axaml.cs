using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        AddHandler(PointerPressedEvent, OnPointer, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
