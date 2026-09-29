using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

public partial class GamePage : UserControl
{
    public GamePage() => InitializeComponent();

    /// <summary>A button inside a flyout (Save, Restore) closes the flyout it's in once it's done its job.</summary>
    private void CloseFlyout(object? sender, RoutedEventArgs e) => CloseFlyoutOf(sender);

    /// <summary>
    /// Closes the flyout a control sits in, as its own buttons do when they've acted. A button raises Click before it
    /// runs its command, and a closed flyout's buttons lose their bindings, so the flyout closes once the command has run.
    /// </summary>
    public static void CloseFlyoutOf(object? sender)
    {
        if ((sender as Control)?.FindLogicalAncestorOfType<Popup>() is { PlacementTarget: Button { Flyout: { } flyout } })
        {
            Dispatcher.UIThread.Post(flyout.Hide, DispatcherPriority.Background);
        }
    }

    /// <summary>Locate the game…: its program, picked in Windows' own file picker (LIB-24).</summary>
    private async void Locate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"{model.Title}'s program, in the game's own folder",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Programs") { Patterns = ["*.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            model.Locate(path);
        }
    }
}
