using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>New named save: the name box has the cursor as it opens, and Esc closes it.</summary>
public partial class NamedSaveDialog : UserControl
{
    public NamedSaveDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is NamedSaveViewModel model)
            {
                model.CancelCommand.Execute(null);
                e.Handled = true;
            }
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => NameBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }
}
