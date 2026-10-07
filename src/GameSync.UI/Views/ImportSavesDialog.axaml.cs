using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>Import saves: Esc closes it.</summary>
public partial class ImportSavesDialog : UserControl
{
    public ImportSavesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ImportSavesViewModel model && !model.Importing)
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
}
