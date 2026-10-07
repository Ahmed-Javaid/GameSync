using Microsoft.Win32;

namespace GameSync.Windows;

/// <summary>
/// ACH-09: which game Steam says is running, from its own value in the person's registry
/// (<c>HKCU\Software\Valve\Steam\RunningAppID</c>), however the game was started. Read only.
/// </summary>
public static class SteamRunning
{
    /// <summary>The running game's Steam app ID; null when none runs, or Steam isn't here.</summary>
    public static long? AppId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("RunningAppID") is int id and > 0 ? id : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
