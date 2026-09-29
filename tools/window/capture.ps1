param([int]$ProcessId, [string]$Out)

# Lists a process's windows, and with -Out saves its visible "GameSync" window to a PNG with PrintWindow, as it draws
# itself, whatever covers it on screen.

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

public static class Capture
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<string> Windows(uint pid)
    {
        var found = new List<string>();
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid)
            {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                RECT r; GetWindowRect(h, out r);
                found.Add(string.Format("{0} visible={1} title='{2}' rect={3},{4} {5}x{6}", h, IsWindowVisible(h), sb, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Shot(uint pid, string path)
    {
        IntPtr target = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (p == pid && IsWindowVisible(h) && sb.ToString() == "GameSync") { target = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) return "no visible GameSync window";
        RECT r; GetWindowRect(target, out r);
        using (var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(target, hdc, 2);
            g.ReleaseHdc(hdc);
            bmp.Save(path, ImageFormat.Png);
        }
        return path;
    }
}
"@

[Capture]::Windows([uint32]$ProcessId) | ForEach-Object { $_ }
if ($Out) { [Capture]::Shot([uint32]$ProcessId, $Out) }
