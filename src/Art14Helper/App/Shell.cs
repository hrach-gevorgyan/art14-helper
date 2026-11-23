using System.Diagnostics;

namespace Art14Helper;

/// <summary>
/// Opens things through the desktop shell (explorer.exe). The tray app runs elevated, and
/// a program it started directly would inherit admin rights; Explorer starts it as the
/// signed-in user instead, as long as it is running: without it, a new explorer.exe would
/// start elevated itself, so nothing is opened then. Explorer reports no errors back, so
/// failures only reach the log.
/// </summary>
internal static class Shell
{
    private static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Opens a file, folder or program as the user would by double-clicking it.</summary>
    internal static void Open(string target)
        => Start(new ProcessStartInfo(Explorer) { UseShellExecute = false, ArgumentList = { target } }, target);

    /// <summary>Opens the file's folder with the file selected.</summary>
    internal static void Reveal(string file)
        => Start(new ProcessStartInfo(Explorer, $"/select,\"{file}\"") { UseShellExecute = false }, file);

    private static void Start(ProcessStartInfo start, string target)
    {
        if (Privileged.IsElevated && Native.User32.GetShellWindow() == IntPtr.Zero)
        {
            Log.Info($"Not opening {target}: Explorer isn't running, and it would start with admin rights");
            Popup.Show(PopupIcon.Warning, "Explorer isn't running", "Couldn't open it without admin rights");
            return;
        }
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"Explorer didn't open {target}", exception);
        }
    }
}
