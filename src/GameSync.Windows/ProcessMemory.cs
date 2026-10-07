using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>
/// PERF-01, KAN-62: while GameSync waits in the tray with no window, the memory pages its window used are handed back to
/// Windows rather than kept until Windows runs short. They go to Windows' standby list, so a window opened again soon
/// gets them back at once.
/// </summary>
public static class ProcessMemory
{
    public static void Trim() => SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
