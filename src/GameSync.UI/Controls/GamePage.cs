using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using GameSync.Core.State;

namespace GameSync.UI.Controls;

/// <summary>A fact on a game's play bar: LAST PLAYED over "25 Sep"; for SAVES, its status as a plain badge.</summary>
/// <param name="IsStatus">The value is a status, shown with its icon and colour.</param>
public sealed record PlayStat(string Label, string Value, bool IsStatus = false, GameStatus? Status = null)
{
    public bool IsText => !IsStatus;

    public override string ToString() => $"{Label}: {Value}";
}

/// <summary>
/// The bar under a game's hero, like Steam's (design system → PlayBar): Play, or the status's own action when the game
/// needs you; then the facts a player looks for (Last played, Play time, Achievements, Saves); then the game's own
/// buttons (favourite, Properties, More).
/// </summary>
public class GsPlayBar : TemplatedControl
{
    public static readonly StyledProperty<object?> PrimaryProperty = AvaloniaProperty.Register<GsPlayBar, object?>(nameof(Primary));

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<GsPlayBar, object?>(nameof(Actions));

    public static readonly StyledProperty<IEnumerable?> StatsProperty = AvaloniaProperty.Register<GsPlayBar, IEnumerable?>(nameof(Stats));

    public object? Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public IEnumerable? Stats
    {
        get => GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
    }
}

/// <summary>A labelled fact (design system → Facts): DEVELOPER beside "Hopoo Games"; a path or an option in mono; genres as tags.</summary>
public sealed record Fact(string Label, string? Value, bool Mono = false, IReadOnlyList<string>? Tags = null)
{
    public bool HasTags => Tags is { Count: > 0 };

    public bool HasText => !HasTags;

    /// <summary>Words that wrap: a developer, a date.</summary>
    public bool IsPlain => HasText && !Mono;

    /// <summary>A path or an option: one line in mono, trimmed from the front so the end (the game's own folder) shows.</summary>
    public bool IsMono => HasText && Mono;

    public override string ToString() => HasTags ? $"{Label}: {string.Join(", ", Tags!)}" : $"{Label}: {Value}";

    /// <summary>The facts that have something to say: a game shows only what's known, never "Unknown".</summary>
    public static IReadOnlyList<Fact> Known(params Fact?[] facts) =>
        facts.OfType<Fact>().Where(f => f.HasTags || !string.IsNullOrWhiteSpace(f.Value)).ToList();
}

/// <summary>Short labelled facts, their labels in one column: a game's About, its Saves card, On this PC, its Installed files.</summary>
public class GsFacts : ItemsControl
{
}
