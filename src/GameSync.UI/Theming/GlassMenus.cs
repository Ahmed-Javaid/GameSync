using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GameSync.UI.Controls;

namespace GameSync.UI.Theming;

/// <summary>
/// Menus in Glossy (the owner, 18 Oct 2026, of the library's view menu over a game's red glass: "This should be more in
/// theme"). A menu, a right-click menu or a small ask from a button floats in a little window of its own, outside the
/// page, so it took the plain dialog surface (Mono's grey) while everything round it was glass over the game's art. In
/// Glossy it's cut from the page's glass instead: the window's backdrop where the menu lies, under the page's card
/// surface (<c>bg-200</c>), inside the glass's dialog edge, its rows lit with the page's <c>bg-300</c>, as a card of the
/// page lifted over it. The backdrop is drawn already blurred and darkened for the text laid over it, so the menu keeps
/// the contrast a card keeps there. Solid keeps the dialog surface.
/// </summary>
public static class GlassMenus
{
    private static readonly ConditionalWeakTable<TemplatedControl, Fill> Fills = new();

    /// <summary>The Fluent brushes a menu's rows and edge take, and the page's glass tokens they take in Glossy.</summary>
    private static readonly (string Fluent, string Token)[] Rows =
    [
        ("MenuFlyoutItemBackgroundPointerOver", "bg-300"),
        ("MenuFlyoutItemBackgroundPressed", "bg-400"),
        ("MenuFlyoutPresenterBorderBrush", "edge-dialog"),
        ("FlyoutBorderThemeBrush", "edge-dialog"),
    ];

    private static bool _started;

    /// <summary>Follows every menu, right-click menu and small ask as it opens.</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Control.LoadedEvent.AddClassHandler<MenuFlyoutPresenter>((menu, _) => Follow(menu));
        Control.LoadedEvent.AddClassHandler<ContextMenu>((menu, _) => Follow(menu));
        Control.LoadedEvent.AddClassHandler<FlyoutPresenter>((menu, _) => Follow(menu));
    }

    private static void Follow(TemplatedControl menu) => Fills.GetValue(menu, m => new Fill(m)).Update();

    /// <summary>
    /// The backdrop's picture where <paramref name="area"/> (in the backdrop's own coordinates) lies, as the backdrop
    /// draws it (scaled to cover and centred), over <paramref name="ground"/> where the area runs past the window, and
    /// under <paramref name="surface"/>; at <paramref name="scaling"/> pixels to a unit.
    /// </summary>
    public static Bitmap Slice(Bitmap art, Size backdrop, Rect area, IBrush? ground, IBrush? surface, double scaling)
    {
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(area.Width * scaling)), Math.Max(1, (int)Math.Ceiling(area.Height * scaling)));
        var slice = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
        var shown = Placed(art.Size, backdrop, area.Position);
        var local = new Rect(area.Size);
        using (var context = slice.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            if (ground is not null)
            {
                context.FillRectangle(ground, local);
            }

            context.DrawImage(art, shown);
            if (surface is not null)
            {
                context.FillRectangle(surface, local);
            }
        }

        return slice;
    }

    /// <summary>
    /// Where the whole of the backdrop's picture lies, seen from a point <paramref name="at"/> of the backdrop: scaled to
    /// cover the backdrop and centred, as <see cref="GsBackdrop"/> draws it.
    /// </summary>
    public static Rect Placed(Size art, Size backdrop, Point at)
    {
        var scale = Math.Max(backdrop.Width / art.Width, backdrop.Height / art.Height);
        return new Rect((backdrop.Width - art.Width * scale) / 2 - at.X, (backdrop.Height - art.Height * scale) / 2 - at.Y, art.Width * scale, art.Height * scale);
    }

    /// <summary>One menu's glass: made again whenever it moves, changes size or the backdrop under it changes.</summary>
    private sealed class Fill
    {
        private readonly TemplatedControl _menu;
        private (Bitmap Art, Rect Area, double Scaling, IBrush? Surface)? _shown;
        private Popup? _popup;
        private WindowBase? _ownWindow;

        public Fill(TemplatedControl menu)
        {
            _menu = menu;
            menu.LayoutUpdated += (_, _) => Update();
        }

        public void Update()
        {
            if (!_menu.IsAttachedToVisualTree())
            {
                return;
            }

            if (_menu.Parent is not Popup { PlacementTarget: { } target } popup || TopLevel.GetTopLevel(target) is not { } window
                || window.GetVisualDescendants().OfType<GsBackdrop>().FirstOrDefault() is not { Source: Bitmap art } backdrop)
            {
                Plain();
                return;
            }

            Watch(popup, window);
            if (_menu.Bounds.Width < 1 || _menu.Bounds.Height < 1 || Offset(backdrop) is not { } at)
            {
                return;
            }

            var area = new Rect(at, _menu.Bounds.Size);
            var scaling = TopLevel.GetTopLevel(_menu)?.RenderScaling ?? window.RenderScaling;
            var surface = Brush(target, "bg-200");
            if (_shown is { } shown && ReferenceEquals(shown.Art, art) && shown.Area == area && shown.Scaling == scaling && ReferenceEquals(shown.Surface, surface))
            {
                return;
            }

            _shown = (art, area, scaling, surface);
            _menu.Background = new ImageBrush(Slice(art, backdrop.Bounds.Size, area, window.Background, surface, scaling)) { Stretch = Stretch.Fill };
            _menu.BorderBrush = Brush(target, "edge-dialog");
            foreach (var (fluent, token) in Rows)
            {
                if (Brush(target, token) is { } brush)
                {
                    _menu.Resources[fluent] = brush;
                }
            }
        }

        /// <summary>
        /// A menu in a window of its own is put in its place on the screen after it's laid out, and may move with it, so its
        /// glass is cut again once it's open and when its window moves.
        /// </summary>
        private void Watch(Popup popup, TopLevel window)
        {
            if (!ReferenceEquals(_popup, popup))
            {
                _popup = popup;
                popup.Opened += (_, _) => Update();
            }

            if (TopLevel.GetTopLevel(_menu) is WindowBase own && !ReferenceEquals(own, window) && !ReferenceEquals(own, _ownWindow))
            {
                _ownWindow = own;
                own.PositionChanged += (_, _) => Update();
            }
        }

        /// <summary>Solid, or no backdrop: the dialog surface the theme gives every menu.</summary>
        private void Plain()
        {
            if (_shown is null)
            {
                return;
            }

            _shown = null;
            _menu.ClearValue(TemplatedControl.BackgroundProperty);
            _menu.ClearValue(TemplatedControl.BorderBrushProperty);
            foreach (var (fluent, _) in Rows)
            {
                _menu.Resources.Remove(fluent);
            }
        }

        /// <summary>The menu's top left in the backdrop's coordinates: within one window, or between the menu's own and it.</summary>
        private Point? Offset(Visual backdrop)
        {
            if (TopLevel.GetTopLevel(_menu) is not { } own || TopLevel.GetTopLevel(backdrop) is not { } window)
            {
                return null;
            }

            if (ReferenceEquals(own, window))
            {
                return _menu.TranslatePoint(default, backdrop);
            }

            var menu = _menu.PointToScreen(default);
            var under = backdrop.PointToScreen(default);
            return new Point((menu.X - under.X) / window.RenderScaling, (menu.Y - under.Y) / window.RenderScaling);
        }

        /// <summary>A token as the page that opened the menu has it: Glossy's see-through value inside the window's frame.</summary>
        private static IBrush? Brush(Control target, string token) =>
            target.TryFindResource(token, target.ActualThemeVariant, out var value) ? value as IBrush : null;
    }
}
