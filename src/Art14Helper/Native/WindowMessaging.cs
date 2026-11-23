using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Messages between the app's own processes (a second launch asking the tray copy for its window).</summary>
internal static unsafe partial class WindowMessaging
{
    /// <summary>Lets processes without admin rights send <paramref name="message"/> to an elevated window.</summary>
    internal static void AcceptFromAnyProcess(IntPtr window, int message)
        => ChangeWindowMessageFilterEx(window, message, MsgfltAllow, IntPtr.Zero);

    /// <summary>
    /// Posts a message to the top-level window with this class and title; false when there is
    /// none, or when it doesn't belong to <paramref name="exe"/>: any program can open a window
    /// with the same name, and must not make a new copy of the app think one already runs.
    /// </summary>
    internal static bool PostToAppWindow(string className, string title, string exe, int message)
    {
        var window = FindWindowW(className, title);
        return window != IntPtr.Zero && BelongsTo(window, exe) && PostMessageW(window, message, 0, 0) != 0;
    }

    private static unsafe bool BelongsTo(IntPtr window, string exe)
    {
        if (GetWindowThreadProcessId(window, out var processId) == 0)
            return false;
        var process = OpenProcess(QueryLimitedInformation, 0, processId);
        if (process == IntPtr.Zero)
            return false;
        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref length) != 0
                   && new string(buffer, 0, length).Equals(Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint QueryLimitedInformation = 0x1000;   // PROCESS_QUERY_LIMITED_INFORMATION

    [LibraryImport(Libraries.User32)]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport(Libraries.Kernel32)]
    private static unsafe partial int QueryFullProcessImageNameW(IntPtr process, uint flags, char* name, ref int length);

    [LibraryImport(Libraries.Kernel32)]
    private static partial int CloseHandle(IntPtr handle);

    private const int MsgfltAllow = 1;

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? className, string? title);

    [LibraryImport(Libraries.User32, SetLastError = true)]
    private static partial int PostMessageW(IntPtr window, int message, nint wParam, nint lParam);

    [LibraryImport(Libraries.User32, SetLastError = true)]
    private static partial int ChangeWindowMessageFilterEx(IntPtr window, int message, int action, IntPtr status);
}
