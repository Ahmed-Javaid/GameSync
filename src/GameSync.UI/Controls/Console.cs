using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GameSync.UI.Controls;

/// <summary>
/// A Console pane (design system → ConsoleTable): <c>bg-000</c> set into the app like a terminal, a bar with the prompt
/// and command on the left and a toolbar on the right, then the table. Tables put their header and rows in the content,
/// sharing column widths through <c>SharedSizeGroup</c>.
/// </summary>
public class GsConsole : ContentControl
{
    public static readonly StyledProperty<string?> CommandProperty = AvaloniaProperty.Register<GsConsole, string?>(nameof(Command));

    public static readonly StyledProperty<object?> ToolbarProperty = AvaloniaProperty.Register<GsConsole, object?>(nameof(Toolbar));

    public string? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? Toolbar
    {
        get => GetValue(ToolbarProperty);
        set => SetValue(ToolbarProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty || change.Property == ToolbarProperty)
        {
            PseudoClasses.Set(":bar", Command is not null || Toolbar is not null);
        }
    }
}

/// <summary>The rows of a Console table: 36px rows with a hairline between, <c>bg-200</c> on hover and <c>secondary-soft</c> when selected.</summary>
public class GsConsoleRows : ListBox
{
}

public enum LogLevel
{
    Info,
    Ok,
    Warn,
    Error,
}

/// <summary>One line of a log: the time, a tag coloured by level, and one plain sentence.</summary>
public sealed record LogLine(string Time, LogLevel Level, string Tag, string Message)
{
    public string TagText => Tag.ToUpperInvariant();
}

/// <summary>A timestamped log in mono (design system → ConsoleLog); error lines get a <c>danger-soft</c> row, and <see cref="Live"/> shows the prompt's cursor.</summary>
public class GsConsoleLog : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<LogLine>> LinesProperty = AvaloniaProperty.Register<GsConsoleLog, IReadOnlyList<LogLine>>(nameof(Lines), []);

    public static readonly StyledProperty<bool> LiveProperty = AvaloniaProperty.Register<GsConsoleLog, bool>(nameof(Live));

    public static readonly StyledProperty<double> TimeWidthProperty = AvaloniaProperty.Register<GsConsoleLog, double>(nameof(TimeWidth), 76);

    public IReadOnlyList<LogLine> Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public bool Live
    {
        get => GetValue(LiveProperty);
        set => SetValue(LiveProperty, value);
    }

    /// <summary>76px fits a time of day; a date and time takes about 92.</summary>
    public double TimeWidth
    {
        get => GetValue(TimeWidthProperty);
        set => SetValue(TimeWidthProperty, value);
    }
}

/// <summary>A log line's row: pseudo-classes for its level, so errors can tint their row.</summary>
public class GsLogLineView : TemplatedControl
{
    public static readonly StyledProperty<LogLine?> LineProperty = AvaloniaProperty.Register<GsLogLineView, LogLine?>(nameof(Line));

    public static readonly StyledProperty<double> TimeWidthProperty = AvaloniaProperty.Register<GsLogLineView, double>(nameof(TimeWidth), 76);

    public LogLine? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public double TimeWidth
    {
        get => GetValue(TimeWidthProperty);
        set => SetValue(TimeWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty)
        {
            foreach (var level in Enum.GetValues<LogLevel>())
            {
                PseudoClasses.Set(":" + level.ToString().ToLowerInvariant(), Line?.Level == level);
            }
        }
    }
}
