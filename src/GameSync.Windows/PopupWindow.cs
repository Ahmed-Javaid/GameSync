using System.Runtime.InteropServices;

namespace GameSync.Windows;

/// <summary>
/// ACH-09: what the achievement popup needs from Windows. It's GameSync's own small window on top of the game: one that
/// never takes focus, a click or a key (so the game carries on), and never touches the game itself; and it goes on the
/// screen the person is looking at, in the corner they picked.
/// </summary>
public static class PopupWindow
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExTopmost = 0x8;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x8000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpFrameChanged = 0x20;
    private const uint MonitorDefaultToPrimary = 1;

    /// <summary>Makes a window click-through, never activated, kept off the taskbar and Alt+Tab, and on top.</summary>
    public static void MakeClickThrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExTopmost | WsExLayered | WsExNoActivate));
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// Cuts a window to a rounded rectangle (physical pixels), for when it can't be see-through: its square corners would
    /// show round the popup's rounded card.
    /// </summary>
    public static void RoundCorners(IntPtr hwnd, int left, int top, int right, int bottom, int radius)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var region = CreateRoundRectRgn(left, top, right + 1, bottom + 1, radius * 2, radius * 2);
            if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0)
            {
                DeleteObject(region);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// The screen the person is using, in physical pixels: the one with the window in front (the game). A game covering
    /// its screen gets the whole screen; anything else the screen less the taskbar.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? ForegroundScreen()
    {
        try
        {
            var front = GetForegroundWindow();
            var monitor = MonitorFromWindow(front, MonitorDefaultToPrimary);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                return null;
            }

            var whole = info.Monitor;
            var covers = front != IntPtr.Zero && GetWindowRect(front, out var rect) &&
                rect.Left <= whole.Left && rect.Top <= whole.Top && rect.Right >= whole.Right && rect.Bottom >= whole.Bottom;
            var area = covers ? whole : info.Work;
            return (area.Left, area.Top, area.Right, area.Bottom);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
