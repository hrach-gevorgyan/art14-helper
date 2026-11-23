using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Win32 popup menu calls for the tray menu: build, show, destroy.</summary>
internal static partial class PopupMenus
{
    private const uint String = 0x0000, Grayed = 0x0001, Disabled = 0x0002, Checked = 0x0008,
        Popup = 0x0010, Separator = 0x0800;
    private const uint ReturnCommand = 0x0100, RightButton = 0x0002; // TPM_*, aligned top-left

    internal static IntPtr Create() => CreatePopupMenu();

    /// <summary>Destroys a menu and every submenu attached to it.</summary>
    internal static void Destroy(IntPtr menu) => DestroyMenu(menu);

    internal static int ItemCount(IntPtr menu) => GetMenuItemCount(menu);

    internal static void AddItem(IntPtr menu, int id, string text, bool enabled, bool isChecked)
        => AppendMenuW(menu, String | (enabled ? 0 : Grayed | Disabled) | (isChecked ? Checked : 0), id, text);

    internal static void AddSubMenu(IntPtr menu, IntPtr subMenu, string text)
        => AppendMenuW(menu, Popup | String, subMenu, text);

    internal static void AddSeparator(IntPtr menu) => AppendMenuW(menu, Separator, 0, null);

    /// <summary>
    /// Shows the menu at a screen point and waits; returns the chosen item id, or 0.
    /// The owner is brought to the foreground first, or the menu won't close when
    /// the user clicks elsewhere.
    /// </summary>
    internal static int Track(IntPtr menu, IntPtr owner, int x, int y)
    {
        SetForegroundWindow(owner);
        var chosen = TrackPopupMenuEx(menu, ReturnCommand | RightButton, x, y, owner, IntPtr.Zero);
        PostMessageW(owner, 0, 0, 0);   // WM_NULL: lets the menu close properly the next time too
        return chosen;
    }

    /// <summary>Whether WM_MENUSELECT flags describe a submenu entry rather than a command.</summary>
    internal static bool IsSubMenuEntry(uint menuSelectFlags) => (menuSelectFlags & Popup) != 0;

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport(Libraries.User32)]
    private static partial int DestroyMenu(IntPtr menu);

    [LibraryImport(Libraries.User32)]
    private static partial int GetMenuItemCount(IntPtr menu);

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AppendMenuW(IntPtr menu, uint flags, nint idOrSubMenu, string? text);

    [LibraryImport(Libraries.User32)]
    private static partial int SetForegroundWindow(IntPtr window);

    [LibraryImport(Libraries.User32)]
    private static partial int PostMessageW(IntPtr window, uint message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32)]
    private static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
}
