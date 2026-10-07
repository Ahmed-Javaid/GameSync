using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GameSync.Windows;

/// <summary>
/// R17: GameSync's app never runs as an administrator. Started that way (Run as administrator, or an installer finishing
/// as admin), it says so in Windows' own message box and closes, so nothing it does, or starts, runs elevated.
/// </summary>
public static class Elevation
{
    /// <summary>Whether this process runs elevated, as an administrator.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>What the app says when it was started as an administrator.</summary>
    public const string Refusal = "GameSync doesn't run as an administrator, so it didn't start. Start it the usual way, from the Start menu or its shortcut.";

    /// <summary>Windows' own message box, with an OK button and the information mark, before any window of GameSync's exists.</summary>
    public static void Tell(string title, string text) => MessageBox(IntPtr.Zero, text, title, OkWithInformation);

    private const uint OkWithInformation = 0x00000040;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
