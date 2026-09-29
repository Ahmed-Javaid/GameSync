using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>
/// A game's Properties: Esc closes it (asking first when there are changes), the copy buttons put their line on the
/// clipboard and say so, and Change… picks the program a game in its own folder starts from.
/// </summary>
public partial class PropertiesDialog : UserControl
{
    public PropertiesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is PropertiesViewModel model)
            {
                model.Close(force: false);
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

    private async void CopyId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertiesViewModel model)
        {
            await CopyAsync(sender, model.Id.Value);
        }
    }

    private async void CopyLine(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PropertiesViewModel model)
        {
            await CopyAsync(sender, model.SteamLine);
        }
    }

    /// <summary>Puts the text on the clipboard, and the button says Copied for a moment (informative feedback).</summary>
    private async Task CopyAsync(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
        if (sender is Controls.GsButton button)
        {
            var was = button.Content;
            button.Content = "Copied";
            button.Icon = "check";
            DispatcherTimer.RunOnce(() =>
            {
                button.Content = was;
                button.Icon = "copy";
            }, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Choose an image…: the game's own cover, banner or logo, picked in Windows' own file picker (ART-06).</summary>
    private async void ChooseArt(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ArtSlot slot || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"An image for {slot.GameTitle}'s {slot.Title.ToLowerInvariant()}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            slot.Choose(path);
        }
    }

    /// <summary>Change…: the program a game in its own folder starts from, picked in Windows' own file picker.</summary>
    private async void PickProgram(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PropertiesViewModel model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"The program {model.Title} starts from",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Programs") { Patterns = ["*.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            model.Program = path;
        }
    }
}
