using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class GameSavesPage : UserControl
{
    private GameSavesViewModel? _watched;

    public GameSavesPage()
    {
        InitializeComponent();
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
}
