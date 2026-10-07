using Microsoft.Win32;

namespace GameSync.Windows;

/// <summary>Windows' own look settings that GameSync follows.</summary>
public static class WindowsLook
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Settings → Personalization → Colors → Transparency effects. With them off, GameSync's Glossy surfaces go Solid
    /// (LOOK-18). On when Windows doesn't say, as it's on by default.
    /// </summary>
    public static bool TransparencyOn()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(Personalize);
        return personalize?.GetValue("EnableTransparency") is not int on || on != 0;
    }

    /// <summary>
    /// Settings → Accessibility → Visual effects → Animation effects (A11Y-04). With them off, nothing in GameSync moves:
    /// a game's colours change at once (KAN-54). On when Windows doesn't say, as it's on by default.
    /// </summary>
    public static bool AnimationsOn() => !SystemParametersInfo(GetClientAreaAnimation, 0, out var on, 0) || on != 0;

    private const uint GetClientAreaAnimation = 0x1042;

    /// <summary>
    /// Settings → Accessibility → Text size (A11Y-04): how much bigger Windows makes text, 1 by default and up to 2.25.
    /// </summary>
    public static double TextScale()
    {
        using var accessibility = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
        return accessibility?.GetValue("TextScaleFactor") is int percent and >= 100 and <= 225 ? percent / 100.0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint update);
}
