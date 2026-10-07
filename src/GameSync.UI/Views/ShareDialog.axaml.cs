using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Share saves: Esc closes it, Windows' own Save As picks the zip's place when Settings says to ask, and Copy path uses the clipboard.</summary>
public partial class ShareDialog : UserControl
{
    private ShareViewModel? _model;

    public ShareDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _model is { } model)
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.CopyText -= Copy;
            _model.PickZipPath = null;
        }

        _model = DataContext as ShareViewModel;
        if (_model is not null)
        {
            _model.CopyText += Copy;
            _model.PickZipPath = PickZipPath;
        }
    }

    private async Task<string?> PickZipPath(string suggested)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Where to save the shared saves",
            SuggestedFileName = suggested,
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
            ShowOverwritePrompt = false,
        });
        return file?.TryGetLocalPath();
    }

    private async void Copy(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
