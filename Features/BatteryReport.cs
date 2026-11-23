namespace Art14Helper;

/// <summary>
/// Windows' own battery report (powercfg /batteryreport): capacity history, recent use and
/// battery life estimates, as a page in the browser. No admin rights needed.
/// </summary>
internal static class BatteryReport
{
    /// <summary>Makes the report and opens it as the normal user; a popup says when that failed. Runs off the UI thread.</summary>
    internal static void Open() => Task.Run(() =>
    {
        // powercfg runs with the app's admin rights, so it writes into the app's own folder, never
        // to a fixed name in a folder the user can write to (a link there would redirect it).
        var file = AppFolders.WorkFile("Battery report.html");
        try
        {
            if (SystemTool.Run("powercfg.exe", "/batteryreport", "/output", file).ExitCode != 0 || !File.Exists(file))
                throw new IOException("powercfg didn't write the report");
            Shell.Open(file);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Battery report not opened", exception);
            Popup.Failed("Battery report failed");
        }
    });
}
