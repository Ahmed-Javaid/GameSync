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
}
