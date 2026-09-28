using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GameSync.Windows;

/// <summary>An item of the tray icon's menu; a null text is a separator, and the default item is drawn bold.</summary>
public sealed record TrayMenuItem(string? Text, string Id = "", bool IsDefault = false, bool Enabled = true)
{
    public static TrayMenuItem Separator { get; } = new(Text: null);
}

/// <summary>
/// The notification-area icon (BG-07) through Windows' own Shell_NotifyIcon: the icon and its hover text, a click that
/// opens GameSync, and Windows' own menu, which follows Windows' light or dark mode as Windows' menus do (LOOK-11). It
/// belongs to the thread that makes it, which must run a message loop (the app's UI thread), and comes back by itself
/// when Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int IconId = 1;
    private const uint CallbackMessage = 0x0400 + 1;
    private const int SelectEvent = 0x0400;
    private const int KeySelectEvent = 0x0401;
    private const int ContextMenuEvent = 0x007B;
    private const uint SettingChange = 0x001A;
    private const uint DisplayChange = 0x007E;
    private const uint DpiChanged = 0x02E0;
    private const uint QueryEndSession = 0x0011;
    private const uint EndSession = 0x0016;
    private const int AddIcon = 0;
    private const int ModifyIcon = 1;
    private const int DeleteIcon = 2;
    private const int SetVersion = 4;
    private const int MessageFlag = 0x01;
    private const int IconFlag = 0x02;
    private const int TipFlag = 0x04;
    private const int ShowTipFlag = 0x80;
    private const int Version4 = 4;
    private const int TipLength = 128;

    private readonly WindowProc _proc;
    private readonly string _class = $"GameSync.Tray.{Environment.ProcessId}";
    private readonly IntPtr _window;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tip = "GameSync";
    private bool _added;

    public TrayIcon()
    {
        _proc = Proc;
        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = Marshal.SizeOf<WindowClass>(),
            Proc = Marshal.GetFunctionPointerForDelegate(_proc),
            Instance = instance,
            ClassName = _class,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"Windows didn't make the tray icon's window class (error {Marshal.GetLastWin32Error()}).");
        }

        // A hidden top-level window, not a message-only one: only a top-level window hears Windows' theme changes and can own the menu.
        _window = CreateWindowExW(0, _class, "GameSync", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Windows didn't make the tray icon's window (error {Marshal.GetLastWin32Error()}).");
        }

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        DarkMenus(_window);
    }

    /// <summary>The icon was clicked, or picked with the keyboard.</summary>
    public event Action? Opened;

    /// <summary>A menu item was picked, with its id.</summary>
    public event Action<string>? Picked;

    /// <summary>The taskbar turned light or dark, or the screen's scale changed: the icon should be drawn again.</summary>
    public event Action? LookChanged;

    /// <summary>What goes wrong inside a Windows callback, which mustn't throw back into Windows.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Windows is signing the person out or shutting down; the program may be ended as soon as this returns.</summary>
    public event Action? SessionEnding;

    /// <summary>The menu, made each time it opens.</summary>
    public Func<IReadOnlyList<TrayMenuItem>> Menu { get; set; } = () => [];

    /// <summary>Windows took the icon; false until the taskbar is ready, as early in a sign-in.</summary>
    public bool IsShown => _added;

    /// <summary>The icon's size in pixels at the taskbar's current scale.</summary>
    public int IconSize
    {
        get
        {
            try
            {
                var dpi = GetDpiForWindow(_window);
                return GetSystemMetricsForDpi(SmallIconWidth, dpi == 0 ? 96 : dpi);
            }
            catch (EntryPointNotFoundException)
            {
                return GetSystemMetrics(SmallIconWidth);
            }
        }
    }

    /// <summary>Whether the taskbar is light (Windows' "default Windows mode"); dark when Windows doesn't say.</summary>
    public static bool TaskbarIsLight()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return personalize?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    /// <summary>Shows the icon, or changes it: a square picture of <paramref name="size"/> pixels as BGRA, top row first, and the hover text.</summary>
    public void Set(int size, byte[] bgra, string tooltip)
    {
        var icon = MakeIcon(size, bgra);
        var old = _icon;
        _icon = icon;
        _tip = tooltip.Length < TipLength ? tooltip : tooltip[..(TipLength - 2)] + "…";
        Send(_added ? ModifyIcon : AddIcon);
        if (old != IntPtr.Zero)
        {
            DestroyIcon(old);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIconW(DeleteIcon, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        DestroyWindow(_window);
        UnregisterClassW(_class, GetModuleHandleW(null));
    }

    private void Send(int message)
    {
        var data = Data(MessageFlag | IconFlag | TipFlag | ShowTipFlag);
        if (message == AddIcon)
        {
            // Explorer may not be up yet at sign-in; TaskbarCreated brings the icon back then.
            _added = Shell_NotifyIconW(AddIcon, ref data);
            if (_added)
            {
                data.TimeoutOrVersion = Version4;
                Shell_NotifyIconW(SetVersion, ref data);
            }
        }
        else if (!Shell_NotifyIconW(ModifyIcon, ref data))
        {
            _added = false;
            Send(AddIcon);
        }
    }

    private NotifyIconData Data(int flags) => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = IconId,
        Flags = flags,
        CallbackMessage = (int)CallbackMessage,
        Icon = _icon,
        Tip = _tip,
        Info = "",
        InfoTitle = "",
    };

    private IntPtr Proc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == CallbackMessage)
            {
                switch ((int)(lParam.ToInt64() & 0xFFFF))
                {
                    case SelectEvent or KeySelectEvent:
                        Opened?.Invoke();
                        break;
                    case ContextMenuEvent:
                        var at = wParam.ToInt64();
                        ShowMenu((short)(at & 0xFFFF), (short)((at >> 16) & 0xFFFF));
                        break;
                }

                return IntPtr.Zero;
            }

            if (message == _taskbarCreated && _taskbarCreated != 0)
            {
                _added = false;
                if (_icon != IntPtr.Zero)
                {
                    Send(AddIcon);
                }

                LookChanged?.Invoke();
                return IntPtr.Zero;
            }

            if ((message == SettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") ||
                message is DisplayChange or DpiChanged)
            {
                LookChanged?.Invoke();
            }

            if (message == QueryEndSession)
            {
                return 1;
            }

            if (message == EndSession && wParam != IntPtr.Zero)
            {
                SessionEnding?.Invoke();
                return IntPtr.Zero;
            }
        }
        catch (Exception e)
        {
            Failed?.Invoke(e);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        var items = Menu();
        if (items.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenuW(menu, SeparatorItem, UIntPtr.Zero, null);
                    continue;
                }

                AppendMenuW(menu, item.Enabled ? StringItem : StringItem | GrayedItem, (UIntPtr)(uint)(i + 1), item.Text);
                if (item.IsDefault)
                {
                    SetMenuDefaultItem(menu, (uint)(i + 1), 0);
                }
            }

            // Windows' own sample: the menu's window comes forward first, or the menu doesn't close when you click away.
            SetForegroundWindow(_window);
            var align = GetSystemMetrics(MenuDropAlignment) != 0 ? RightAlign : LeftAlign;
            var picked = TrackPopupMenuEx(menu, RightButton | ReturnCommand | align, x, y, _window, IntPtr.Zero);
            PostMessageW(_window, 0, IntPtr.Zero, IntPtr.Zero);
            if (picked > 0 && picked <= items.Count)
            {
                Picked?.Invoke(items[picked - 1].Id);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>A 32-bit icon with its alpha, from BGRA pixels.</summary>
    private static IntPtr MakeIcon(int size, byte[] bgra)
    {
        if (bgra.Length != size * size * 4)
        {
            throw new ArgumentException($"A {size}×{size} icon needs {size * size * 4} bytes, not {bgra.Length}.", nameof(bgra));
        }

        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = size, Height = -size, Planes = 1, BitCount = 32 };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        var mask = CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
        try
        {
            Marshal.Copy(bgra, 0, bits, bgra.Length);
            var info = new IconInfo { IsIcon = true, Mask = mask, Color = color };
            var icon = CreateIconIndirect(ref info);
            return icon != IntPtr.Zero ? icon : throw new InvalidOperationException($"Windows didn't make the tray icon (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    /// <summary>Windows 10 1903 and later: this app's menus follow Windows' dark mode. Undocumented, so a failure just leaves them light.</summary>
    private static void DarkMenus(IntPtr window)
    {
        if (Environment.OSVersion.Version.Build < 18362)
        {
            return;
        }

        try
        {
            SetPreferredAppMode(AllowDark);
            AllowDarkModeForWindow(window, true);
            FlushMenuThemes();
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    private const int SmallIconWidth = 49;
    private const int MenuDropAlignment = 40;
    private const uint StringItem = 0x0000;
    private const uint GrayedItem = 0x0001;
    private const uint SeparatorItem = 0x0800;
    private const uint LeftAlign = 0x0000;
    private const uint RightAlign = 0x0008;
    private const uint RightButton = 0x0002;
    private const uint ReturnCommand = 0x0100;
    private const int AllowDark = 1;

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public int Size;
        public int Style;
        public IntPtr Proc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipLength)]
        public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowDarkModeForWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
