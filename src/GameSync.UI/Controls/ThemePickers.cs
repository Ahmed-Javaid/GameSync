using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GameSync.UI.Theming;

namespace GameSync.UI.Controls;

/// <summary>A preset card for <see cref="GsThemePicker"/>: the preset, its colours in the current mode, and whether it follows the Windows accent.</summary>
public sealed record ThemeCard(string Id, string Name, IReadOnlyDictionary<string, string> Tokens, bool FollowsAccent) : IHasId
{
    public string Label => FollowsAccent ? $"{Name}, follows your Windows accent colour" : Name;

    /// <summary>What a screen reader says for the card (A11Y-03).</summary>
    public override string ToString() => Label;

    /// <summary>Every preset in one mode, as the picker shows them (LOOK-05).</summary>
    public static IReadOnlyList<ThemeCard> For(ThemeMode mode, bool pureBlack, string? accent) =>
        ThemeEngine.Presets.Select(p => new ThemeCard(p.Id, p.Name, ThemeEngine.Build(new ThemeChoice(p.Id, mode, pureBlack, Accent: accent)), p.FollowsAccent)).ToList();
}

/// <summary>A swatch for <see cref="GsSwatchPicker"/>, in the tone it takes in this theme and role, with the text colour that goes on it.</summary>
public sealed record SwatchCard(string Id, string Name, Color Fill, Color OnFill) : IHasId
{
    public IBrush FillBrush => new SolidColorBrush(Fill);

    public IBrush OnFillBrush => new SolidColorBrush(OnFill);

    /// <summary>What a screen reader says for the swatch (A11Y-03).</summary>
    public override string ToString() => Name;

    /// <summary>Every swatch as primary or secondary of <paramref name="choice"/> (LOOK-06).</summary>
    public static IReadOnlyList<SwatchCard> For(ThemeChoice choice, bool secondary) =>
        ThemeEngine.Swatches.Select(s =>
        {
            var tokens = ThemeEngine.Build(secondary ? choice with { Secondary = s.Id } : choice with { Primary = s.Id });
            var role = secondary ? "secondary" : "primary";
            return new SwatchCard(s.Id, s.Name, ThemeService.ParseColor(tokens[role]), ThemeService.ParseColor(tokens["on-" + role]));
        }).ToList();
}

/// <summary>The preset cards, each a live mini preview of GameSync in the current mode, the chosen one ticked (LOOK-05).</summary>
public class GsThemePicker : GsPillTabs
{
}

/// <summary>Swatches for one theme colour, shown in the tone they take in this mode (LOOK-06).</summary>
public class GsSwatchPicker : GsPillTabs
{
}

/// <summary>A small picture of GameSync in one theme's colours: the rail, the hero with its button, and two cards.</summary>
public sealed class GsThemeMini : Control
{
    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> TokensProperty =
        AvaloniaProperty.Register<GsThemeMini, IReadOnlyDictionary<string, string>?>(nameof(Tokens));

    static GsThemeMini()
    {
        AffectsRender<GsThemeMini>(TokensProperty);
    }

    public IReadOnlyDictionary<string, string>? Tokens
    {
        get => GetValue(TokensProperty);
        set => SetValue(TokensProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        return new Size(width, width * 11 / 16);
    }

    public override void Render(DrawingContext context)
    {
        if (Tokens is not { } v)
        {
            return;
        }

        IBrush B(string key) => new SolidColorBrush(ThemeService.ParseColor(v[key]));
        var w = Bounds.Width;
        var h = Bounds.Height;
        var outer = new RoundedRect(new Rect(0.5, 0.5, w - 1, h - 1), 10);
        context.DrawRectangle(B("bg-100"), new Pen(B("line-100"), 1), outer);

        using (context.PushClip(outer))
        {
            var railWidth = w * 0.16;
            context.DrawLine(new Pen(B("line-100"), 1), new Point(railWidth, 0), new Point(railWidth, h));
            var cx = railWidth / 2;
            context.DrawRectangle(null, new Pen(B("primary"), 2), new RoundedRect(new Rect(cx - 4, 9, 8, 8), 2));
            var y = 9 + 8 + 2 + 5;
            for (var i = 0; i < 3; i++)
            {
                context.DrawRectangle(i == 0 ? B("secondary-soft") : B("bg-300"), null, new RoundedRect(new Rect(cx - 5.5, y, 11, 9), 3));
                if (i == 0)
                {
                    context.DrawRectangle(B("secondary"), null, new RoundedRect(new Rect(cx - 2.5, y + 2, 5, 5), 1));
                }

                y += 9 + 5;
            }

            var main = new Rect(railWidth + 7, 7, w - railWidth - 14, h - 14);
            var heroHeight = (main.Height - 5) * 1.15 / 2.15;
            var hero = new Rect(main.X, main.Y, main.Width, heroHeight);
            context.DrawRectangle(B("bg-300"), null, new RoundedRect(hero, 5));
            context.DrawRectangle(B("ink"), null, new RoundedRect(new Rect(hero.X + hero.Width * 0.08, hero.Bottom - hero.Height * 0.24 - 4, hero.Width * 0.42, 4), 2));
            context.DrawRectangle(B("primary"), null, new RoundedRect(new Rect(hero.Right - hero.Width * 0.07 - hero.Width * 0.28, hero.Bottom - hero.Height * 0.17 - 8, hero.Width * 0.28, 8), 4));

            var cardsTop = hero.Bottom + 5;
            var cardWidth = (main.Width - 5) / 2;
            var cardHeight = main.Bottom - cardsTop;
            for (var i = 0; i < 2; i++)
            {
                var card = new Rect(main.X + i * (cardWidth + 5), cardsTop, cardWidth, cardHeight);
                context.DrawRectangle(B("bg-200"), null, new RoundedRect(card, 4));
                var inner = card.Deflate(5);
                if (i == 0)
                {
                    context.DrawRectangle(B("bg-400"), null, new RoundedRect(new Rect(inner.X, inner.Bottom - 3, inner.Width * 0.9, 3), 1.5));
                    context.DrawRectangle(B("primary"), null, new RoundedRect(new Rect(inner.X, inner.Bottom - 3, inner.Width * 0.9 * 0.62, 3), 1.5));
                    context.DrawRectangle(B("ink-muted"), null, new RoundedRect(new Rect(inner.X, inner.Bottom - 3 - 3 - 3, inner.Width * 0.8, 3), 1.5));
                }
                else
                {
                    context.DrawRectangle(B("ink-faint"), null, new RoundedRect(new Rect(inner.X, inner.Bottom - 3, inner.Width * 0.5, 3), 1.5));
                    context.DrawRectangle(B("secondary"), null, new RoundedRect(new Rect(inner.X, inner.Bottom - 3 - 3 - 5, inner.Width * 0.42, 5), 2.5));
                }
            }
        }
    }
}
