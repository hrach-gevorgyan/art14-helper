using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Dark window chrome and menus, the way Windows' own apps get them.</summary>
internal static partial class WindowTheme
{
    /// <summary>A dark or light title bar; on Windows 11 the caption also takes <paramref name="caption"/>.</summary>
    internal static void TitleBar(IntPtr window, bool dark, Color caption)
    {
        var on = dark ? 1 : 0;
        DwmSetWindowAttribute(window, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        var color = caption.R | caption.G << 8 | caption.B << 16;      // COLORREF
        DwmSetWindowAttribute(window, 35, ref color, sizeof(int));     // DWMWA_CAPTION_COLOR, ignored on Windows 10
    }

    /// <summary>
    /// Popup menus owned by <paramref name="window"/> follow the system light/dark app
    /// mode. These uxtheme switches have no names, only numbers; where they're missing
    /// the menus simply stay light.
    /// </summary>
    internal static void MenusFollowSystem(IntPtr window)
    {
        try
        {
            SetPreferredAppMode(1);   // allow dark
            AllowDarkModeForWindow(window, 1);
            FlushMenuThemes();
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Info("Menus stay light: this Windows has no dark menu switch");
        }
    }

    /// <summary>Picks up a light/dark change for menus opened from now on.</summary>
    internal static void RefreshMenus()
    {
        try
        {
            FlushMenuThemes();
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    [LibraryImport(Libraries.Dwmapi)]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [LibraryImport(Libraries.UxTheme, EntryPoint = "#133")]
    private static partial int AllowDarkModeForWindow(IntPtr window, int allow);

    [LibraryImport(Libraries.UxTheme, EntryPoint = "#135")]
    private static partial int SetPreferredAppMode(int mode);

    [LibraryImport(Libraries.UxTheme, EntryPoint = "#136")]
    private static partial void FlushMenuThemes();
}
