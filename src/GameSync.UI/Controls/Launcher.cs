using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GameSync.Core.State;

namespace GameSync.UI.Controls;

/// <summary>Lays out one child at a fixed height-to-width ratio, as the 2:3 tiles are.</summary>
public sealed class AspectPanel : Panel
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectPanel, double>(nameof(Ratio), 1.5);

    static AspectPanel() => AffectsMeasure<AspectPanel>(RatioProperty);

    /// <summary>Height over width: 1.5 for Steam's 600×900 capsules.</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width;
        var size = new Size(width, width * Ratio);
        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }
}

/// <summary>
/// A 2:3 cover tile with the game's name, a meta line, and a status badge unless the game is synced, so problems stand
/// out. No art makes a title cover: the name on <c>bg-300</c> with its first letter large behind it, never an empty box.
/// </summary>
public class GsGameTile : Button
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsGameTile, string?>(nameof(Title));

    public static readonly StyledProperty<IImage?> ArtProperty = AvaloniaProperty.Register<GsGameTile, IImage?>(nameof(Art));

    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsGameTile, GameStatus?>(nameof(Status));

    public static readonly StyledProperty<string?> MetaProperty = AvaloniaProperty.Register<GsGameTile, string?>(nameof(Meta));

    public static readonly DirectProperty<GsGameTile, string> InitialProperty =
        AvaloniaProperty.RegisterDirect<GsGameTile, string>(nameof(Initial), t => t.Initial);

    public static readonly DirectProperty<GsGameTile, bool> ShowsStatusProperty =
        AvaloniaProperty.RegisterDirect<GsGameTile, bool>(nameof(ShowsStatus), t => t.ShowsStatus);

    private string _initial = "?";
    private bool _showsStatus;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public string Initial
    {
        get => _initial;
        private set => SetAndRaise(InitialProperty, ref _initial, value);
    }

    public bool ShowsStatus
    {
        get => _showsStatus;
        private set => SetAndRaise(ShowsStatusProperty, ref _showsStatus, value);
    }

    /// <summary>The first letter or digit of a title, for its title cover.</summary>
    public static string InitialOf(string? title) => title?.FirstOrDefault(char.IsLetterOrDigit) is { } c and not '\0' ? char.ToUpperInvariant(c).ToString() : "?";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            Initial = InitialOf(Title);
            Avalonia.Automation.AutomationProperties.SetName(this, Title);
        }
        else if (change.Property == StatusProperty)
        {
            // Synced games and games not syncing yet show no badge, so the problems stand out.
            ShowsStatus = Status is { } s && s != GameStatus.Synced;
        }
        else if (change.Property == ArtProperty)
        {
            PseudoClasses.Set(":art", Art is not null);
        }
    }
}

/// <summary>A pill on cover art (the hero's playtime): <c>glass</c> behind <c>on-art</c> text, the same in every theme.</summary>
public class GsChip : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<GsChip, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<GsChip, string?>(nameof(Text));

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// The wide banner for a game (design system → HeroBanner): Steam's library hero behind, the game's logo or title, the
/// playtime chip, the save status and a sentence about the save, Manage saves, Game settings and the primary Play.
/// Its text sits on the art, so it uses <c>on-art</c> over the <c>art-scrim</c> fade in every theme.
/// </summary>
public class GsHeroBanner : TemplatedControl
{
    public static readonly StyledProperty<IImage?> ArtProperty = AvaloniaProperty.Register<GsHeroBanner, IImage?>(nameof(Art));

    public static readonly StyledProperty<IImage?> LogoProperty = AvaloniaProperty.Register<GsHeroBanner, IImage?>(nameof(Logo));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Title));

    public static readonly StyledProperty<string?> EyebrowProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Eyebrow));

    public static readonly StyledProperty<string?> ChipProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Chip));

    public static readonly StyledProperty<string?> BlurbProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(Blurb));

    public static readonly StyledProperty<GameStatus?> StatusProperty = AvaloniaProperty.Register<GsHeroBanner, GameStatus?>(nameof(Status));

    public static readonly StyledProperty<string?> StatusLabelProperty = AvaloniaProperty.Register<GsHeroBanner, string?>(nameof(StatusLabel));

    public static readonly StyledProperty<string> PlayLabelProperty = AvaloniaProperty.Register<GsHeroBanner, string>(nameof(PlayLabel), "Continue playing");

    public static readonly StyledProperty<ICommand?> PlayCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(PlayCommand));

    public static readonly StyledProperty<ICommand?> SavesCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(SavesCommand));

    public static readonly StyledProperty<ICommand?> SettingsCommandProperty = AvaloniaProperty.Register<GsHeroBanner, ICommand?>(nameof(SettingsCommand));

    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public IImage? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? Chip
    {
        get => GetValue(ChipProperty);
        set => SetValue(ChipProperty, value);
    }

    public string? Blurb
    {
        get => GetValue(BlurbProperty);
        set => SetValue(BlurbProperty, value);
    }

    public GameStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? StatusLabel
    {
        get => GetValue(StatusLabelProperty);
        set => SetValue(StatusLabelProperty, value);
    }

    public string PlayLabel
    {
        get => GetValue(PlayLabelProperty);
        set => SetValue(PlayLabelProperty, value);
    }

    public ICommand? PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ICommand? SavesCommand
    {
        get => GetValue(SavesCommandProperty);
        set => SetValue(SavesCommandProperty, value);
    }

    public ICommand? SettingsCommand
    {
        get => GetValue(SettingsCommandProperty);
        set => SetValue(SettingsCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LogoProperty)
        {
            PseudoClasses.Set(":logo", Logo is not null);
        }
        else if (change.Property == ArtProperty)
        {
            PseudoClasses.Set(":art", Art is not null);
        }
        else if (change.Property == TitleProperty)
        {
            Avalonia.Automation.AutomationProperties.SetName(this, Title);
        }
    }
}

/// <summary>
/// The play-time calendar (design system → ActivityGrid): a cell a day, Monday first. Level 0 none, 1 under 2 hours
/// (hatched: some, but not much), 2 two to four hours (<c>secondary</c>), 3 over four (<c>primary</c>).
/// </summary>
public class GsActivityGrid : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>> DaysProperty = AvaloniaProperty.Register<GsActivityGrid, IReadOnlyList<int>>(nameof(Days), []);

    public static readonly StyledProperty<int> StartWeekdayProperty = AvaloniaProperty.Register<GsActivityGrid, int>(nameof(StartWeekday));

    public static readonly StyledProperty<double> CellRatioProperty = AvaloniaProperty.Register<GsActivityGrid, double>(nameof(CellRatio), 1 / 1.45);

    private const double Gap = 6;

    static GsActivityGrid()
    {
        AffectsRender<GsActivityGrid>(DaysProperty, StartWeekdayProperty);
        AffectsMeasure<GsActivityGrid>(DaysProperty, StartWeekdayProperty, CellRatioProperty);
    }

    /// <summary>Each day's level, 0 to 3, from the first of the month.</summary>
    public IReadOnlyList<int> Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    /// <summary>The weekday of the first day, 0 for Monday.</summary>
    public int StartWeekday
    {
        get => GetValue(StartWeekdayProperty);
        set => SetValue(StartWeekdayProperty, value);
    }

    /// <summary>A cell's height over its width.</summary>
    public double CellRatio
    {
        get => GetValue(CellRatioProperty);
        set => SetValue(CellRatioProperty, value);
    }

    private int Rows => (StartWeekday + Days.Count + 6) / 7;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 280 : availableSize.Width;
        var cell = (width - Gap * 6) / 7;
        return new Size(width, Math.Max(0, Rows * cell * CellRatio + (Rows - 1) * Gap));
    }

    public override void Render(DrawingContext context)
    {
        var cellWidth = (Bounds.Width - Gap * 6) / 7;
        var cellHeight = cellWidth * CellRatio;
        for (var i = 0; i < Days.Count; i++)
        {
            var slot = StartWeekday + i;
            var rect = new Rect(slot % 7 * (cellWidth + Gap), slot / 7 * (cellHeight + Gap), cellWidth, cellHeight);
            ActivityLevels.Paint(this, context, new RoundedRect(rect, 10), Days[i]);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DaysProperty)
        {
            // The cells are drawn, so the grid says what they hold (A11Y-03).
            var played = Days.Count(d => d > 0);
            Avalonia.Automation.AutomationProperties.SetName(this, $"Played on {played} of {Days.Count} days, {Days.Count(d => d == 3)} of them over 4 hours");
        }
    }
}

/// <summary>The play-time calendar with its legend above and the weekday names below, as the design system shows it.</summary>
public class GsActivity : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<int>> DaysProperty = AvaloniaProperty.Register<GsActivity, IReadOnlyList<int>>(nameof(Days), []);

    public static readonly StyledProperty<int> StartWeekdayProperty = AvaloniaProperty.Register<GsActivity, int>(nameof(StartWeekday));

    public IReadOnlyList<int> Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public int StartWeekday
    {
        get => GetValue(StartWeekdayProperty);
        set => SetValue(StartWeekdayProperty, value);
    }
}

/// <summary>A day of play for <see cref="GsActivityGrid"/>: 0 none, 1 under 2 hours, 2 two to four, 3 over four.</summary>
public static class ActivityLevels
{
    public static int Of(TimeSpan played) => played <= TimeSpan.Zero ? 0 : played < TimeSpan.FromHours(2) ? 1 : played <= TimeSpan.FromHours(4) ? 2 : 3;

    /// <summary>How a cell of each level is painted: <c>bg-300</c>, hatched <c>bg-400</c>, <c>secondary</c> at 60%, <c>primary</c>.</summary>
    public static void Paint(Control owner, DrawingContext context, RoundedRect cell, int level)
    {
        IBrush? Brush(string key) => owner.TryFindResource(key, owner.ActualThemeVariant, out var value) ? value as IBrush : null;
        var rect = cell.Rect;
        switch (level)
        {
            case 1:
                context.DrawRectangle(Brush("bg-300"), null, cell);
                using (context.PushClip(cell))
                {
                    var pen = new Pen(Brush("bg-400"), 3);
                    for (var x = -rect.Height; x < rect.Width; x += 7)
                    {
                        context.DrawLine(pen, new Point(rect.X + x, rect.Bottom), new Point(rect.X + x + rect.Height, rect.Y));
                    }
                }

                break;
            case 2:
                using (context.PushOpacity(0.6))
                {
                    context.DrawRectangle(Brush("secondary"), null, cell);
                }

                break;
            case 3:
                context.DrawRectangle(Brush("primary"), null, cell);
                break;
            default:
                context.DrawRectangle(Brush("bg-300"), null, cell);
                break;
        }
    }
}

/// <summary>A legend dot for one activity level.</summary>
public sealed class GsLevelDot : Control
{
    public static readonly StyledProperty<int> LevelProperty = AvaloniaProperty.Register<GsLevelDot, int>(nameof(Level));

    static GsLevelDot() => AffectsRender<GsLevelDot>(LevelProperty);

    public int Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(12, 12);

    public override void Render(DrawingContext context) => ActivityLevels.Paint(this, context, new RoundedRect(new Rect(0, 0, 12, 12), 6), Level);
}

/// <summary>
/// Cover art filling its space the way CSS <c>background-size: cover</c> does: scaled to cover, centred, cut to fit.
/// It never sets the size of what holds it, so a banner's height comes from its content.
/// </summary>
public sealed class GsCoverImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<GsCoverImage, IImage?>(nameof(Source));

    static GsCoverImage() => AffectsRender<GsCoverImage>(SourceProperty);

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => default;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } image || image.Size.Width <= 0 || image.Size.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(Bounds.Width / image.Size.Width, Bounds.Height / image.Size.Height);
        var shown = new Size(Bounds.Width / scale, Bounds.Height / scale);
        var source = new Rect((image.Size.Width - shown.Width) / 2, (image.Size.Height - shown.Height) / 2, shown.Width, shown.Height);
        context.DrawImage(image, source, new Rect(Bounds.Size));
    }
}

/// <summary>Lays out children in fixed columns with a gap, wrapping to rows, for tiles in a card or a library grid.</summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<ColumnsPanel, int>(nameof(Columns), 3);

    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(Gap), 12);

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(ColumnsProperty, GapProperty);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private double CellWidth(double width) => (width - Gap * (Columns - 1)) / Columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Columns * 160 + (Columns - 1) * Gap : availableSize.Width;
        var cell = CellWidth(width);
        double height = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (i % Columns == Columns - 1 || i == Children.Count - 1)
            {
                height += row + (height > 0 ? Gap : 0);
                row = 0;
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = CellWidth(finalSize.Width);
        double y = 0, row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % Columns;
            Children[i].Arrange(new Rect(column * (cell + Gap), y, cell, Children[i].DesiredSize.Height));
            row = Math.Max(row, Children[i].DesiredSize.Height);
            if (column == Columns - 1)
            {
                y += row + Gap;
                row = 0;
            }
        }

        return finalSize;
    }
}
