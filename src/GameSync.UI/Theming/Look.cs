using GameSync.Core.State;

namespace GameSync.UI.Theming;

/// <summary>
/// The person's appearance choices on this PC (LOOK-01 to LOOK-07, LOOK-10, LOOK-17), kept in state.db and never synced,
/// so each PC keeps its own look. The mode can be Dark, Light or Match Windows, which follows Windows' app mode; the
/// surface is Glossy, the default, or Solid.
/// </summary>
public sealed record Look(
    string Mode = Look.Dark,
    bool PureBlack = false,
    string Preset = "arcade",
    string? Primary = null,
    string? Secondary = null,
    string Surface = Look.Glossy)
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string MatchWindows = "windows";
    public const string Glossy = "glossy";
    public const string Solid = "solid";

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
            Nonempty(state.GetSetting(Prefix + "secondary")),
            state.GetSetting(Prefix + "surface") == Solid ? Solid : Glossy);
    }

    public void Save(StateStore state)
    {
        state.SetSetting(Prefix + "mode", Mode);
        state.SetSetting(Prefix + "black", PureBlack ? "1" : "");
        state.SetSetting(Prefix + "preset", Preset);
        state.SetSetting(Prefix + "primary", Primary ?? "");
        state.SetSetting(Prefix + "secondary", Secondary ?? "");
        state.SetSetting(Prefix + "surface", Surface);
    }

    /// <summary>The theme to build now, given Windows' light or dark app mode and its accent colour (<c>#rrggbb</c>).</summary>
    public ThemeChoice Resolve(bool windowsLight, string? windowsAccent) => new(
        Preset,
        Mode == Light || (Mode == MatchWindows && windowsLight) ? ThemeMode.Light : ThemeMode.Dark,
        PureBlack,
        Primary,
        Secondary,
        windowsAccent);

    /// <summary>
    /// LOOK-17, LOOK-18: whether pages show Glossy now. It needs the person's choice, no pure black (a dark mode choice),
    /// and Windows' transparency effects on; otherwise the app is Solid. Light mode has its own Glossy since design system
    /// version 35 (the owner, 3 Oct 2026).
    /// </summary>
    public bool ShowsGlossy(ThemeChoice resolved, bool transparencyOn) =>
        Surface == Glossy && (resolved.Mode == ThemeMode.Light || !resolved.PureBlack) && transparencyOn;

    private static string? Nonempty(string? value) => value is { Length: > 0 } ? value : null;
}
