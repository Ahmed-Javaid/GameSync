using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameSync.Windows;

/// <summary>
/// Windows lets only the program the person is using bring a window to the front. A second start of GameSync is that
/// program for a moment, so it passes the right on to the running app before asking it to show its window (BG-01).
/// </summary>
public static class Foreground
{
    public static void LetPipeServerComeForward(SafePipeHandle pipe)
    {
        try
        {
            if (GetNamedPipeServerProcessId(pipe, out var process))
            {
                AllowSetForegroundWindow(process);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
