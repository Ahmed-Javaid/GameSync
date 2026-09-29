using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Import kept saves: Esc closes it, and Choose the folder… and Change… open Windows' own folder picker.</summary>
public partial class ImportKeptDialog : UserControl
{
    public ImportKeptDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ImportKeptViewModel model)
            {
                model.CloseCommand.Execute(null);
                e.Handled = true;
            }
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    private async void PickFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportKeptViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"The folder holding the copies you kept of {model.Title}'s saves", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            model.Read(path);
        }
    }

    private void CloseFlyout(object? sender, RoutedEventArgs e) => GamePage.CloseFlyoutOf(sender);
}
