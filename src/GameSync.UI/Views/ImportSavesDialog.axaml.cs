using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Import saves: Esc closes it, or the tag's field while it's open, and the field takes the cursor as it opens.</summary>
public partial class ImportSavesDialog : UserControl
{
    private ImportSavesViewModel? _model;

    public ImportSavesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not ImportSavesViewModel model || model.Importing)
            {
                return;
            }

            if (model.TagOpen)
            {
                model.ToggleTagCommand.Execute(null);
            }
            else
            {
                model.CloseCommand.Execute(null);
            }

            e.Handled = true;
        });
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= ModelChanged;
        }

        _model = DataContext as ImportSavesViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += ModelChanged;
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportSavesViewModel.TagOpen) && _model?.TagOpen == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TagBox.Focus(NavigationMethod.Tab);
                TagBox.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }
}
