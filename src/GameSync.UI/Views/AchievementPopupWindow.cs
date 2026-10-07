using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GameSync.UI.Controls;
using GameSync.UI.Theming;
using GameSync.Windows;

namespace GameSync.UI.Views;

/// <summary>
/// ACH-09: the achievement popup's own small window (design system version 35 → AchievementPopup). It goes in its corner
/// of the screen the person is using, on top of the game, and never takes focus, a click or a key, so the game carries
/// on; nothing about the game is touched. See-through round the card where Windows allows it, for its shadow; otherwise
/// cut to the card's rounded corners.
/// </summary>
public sealed class AchievementPopupWindow : Window
{
    /// <summary>Room round the card for its shadow, when the window is see-through.</summary>
    private const double Room = 28;

    /// <summary>The card's distance from the screen's edges.</summary>
    private const double Edge = 24;

    private readonly Border _frame;

    /// <param name="corner"><c>top-right</c>, <c>top-left</c>, <c>bottom-right</c> or <c>bottom-left</c>.</param>
    public AchievementPopupWindow(GsAchievementPopup popup, string corner)
    {
        Popup = popup;
        Corner = corner;
        Title = "GameSync: achievement unlocked";
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        // Shown off the screen until it has its size, then put in its corner: it never flashes where it isn't meant to be.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(-32000, -32000);
        _frame = new Border { Padding = new Thickness(Room), Child = popup };
        Content = _frame;

        // Dark glass in every theme: what's inside reads the dark theme's colours, whatever GameSync's own look.
        ThemeService.Scope(Resources, ThemeEngine.Build(new ThemeChoice()));
        popup.Opacity = 0;
    }

    public GsAchievementPopup Popup { get; }

    public string Corner { get; }

    /// <summary>It comes in from the left for a left corner, from the right otherwise.</summary>
    public bool FromLeft => Corner.EndsWith("left", StringComparison.Ordinal);

    /// <summary>Shows it in its corner without taking focus, lets clicks through, then slides it in.</summary>
    public Task ShowInCornerAsync()
    {
        var placed = new TaskCompletionSource();
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Place();
            placed.TrySetResult();
        }, DispatcherPriority.Loaded);
        Show();
        return placed.Task.ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => Popup.SlideInAsync(FromLeft)), TaskScheduler.Default).Unwrap();
    }

    /// <summary>Slides it out, then closes it.</summary>
    public async Task HideAsync()
    {
        await Popup.SlideOutAsync(FromLeft);
        Close();
    }

    private void Place()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var seeThrough = ActualTransparencyLevel == WindowTransparencyLevel.Transparent;
        if (!seeThrough)
        {
            // No shadow can show: the window is the card, cut to its corners.
            _frame.Padding = default;
            UpdateLayout();
        }

        var scale = RenderScaling;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);
        if (PopupWindow.ForegroundScreen() is { } screen)
        {
            var edge = (int)Math.Round((Edge - (seeThrough ? Room : 0)) * scale);
            var x = FromLeft ? screen.Left + edge : screen.Right - edge - width;
            var y = Corner.StartsWith("top", StringComparison.Ordinal) ? screen.Top + edge : screen.Bottom - edge - height;
            Position = new PixelPoint(x, y);
        }

        if (!seeThrough)
        {
            PopupWindow.RoundCorners(handle, 0, 0, width, height, (int)Math.Round(20 * scale));
        }

        PopupWindow.MakeClickThrough(handle);
    }
}
