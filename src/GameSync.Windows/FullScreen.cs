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

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
