using Avalonia;
using Avalonia.Controls;

namespace GameSync.UI.Views;

public partial class Shell : UserControl
{
    /// <summary>The window's title bar height when the page reaches up into it; 0 in a window with Windows' own title bar.</summary>
    public static readonly StyledProperty<double> TitleBarHeightProperty = AvaloniaProperty.Register<Shell, double>(nameof(TitleBarHeight));

    public static readonly StyledProperty<Thickness> PageMarginProperty = AvaloniaProperty.Register<Shell, Thickness>(nameof(PageMargin));

    /// <summary>How far a page's own top margin reaches up under the title bar's strip, so the page starts close to the top.</summary>
    private const double UnderTitleBar = 16;

    public Shell() => InitializeComponent();

    public double TitleBarHeight
    {
        get => GetValue(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>Where the page starts: just under the title bar's strip, overlapping the page's clear top margin.</summary>
    public Thickness PageMargin
    {
        get => GetValue(PageMarginProperty);
        private set => SetValue(PageMarginProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleBarHeightProperty)
        {
            PageMargin = new Thickness(0, Math.Max(0, TitleBarHeight - UnderTitleBar), 0, 0);
        }
    }
}
