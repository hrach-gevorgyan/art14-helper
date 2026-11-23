using System.Runtime.InteropServices;

namespace Art14Helper.Ui;

/// <summary>
/// A standard Windows tooltip that the app positions itself, used for the tray menu's
/// item descriptions (a popup menu has no tooltips of its own).
/// </summary>
internal sealed unsafe partial class TrackingTooltip : IDisposable
{
    private const int MaxWidth = 300;

    private readonly IntPtr _owner;
    private IntPtr _window;
    private bool _registered;

    internal TrackingTooltip(IntPtr owner)
    {
        _owner = owner;
        _window = CreateWindowExW(WsExTopmost, "tooltips_class32", null, WsPopup | TtsNoPrefix | TtsAlwaysTip,
            CwUseDefault, CwUseDefault, CwUseDefault, CwUseDefault, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero)
            return;
        SendMessageW(_window, TtmSetMaxTipWidth, 0, MaxWidth);
        // Above the menu, which is itself topmost.
        SetWindowPos(_window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Shows <paramref name="text"/> with its top-left corner at a screen point.</summary>
    internal void Show(string text, int x, int y)
    {
        if (_window == IntPtr.Zero)
            return;
        fixed (char* chars = text)
        {
            // The control keeps its own copy of the text.
            var tool = Tool(chars);
            SendMessageW(_window, _registered ? TtmUpdateTipTextW : TtmAddToolW, 0, (nint)(&tool));
            _registered = true;
            SendMessageW(_window, TtmTrackPosition, 0, (y << 16) | (x & 0xFFFF));
            SendMessageW(_window, TtmTrackActivate, 1, (nint)(&tool));
        }
    }

    internal void Hide()
    {
        if (_window == IntPtr.Zero || !_registered)
            return;
        var tool = Tool(null);
        SendMessageW(_window, TtmTrackActivate, 0, (nint)(&tool));
        SendMessageW(_window, TtmDelToolW, 0, (nint)(&tool));
        _registered = false;
    }

    public void Dispose()
    {
        Hide();
        if (_window != IntPtr.Zero)
            DestroyWindow(_window);
        _window = IntPtr.Zero;
    }

    private ToolInfo Tool(char* text) => new()
    {
        Size = (uint)sizeof(ToolInfo),
        Flags = TtfIdIsHwnd | TtfTrack | TtfAbsolute | TtfTransparent,
        Window = _owner,
        Id = _owner,
        Text = text
    };

    // Window styles and tooltip messages (commctrl.h).
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint TtsAlwaysTip = 0x01;
    private const uint TtsNoPrefix = 0x02;
    private const int CwUseDefault = unchecked((int)0x80000000);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = -1;
    private const uint TtfIdIsHwnd = 0x0001, TtfTrack = 0x0020, TtfAbsolute = 0x0080, TtfTransparent = 0x0100;
    private const uint WmUser = 0x0400;
    private const uint TtmTrackActivate = WmUser + 17;
    private const uint TtmTrackPosition = WmUser + 18;
    private const uint TtmSetMaxTipWidth = WmUser + 24;
    private const uint TtmAddToolW = WmUser + 50;
    private const uint TtmDelToolW = WmUser + 51;
    private const uint TtmUpdateTipTextW = WmUser + 57;

    /// <summary>TTTOOLINFOW</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ToolInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public IntPtr Id;
        public int Left, Top, Right, Bottom;
        public IntPtr Instance;
        public char* Text;
        public IntPtr Param;
        public IntPtr Reserved;
    }

    [LibraryImport(Libraries.User32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint exStyle, string className, string? title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr SendMessageW(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32)]
    private static partial int SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [LibraryImport(Libraries.User32)]
    private static partial int DestroyWindow(IntPtr window);
}
