using System.Runtime.InteropServices;

// DllImport and LibraryImport load system DLLs from System32 only, never from the exe's folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Art14Helper;

internal static class Program
{
    /// <summary>
    /// No arguments: the tray app. Otherwise one job, then exit:
    /// <c>--set-name value</c> one hardware change with admin rights (UAC), for a tray app
    /// running without them, see <see cref="Privileged"/>; <c>--install</c> and
    /// <c>--uninstall</c>; and the elevated driver backup and restore of the Drivers page.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var hardening = ProcessHardening.Apply();   // before anything loads a DLL or touches a file
        UiThread.Start();
        LogUnexpectedErrors();
        if (Privileged.IsElevated && args is [])
            Log.Info($"Started with admin rights; protections: {hardening}");
        var value = args is [_, var second, ..] ? second : string.Empty;
        return args switch
        {
            [] => StartTray(),
            [var set, ..] when set.StartsWith("--set-", StringComparison.Ordinal)
                => ShowingErrors(() => Privileged.RunFromCommandLine(set[6..], value)),
            ["--install", ..] => Installer.Install(value),
            ["--uninstall", ..] => Installer.Uninstall(),
            [SelfTest.Switch, ..] => SelfTest.Run(),
            // Started by the Drivers page; reports through a file, no window.
            [var transfer, ..] when transfer is DriverBackup.BackupSwitch or DriverBackup.RestoreSwitch
                => DriverBackup.RunFromCommandLine(transfer, value),
            _ => StartTray()
        };
    }

    private static int StartTray()
    {
        // Not the installed copy, or not elevated: install, update or hand over.
        if (!Installer.PrepareTrayLaunch())
            return 0;
        // With admin rights, in the global namespace (per session): only administrators can create
        // names there, so no other program can take the name first and keep the app from starting.
        string name;
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
            name = Privileged.IsElevated ? @"Global\Art14Helper.SingleInstance." + self.SessionId : "Art14Helper.SingleInstance";
        using var single = new Mutex(false, name, out var first);
        if (!first)
            return 0;
        using var app = new TrayApp();
        UiThread.Run();
        return 0;
    }

    /// <summary>
    /// A tray app that crashes takes every feature away until the next sign-in. Errors in
    /// window messages and UI work are already caught and logged where they happen; here
    /// background tasks nobody awaited are logged and kept from ending the process, and a
    /// fatal error on another thread is at least written down.
    /// </summary>
    private static void LogUnexpectedErrors()
    {
        TaskScheduler.UnobservedTaskException += (_, error) =>
        {
            Log.Error("Background task failed unobserved", error.Exception);
            error.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, error)
            => Log.Error("Fatal error, the app closes", error.ExceptionObject as Exception);
    }

    /// <summary>
    /// A change the user asked for through UAC: a failure must be shown, or the prompt
    /// just flashes by with no explanation. Exit code 0 done, 2 unknown setting or value, 1 failed.
    /// </summary>
    private static int ShowingErrors(Func<bool> change)
    {
        try
        {
            return change() ? 0 : 2;
        }
        catch (Exception exception)
        {
            CommonDialogs.Message(IntPtr.Zero, exception.Message, AppInfo.Name, CommonDialogs.Icon.Error);
            return 1;
        }
    }
}
