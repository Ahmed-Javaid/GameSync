using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>BG-06: whether a notification now would get in the way: a fullscreen game or app, a presentation, or nobody there.</summary>
public static class FullScreen
{
    private const int NotPresent = 1;
    private const int Busy = 2;
    private const int Direct3DFullScreen = 3;
    private const int PresentationMode = 4;
    private const int StoreAppFullScreen = 7;

    public static bool IsBusy() =>
        SHQueryUserNotificationState(out var state) == 0 && state is NotPresent or Busy or Direct3DFullScreen or PresentationMode or StoreAppFullScreen;

    /// <summary>
    /// ACH-09: a game running in exclusive fullscreen, which no window can show over (the achievement popup plays only its
    /// sound then). A game in a window or borderless isn't.
    /// </summary>
    public static bool IsExclusive() => SHQueryUserNotificationState(out var state) == 0 && state == Direct3DFullScreen;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
