using Avalonia.Controls;
using GameSync.UI.Controls;
using GameSync.UI.Theming;

namespace GameSync.Snapshots;

/// <summary>The appearance settings with the theme and swatch pickers, under a dialog.</summary>
public partial class Gallery3 : UserControl
{
    public Gallery3()
    {
        InitializeComponent();
        Rail.Items = Gallery.RailItems;
        Mode.ItemsSource = new NavItem[] { new("dark", "Dark", "moon"), new("light", "Light", "sun"), new("system", "Match Windows", "monitor") };
        Themes.ItemsSource = ThemeCard.For(ThemeMode.Dark, pureBlack: false, accent: "#0078d4");
        Primary.ItemsSource = SwatchCard.For(new ThemeChoice(), secondary: false);
        Secondary.ItemsSource = SwatchCard.For(new ThemeChoice(), secondary: true);
    }
}
