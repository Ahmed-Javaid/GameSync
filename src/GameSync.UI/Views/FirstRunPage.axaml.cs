using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>First run: the scan starts once the page is on screen, and Add a game folder picks one in Windows' folder picker.</summary>
public partial class FirstRunPage : UserControl
{
    public FirstRunPage()
    {
        // Before the page is built: the folder list's Add button reads it once, as it's bound.
        AddFolderCommand = new RelayCommand(PickGameFolder);
        InitializeComponent();
    }

    /// <summary>Add a game folder, in Your games.</summary>
    public ICommand AddFolderCommand { get; }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as FirstRunViewModel)?.Start();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            (DataContext as FirstRunViewModel)?.Start();
        }
    }

    /// <summary>Where saves are kept → A folder every PC can reach: Choose a folder…, in Windows' own folder picker.</summary>
    private async void ChooseCloudFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not FirstRunViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
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
            model.Cloud.ChooseFolder(path);
        }
    }

    private async void PickGameFolder()
    {
        if (DataContext is not FirstRunViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
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
            model.AddFolder(path);
        }
    }
}
