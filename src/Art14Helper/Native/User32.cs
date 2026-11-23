using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Rectangle as Win32 passes it: edges, not size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left, Top, Right, Bottom;

    internal readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);

    internal static Rect From(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
}

/// <summary>Window messages the app handles.</summary>
internal static class WindowMessage
{
    internal const uint Create = 0x0001, Destroy = 0x0002, Size = 0x0005, Paint = 0x000F, Close = 0x0010,
        QueryEndSession = 0x0011, EraseBackground = 0x0014, EndSession = 0x0016, SettingChange = 0x001A,
        SetCursor = 0x0020, GetMinMaxInfo = 0x0024, DisplayChange = 0x007E, NcDestroy = 0x0082,
        KeyDown = 0x0100, Timer = 0x0113, MenuSelect = 0x011F,
        MouseMove = 0x0200, LeftDown = 0x0201, LeftUp = 0x0202, RightUp = 0x0205, MouseWheel = 0x020A,
        CaptureChanged = 0x0215, PowerBroadcast = 0x0218, DeviceChange = 0x0219, MouseLeave = 0x02A3, DpiChanged = 0x02E0,
        ThemeChanged = 0x031A, App = 0x8000;
}

/// <summary>Windows, the message loop, timers and the mouse (user32).</summary>
internal static unsafe partial class User32
{
    internal const uint Popup = 0x80000000, Caption = 0x00C00000, SystemMenu = 0x00080000, ThickFrame = 0x00040000,
        MinimizeBox = 0x00020000, MaximizeBox = 0x00010000;   // WS_*
    internal const uint ToolWindow = 0x80, AppWindow = 0x40000;                       // WS_EX_*
    internal const int ShowNormal = 1, ShowNoActivate = 4, Show = 5, Restore = 9, Hide = 0;   // SW_*
    internal const int HitClient = 1;                                                 // HTCLIENT
    internal const int KeyTab = 0x09, KeyEnter = 0x0D, KeySpace = 0x20, KeyEscape = 0x1B, KeyPageUp = 0x21, KeyPageDown = 0x22, KeyEnd = 0x23, KeyHome = 0x24,
        KeyUp = 0x26, KeyDown = 0x28;                                                 // VK_*
    internal const int UseDefault = unchecked((int)0x80000000);                       // CW_USEDEFAULT
    internal static readonly IntPtr MessageOnly = -3;                                // HWND_MESSAGE

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, nint, nint, nint> Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueuedMessage
    {
        public IntPtr Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public Point Cursor;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintInfo
    {
        public IntPtr Dc;
        public int Erase;
        public Rect Area;
        public int Restore, IncludeUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SizeLimits
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LeaveTracking
    {
        public uint Size, Flags;
        public IntPtr Window;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    internal static IntPtr Module => GetModuleHandleW(null);

    /// <summary>Asks for one WM_MOUSELEAVE when the pointer leaves the window.</summary>
    internal static void TrackLeave(IntPtr window)
    {
        var tracking = new LeaveTracking { Size = (uint)sizeof(LeaveTracking), Flags = 0x2, Window = window };   // TME_LEAVE
        TrackMouseEvent(&tracking);
    }

    internal static Point CursorPosition
    {
        get
        {
            Point point;
            return GetCursorPos(&point) != 0 ? point : Point.Empty;
        }
    }

    internal static Point ToClient(IntPtr window, Point screen)
    {
        ScreenToClient(window, &screen);
        return screen;
    }

    internal static Rectangle ClientArea(IntPtr window)
    {
        Rect area;
        return GetClientRect(window, &area) != 0 ? area.ToRectangle() : Rectangle.Empty;
    }

    /// <summary>The whole window, frame included, on screen.</summary>
    internal static Rectangle WindowBounds(IntPtr window)
    {
        Rect area;
        return GetWindowRect(window, &area) != 0 ? area.ToRectangle() : Rectangle.Empty;
    }

    /// <summary>The free area (without the taskbar) and the scale of the monitor under a screen point.</summary>
    internal static (Rectangle WorkArea, float Scale) MonitorAt(Point screen)
    {
        var monitor = MonitorFromPoint((uint)screen.X | (long)screen.Y << 32, 2);   // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        var area = GetMonitorInfoW(monitor, &info) != 0 ? info.Work.ToRectangle() : new Rectangle(0, 0, 1280, 800);
        return (area, GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96f : 1f);   // MDT_EFFECTIVE_DPI
    }

    internal static void Invalidate(IntPtr window) => InvalidateRect(window, null, 0);

    internal static void Move(IntPtr window, Rectangle bounds)
        => SetWindowPos(window, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014);   // SWP_NOZORDER | SWP_NOACTIVATE

    internal static void SetCursor(bool hand) => SetCursorTo(LoadCursorW(IntPtr.Zero, hand ? 32649 : 32512));   // IDC_HAND, IDC_ARROW

    internal static IntPtr ArrowCursor => LoadCursorW(IntPtr.Zero, 32512);

    internal static bool IsShiftDown => GetKeyState(0x10) < 0;   // VK_SHIFT, high bit = down

    /// <summary>Presses and releases one key, as if typed (used for the volume keys).</summary>
    internal static void TapKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, 0);
        keybd_event(virtualKey, 0, 0x2, 0);   // KEYEVENTF_KEYUP
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blend
    {
        public byte Operation, Flags, SourceConstantAlpha, AlphaFormat;
    }

    /// <summary>Gives a layered window a new picture (per-pixel alpha) and position.</summary>
    internal static void PresentLayered(IntPtr window, IntPtr screenDc, Point position, Size size, IntPtr pictureDc, byte opacity = 255)
    {
        var origin = Point.Empty;
        var blend = new Blend { SourceConstantAlpha = opacity, AlphaFormat = 1 };   // AC_SRC_OVER with AC_SRC_ALPHA
        UpdateLayeredWindow(window, screenDc, &position, &size, pictureDc, &origin, 0, &blend, 2);   // ULW_ALPHA
    }

    /// <summary>Changes a layered window's overall opacity, keeping its picture (for fades).</summary>
    internal static void SetLayeredOpacity(IntPtr window, byte opacity)
    {
        var blend = new Blend { SourceConstantAlpha = opacity, AlphaFormat = 1 };
        UpdateLayeredWindow(window, IntPtr.Zero, null, null, IntPtr.Zero, null, 0, &blend, 2);
    }

    [LibraryImport(Libraries.Kernel32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? name);

    [LibraryImport(Libraries.User32)]
    internal static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport(Libraries.User32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateWindowExW(uint exStyle, string className, string? title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [LibraryImport(Libraries.User32)]
    internal static partial nint DefWindowProcW(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32)]
    internal static partial int DestroyWindow(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial int ShowWindow(IntPtr window, int command);

    [LibraryImport(Libraries.User32)]
    internal static partial int IsIconic(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial int SetForegroundWindow(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size, Time;
    }

    /// <summary>How long since the last keyboard or mouse input, in milliseconds.</summary>
    internal static uint IdleMilliseconds
    {
        get
        {
            var info = new LastInput { Size = (uint)sizeof(LastInput) };
            return GetLastInputInfo(&info) != 0 ? (uint)Environment.TickCount - info.Time : 0;
        }
    }

    [LibraryImport(Libraries.User32)]
    private static partial int GetLastInputInfo(LastInput* info);

    /// <summary>The calling thread's active window (never another app's), or zero: the owner for a dialog.</summary>
    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr GetActiveWindow();

    /// <summary>The desktop shell's window (Explorer's), or zero when no shell is running.</summary>
    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr GetShellWindow();

    [LibraryImport(Libraries.User32)]
    internal static partial nint SendMessageW(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32)]
    internal static partial int PostMessageW(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32)]
    internal static partial int GetMessageW(QueuedMessage* message, IntPtr window, uint first, uint last);

    [LibraryImport(Libraries.User32)]
    internal static partial int TranslateMessage(QueuedMessage* message);

    [LibraryImport(Libraries.User32)]
    internal static partial nint DispatchMessageW(QueuedMessage* message);

    [LibraryImport(Libraries.User32)]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport(Libraries.User32)]
    internal static partial nuint SetTimer(IntPtr window, nuint id, uint milliseconds, IntPtr callback);

    [LibraryImport(Libraries.User32)]
    internal static partial int KillTimer(IntPtr window, nuint id);

    [LibraryImport(Libraries.User32)]
    private static partial int InvalidateRect(IntPtr window, Rect* area, int erase);

    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr BeginPaint(IntPtr window, PaintInfo* paint);

    [LibraryImport(Libraries.User32)]
    internal static partial int EndPaint(IntPtr window, PaintInfo* paint);

    [LibraryImport(Libraries.User32)]
    private static partial int GetClientRect(IntPtr window, Rect* area);

    [LibraryImport(Libraries.User32)]
    private static partial int GetWindowRect(IntPtr window, Rect* area);

    [LibraryImport(Libraries.User32)]
    private static partial int SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [LibraryImport(Libraries.User32)]
    internal static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr SetCapture(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial int ReleaseCapture();

    [LibraryImport(Libraries.User32)]
    private static partial int TrackMouseEvent(LeaveTracking* tracking);

    [LibraryImport(Libraries.User32)]
    private static partial short GetKeyState(int key);

    [LibraryImport(Libraries.User32)]
    private static partial int GetCursorPos(Point* point);

    [LibraryImport(Libraries.User32)]
    private static partial int ScreenToClient(IntPtr window, Point* point);

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr LoadCursorW(IntPtr instance, nint id);

    [LibraryImport(Libraries.User32, EntryPoint = "SetCursor")]
    private static partial IntPtr SetCursorTo(IntPtr cursor);

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr MonitorFromPoint(long point, uint flags);

    [LibraryImport(Libraries.User32)]
    private static partial int GetMonitorInfoW(IntPtr monitor, MonitorInfo* info);

    [LibraryImport(Libraries.Shcore)]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string name);

    [LibraryImport(Libraries.User32)]
    private static partial void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    [LibraryImport(Libraries.User32)]
    private static partial int UpdateLayeredWindow(IntPtr window, IntPtr screenDc, Point* position, Size* size,
        IntPtr sourceDc, Point* sourceOrigin, uint colorKey, Blend* blend, uint flags);

    [LibraryImport(Libraries.User32)]
    internal static partial IntPtr GetDC(IntPtr window);

    [LibraryImport(Libraries.User32)]
    internal static partial int ReleaseDC(IntPtr window, IntPtr dc);
}
