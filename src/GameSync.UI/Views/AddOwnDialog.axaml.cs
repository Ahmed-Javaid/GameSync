using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Add a game or folder: Esc closes it, and Choose a folder…, Change… and Choose a program… open Windows' own pickers.</summary>
public partial class AddOwnDialog : UserControl
{
    public AddOwnDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is AddOwnViewModel model)
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

    private async void PickFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddOwnViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "The folder to keep in step: a game's saves, or a server's world",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            model.LookAt(path);
        }
    }

    private async void PickProgram(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddOwnViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "The program that uses the folder",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Programs") { Patterns = ["*.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            model.ChooseProgram(path);
        }
    }
}
