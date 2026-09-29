using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>The cloud's two ways; Choose a folder picks it in Windows' own folder picker.</summary>
public partial class CloudChoice : UserControl
{
    public CloudChoice() => InitializeComponent();

    private async void ChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CloudSetupViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "A folder every PC can reach, such as a NAS, a USB drive or another disk",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            model.ChooseFolder(path);
        }
    }
}
