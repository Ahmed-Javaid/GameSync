using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class GameSavesPage : UserControl
{
    private GameSavesViewModel? _watched;

    public GameSavesPage()
    {
        InitializeComponent();
        NamedList.AddHandler(KeyDownEvent, MoveInNamedSaves);
        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnChanged;
            }

            _watched = DataContext as GameSavesViewModel;
            if (_watched is not null)
            {
                _watched.PropertyChanged += OnChanged;
                ShowPicked();
            }
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameSavesViewModel.Versions))
        {
            ShowPicked();
        }
    }

    /// <summary>The version a row of the Versions tab picked comes into view once its row is laid out (MGR-08).</summary>
    private void ShowPicked() => Dispatcher.UIThread.Post(() =>
    {
        if (_watched?.Versions.FirstOrDefault(v => v.IsPicked) is { } picked && VersionList.ContainerFromItem(picked) is { } row)
        {
            row.BringIntoView();
        }
    }, DispatcherPriority.Loaded);

    /// <summary>A button inside a flyout (Save, Restore) closes the flyout it's in once it's done its job.</summary>
    private void CloseFlyout(object? sender, RoutedEventArgs e) => GamePage.CloseFlyoutOf(sender);

    /// <summary>
    /// A11Y-02: the named saves are one Tab stop. Up and Down go to the same button of the save above or below (counted
    /// from the right, as a save in place has no Restore), Left and Right between a save's own buttons.
    /// </summary>
    private void MoveInNamedSaves(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual from)
        {
            return;
        }

        var rows = NamedList.GetRealizedContainers()
            .Select(row => row.GetVisualDescendants().OfType<Button>().Where(b => b.Focusable && b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToList())
            .Where(row => row.Count > 0)
            .ToList();
        var button = from as Button ?? from.FindAncestorOfType<Button>();
        var at = rows.FindIndex(row => row.Contains(button!));
        if (button is null || at < 0)
        {
            return;
        }

        var row = rows[at];
        var fromRight = row.Count - 1 - row.IndexOf(button);
        Button? to = e.Key switch
        {
            Key.Left => row.ElementAtOrDefault(row.IndexOf(button) - 1),
            Key.Right => row.ElementAtOrDefault(row.IndexOf(button) + 1),
            _ when rows.ElementAtOrDefault(at + (e.Key == Key.Down ? 1 : -1)) is { } next => next[Math.Max(0, next.Count - 1 - fromRight)],
            _ => null,
        };

        if (to is not null)
        {
            to.Focus(NavigationMethod.Directional);
            to.BringIntoView();
            e.Handled = true;
        }
    }

    /// <summary>KAN-87: Export as a folder…: the folder to put it in, picked in Windows' own picker, then the export.</summary>
    private async void ExportFolder(object? sender, RoutedEventArgs e)
    {
        GamePage.CloseFlyoutOf(sender);
        if (sender is not Control { DataContext: NamedSaveItem named } || _watched is not { } page || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Where to put “{named.Name}” as a folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } parent)
        {
            await page.ExportFolderAsync(named, parent);
        }
    }
}
