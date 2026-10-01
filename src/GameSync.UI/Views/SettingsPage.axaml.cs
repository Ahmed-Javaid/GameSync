using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// Settings: its folders are picked in Windows' own folder picker, Copy diagnostics puts its text on the clipboard,
/// and every setting is read again each time the page shows.
/// </summary>
public partial class SettingsPage : UserControl
{
    private SettingsViewModel? _model;

    public SettingsPage()
    {
        // Before the page is built: the folder fields and lists read them once, as they're bound.
        MoveBackupCommand = new RelayCommand(PickBackupFolder);
        AddGameFolderCommand = new RelayCommand(PickGameFolder);
        AddSaveFolderCommand = new RelayCommand(PickSaveFolder);
        InitializeComponent();
    }

    /// <summary>Change… on the backup folder (FOLD-02).</summary>
    public ICommand MoveBackupCommand { get; }

    /// <summary>Add folder in Game folders to scan (FOLD-07).</summary>
    public ICommand AddGameFolderCommand { get; }

    /// <summary>Add folder in Extra save folders (FOLD-08).</summary>
    public ICommand AddSaveFolderCommand { get; }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _model?.Open();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.CopyText -= Copy;
            _model.PropertyChanged -= SectionChanged;
        }

        _model = DataContext as SettingsViewModel;
        if (_model is not null)
        {
            _model.CopyText += Copy;
            _model.PropertyChanged += SectionChanged;
            if (VisualRoot is not null)
            {
                _model.Open();
            }
        }
    }

    private void CloseFlyout(object? sender, RoutedEventArgs e) => GamePage.CloseFlyoutOf(sender);

    /// <summary>Another section starts at its top, not where the last one was scrolled to.</summary>
    private void SectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Section))
        {
            Scroller.Offset = default;
        }
    }

    private async void Copy(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void PickBackupFolder()
    {
        if (_model is { } model && await PickFolder("A folder for GameSync's backups on this PC") is { } picked)
        {
            await model.Storage.MoveBackup(SettingsData.BackupFolderFor(picked));
        }
    }

    private async void PickGameFolder()
    {
        if (_model is { } model && await PickFolder("A folder your games are in, such as G:\\ or E:\\Games") is { } picked)
        {
            await model.Storage.AddGameFolder(picked);
        }
    }

    private async void PickSaveFolder()
    {
        if (_model is { } model && await PickFolder("A folder with saves in it") is { } picked)
        {
            await model.Storage.AddSaveFolder(picked);
        }
    }

    private async Task<string?> PickFolder(string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
