using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GameSync.UI.ViewModels;

namespace GameSync.UI.Views;

/// <summary>A live save beside copies kept by hand (KAN-61): Esc closes it, and the keyboard is in it as it opens.</summary>
public partial class KeptCopiesDialog : UserControl
{
    public KeptCopiesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is KeptCopiesViewModel model)
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
