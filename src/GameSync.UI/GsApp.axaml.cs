using Avalonia;
using Avalonia.Markup.Xaml;
using GameSync.UI.Theming;

namespace GameSync.UI;

/// <summary>GameSync's Avalonia app: the design system's tokens and components, and the theme's colours (Arcade, dark, until the person's choice is read).</summary>
public class GsApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeService.Apply(this, new ThemeChoice());
        TextScale.Apply(Resources, 1);
    }
}
