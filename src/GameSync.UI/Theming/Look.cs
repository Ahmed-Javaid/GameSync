using GameSync.Core.State;

namespace GameSync.UI.Theming;

/// <summary>
/// The person's appearance choices on this PC (LOOK-01 to LOOK-07, LOOK-10), kept in state.db and never synced, so
/// each PC keeps its own look. The mode can be Dark, Light or Match Windows, which follows Windows' app mode.
/// </summary>
public sealed record Look(string Mode = Look.Dark, bool PureBlack = false, string Preset = "arcade", string? Primary = null, string? Secondary = null)
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string MatchWindows = "windows";

    private const string Prefix = "look.";

    public static Look Load(StateStore state)
    {
        var mode = state.GetSetting(Prefix + "mode");
        var preset = state.GetSetting(Prefix + "preset");
        return new Look(
            mode is Light or MatchWindows ? mode : Dark,
            state.GetSetting(Prefix + "black") is { Length: > 0 },
            preset is { Length: > 0 } && ThemeEngine.Presets.Any(p => p.Id == preset) ? preset : "arcade",
            Nonempty(state.GetSetting(Prefix + "primary")),
            Nonempty(state.GetSetting(Prefix + "secondary")));
    }

    public void Save(StateStore state)
    {
        state.SetSetting(Prefix + "mode", Mode);
        state.SetSetting(Prefix + "black", PureBlack ? "1" : "");
        state.SetSetting(Prefix + "preset", Preset);
        state.SetSetting(Prefix + "primary", Primary ?? "");
        state.SetSetting(Prefix + "secondary", Secondary ?? "");
    }

    /// <summary>The theme to build now, given Windows' light or dark app mode and its accent colour (<c>#rrggbb</c>).</summary>
    public ThemeChoice Resolve(bool windowsLight, string? windowsAccent) => new(
        Preset,
        Mode == Light || (Mode == MatchWindows && windowsLight) ? ThemeMode.Light : ThemeMode.Dark,
        PureBlack,
        Primary,
        Secondary,
        windowsAccent);

    private static string? Nonempty(string? value) => value is { Length: > 0 } ? value : null;
}
