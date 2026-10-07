using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.Core.Model;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class SaveManagerPage : UserControl
{
    public SaveManagerPage()
    {
        InitializeComponent();
        // Back from a game's saves or its conflict: Esc, Alt+Left and the mouse's back button, as the Back button does.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (BackOf(DataContext) is { } back && (e.Key == Key.Escape || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)) &&
                e.Source is not TextBox)
            {
                back.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: false);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.XButton1 && BackOf(DataContext) is { } back)
            {
                back.Execute(null);
                e.Handled = true;
            }
        }, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnRowKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            // A row's button clicks as Space comes up; on a row Space ticks, it mustn't open the game as well.
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None &&
                (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: SaveRow { Selectable: true } })
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A11Y-02 (design system → Shortcuts: Up and Down move through the list): each table is one Tab stop; Up, Down,
    /// Home, End, Page Up and Page Down go from row to row, Space ticks a game that syncs for sharing, and Enter opens
    /// it, as the row's click does. The Versions tab's table moves the same way, and Enter opens a version's game.
    /// </summary>
    private void OnRowKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not { DataContext: SaveRow or VersionRow } button ||
            button.FindAncestorOfType<ItemsControl>() is not { } table)
        {
            return;
        }

        // PERF-03: only the rows in view are made, so the next one is found by its place in the whole table and brought in.
        var item = button.DataContext!;
        var count = table.ItemCount;
        var at = (table.ItemsSource as System.Collections.IList)?.IndexOf(item) ?? -1;
        int? next = e.Key switch
        {
            Key.Down => at + 1,
            Key.Up => at - 1,
            Key.PageDown => at + 10,
            Key.PageUp => at - 10,
            Key.Home => 0,
            Key.End => count - 1,
            _ => null,
        };
        if (next is { } to && count > 0 && at >= 0)
        {
            var index = Math.Clamp(to, 0, count - 1);
            table.ScrollIntoView(index);
            table.UpdateLayout();
            if (table.ContainerFromIndex(index)?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is SaveRow or VersionRow) is { } target)
            {
                target.Focus(NavigationMethod.Directional);
                target.BringIntoView();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Space && item is SaveRow { Selectable: true } row && DataContext is SaveManagerViewModel model)
        {
            model.SelectCommand.Execute(row.Id);
            e.Handled = true;
            FocusWhenMade(table, row.Id);
        }
    }

    /// <summary>
    /// A tick makes the table again, row by row, at its next layout: focus goes to the game's new row once it's there,
    /// so the old row taking focus away with it leaves the keyboard where it was.
    /// </summary>
    private static void FocusWhenMade(ItemsControl table, GameId id)
    {
        if ((table.ItemsSource as IEnumerable<SaveRow>)?.FirstOrDefault(r => r.Id == id) is not { } wanted)
        {
            return;
        }

        var passes = 0;
        void TryFocus(object? sender, EventArgs e)
        {
            if (Rows(table).FirstOrDefault(b => ReferenceEquals(b.DataContext, wanted)) is { } made)
            {
                table.LayoutUpdated -= TryFocus;
                made.Focus(NavigationMethod.Directional);
            }
            else if (++passes > 5)
            {
                table.LayoutUpdated -= TryFocus;
            }
        }

        table.LayoutUpdated += TryFocus;
        TryFocus(null, EventArgs.Empty);
    }

    private static List<Button> Rows(ItemsControl table) =>
        table.GetRealizedContainers()
            .Select(c => c.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is SaveRow))
            .OfType<Button>()
            .ToList();

    /// <summary>Import saves (SHARE-10): a zip shared from GameSync, picked in Windows' own picker.</summary>
    private async void PickZip(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveManagerViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "A zip of saves shared from GameSync",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
        });
        if (files.Count > 0 && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) is { } path)
        {
            model.ImportZip(path);
        }
    }

    /// <summary>Copy the log (MGR-09): the lines shown go on the clipboard, and the button says Copied for a moment.</summary>
    private async void CopyLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveManagerViewModel { LogTab: { } log } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(log.CopyText());
        if (sender is Controls.GsButton button)
        {
            button.Content = "Copied";
            button.Icon = "check";
            DispatcherTimer.RunOnce(() =>
            {
                button.Content = "Copy the log";
                button.Icon = "copy";
            }, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>The Back of the page showing: the conflict's, or the game's saves'; none over every game's saves.</summary>
    private static ICommand? BackOf(object? context) => context switch
    {
        SaveManagerViewModel { Conflict: { } conflict } => conflict.BackCommand,
        SaveManagerViewModel { Game: not null } page => page.BackCommand,
        _ => null,
    };
}
