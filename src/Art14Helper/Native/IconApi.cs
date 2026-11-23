using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Icon sizes, the app's own icon, and the notification-area (tray) entry.</summary>
internal static unsafe partial class IconApi
{
    /// <summary>
    /// Width of a small icon (the tray size) at the taskbar's current scale. Plain
    /// GetSystemMetrics keeps the scale the app started with, so after a scale change the
    /// icon would be drawn at the old size and look blurry.
    /// </summary>
    internal static int SmallIconSize
    {
        get
        {
            var taskbar = FindWindowW("Shell_TrayWnd", null);
            var dpi = taskbar != IntPtr.Zero ? User32.GetDpiForWindow(taskbar) : 0;
            return dpi > 0 ? GetSystemMetricsForDpi(49, dpi) : GetSystemMetrics(49);   // SM_CXSMICON
        }
    }

    /// <summary>Frees an HICON made by Bitmap.GetHicon or <see cref="AppIcon"/>.</summary>
    internal static void Destroy(IntPtr icon) => DestroyIcon(icon);

    /// <summary>This exe's icon at <paramref name="size"/> pixels; the caller frees it. Zero if there is none.</summary>
    internal static IntPtr AppIcon(int size)
    {
        if (Environment.ProcessPath is not { } exe)
            return IntPtr.Zero;
        IntPtr icon;
        var bothSizes = (uint)(size | size << 16);
        return SHDefExtractIconW(exe, 0, 0, &icon, null, bothSizes) == 0 ? icon : IntPtr.Zero;
    }

    // ---- Notification area ----

    private const uint Add = 0, Modify = 1, Delete = 2;               // NIM_*
    private const uint WithMessage = 1, WithIcon = 2, WithTip = 4;    // NIF_*

    /// <summary>NOTIFYICONDATAW</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrayEntry
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State, StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    internal const int TrayTipLength = 127;

    /// <summary>Adds, or with <paramref name="update"/> changes, the window's tray entry.</summary>
    internal static bool SetTrayEntry(IntPtr window, uint callbackMessage, IntPtr icon, string tip, bool update)
    {
        var entry = new TrayEntry
        {
            Size = (uint)sizeof(TrayEntry),
            Window = window,
            Flags = WithMessage | WithIcon | WithTip,
            CallbackMessage = callbackMessage,
            Icon = icon
        };
        var length = Math.Min(tip.Length, TrayTipLength);
        tip.AsSpan(0, length).CopyTo(new Span<char>(entry.Tip, 128));
        return Shell_NotifyIconW(update ? Modify : Add, &entry) != 0;
    }

    internal static void RemoveTrayEntry(IntPtr window)
    {
        var entry = new TrayEntry { Size = (uint)sizeof(TrayEntry), Window = window };
        Shell_NotifyIconW(Delete, &entry);
    }

    [LibraryImport(Libraries.User32)]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport(Libraries.User32)]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? className, string? title);

    [LibraryImport(Libraries.User32)]
    private static partial int DestroyIcon(IntPtr icon);

    [LibraryImport(Libraries.Shell32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIconW(string file, int index, uint flags, IntPtr* large, IntPtr* small, uint sizes);

    [LibraryImport(Libraries.Shell32)]
    private static partial int Shell_NotifyIconW(uint message, TrayEntry* entry);
}
