using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Add a place: Esc closes it, and Choose a folder…, Choose a file… and Change… open Windows' own pickers.</summary>
public partial class AddPlaceDialog : UserControl
{
    public AddPlaceDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is AddPlaceViewModel model)
            {
                model.CancelCommand.Execute(null);
                e.Handled = true;
            }
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The dialog takes the keyboard: the focus moves into it, so Tab and Esc work at once.
        Dispatcher.UIThread.Post(() => Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    private void PickFolder(object? sender, RoutedEventArgs e) => Pick(file: false);

    private void PickFile(object? sender, RoutedEventArgs e) => Pick(file: true);

    /// <summary>Change…: the same kind of picker as the place picked.</summary>
    private void PickAgain(object? sender, RoutedEventArgs e) => Pick(file: DataContext is AddPlaceViewModel { Place.IsFile: true });

    private async void Pick(bool file)
    {
        if (DataContext is not AddPlaceViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        string? path = null;
        if (file)
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = $"A save file of {model.Title}", AllowMultiple = false });
            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        else
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"The folder {model.Title} keeps its saves in", AllowMultiple = false });
            path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }

        if (path is not null)
        {
            model.Look(path);
        }
    }
}
