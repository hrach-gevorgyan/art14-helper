using Win32Exception = System.ComponentModel.Win32Exception;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Art14Helper;

/// <summary>
/// Keeps the app in <c>C:\Program Files\Art14Helper</c> and running elevated.
///
/// Why: the app needs admin rights for the BIOS. An elevated logon task that starts
/// an exe from a user-writable folder (Downloads, a build folder) is a UAC bypass:
/// any program running as the user could replace that exe. Program Files is
/// writable only by administrators, so the logon task can safely start it elevated.
///
/// Flow when you double-click a freshly built exe:
/// - same build already installed: start the installed copy and exit;
/// - not installed or a different build: offer to install/update (one UAC prompt),
///   then the installed copy starts elevated through the logon task.
/// When the installed copy itself is started without admin rights (e.g. from the
/// Start menu), it hands over to the logon task, or asks UAC if there's no task.
/// </summary>
internal static class Installer
{
    private const string Title = AppInfo.Name;

    internal static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Art14Helper");

    internal static string InstalledExe { get; } = Path.Combine(InstallDirectory, "Art14Helper.exe");

    internal static bool IsInstalled => File.Exists(InstalledExe);

    internal static bool IsInstalledCopy => Environment.ProcessPath is { } current
        && string.Equals(Path.GetFullPath(current), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Called before the tray starts. true = run the tray in this process;
    /// false = another (installed, elevated) instance was started, so exit.
    /// </summary>
    internal static bool PrepareTrayLaunch()
    {
        try
        {
            if (IsInstalledCopy)
            {
                // With admin rights while a copy without them runs: that copy makes way.
                // Otherwise, opened again (Start menu, exe) while it runs: show its window instead.
                if (!TryReplaceLimitedInstance() && TrySignalRunningInstance())
                    return false;

                // Elevated already, or a standard (non-admin) account that can never
                // be elevated: run here. Handing over would just start this again.
                if (Privileged.IsElevated || !IsAdministratorAccount())
                    return true;

                // Started without admin rights: hand over to an elevated instance.
                // If UAC is declined, run here with limited functions.
                return !(Autostart.TryStartNow(InstalledExe) || TryStartElevated(InstalledExe, null));
            }

            var current = Environment.ProcessPath;
            if (current is null)
                return true;

            if (IsInstalled && SameFile(current, InstalledExe))
            {
                if (TrySignalRunningInstance())
                    return false;
                return !(Autostart.TryStartNow(InstalledExe) || TryStartElevated(InstalledExe, null));
            }

            using var setup = new SetupWindow(IsInstalled ? SetupWindow.Mode.Update : SetupWindow.Mode.Install);
            setup.ShowDialog();
            if (setup.Result is not { } choices)
                return false;

            // The elevated copy installs, shows the progress and starts the app.
            TryStartElevated(current, "--install", EncodeChoices(choices, update: IsInstalled));
            return false;
        }
        catch (Exception exception)
        {
            Log.Error("Install check failed; running from the current folder", exception);
            return true;
        }
    }

    /// <summary>
    /// <c>--install</c> (elevated): stops running copies, copies this exe to Program
    /// Files (removing files left by an earlier update), registers the logon task and
    /// starts the installed copy. Exit code 0 on success.
    /// </summary>
    internal static int Install(string options)
    {
        if (!Privileged.IsElevated)
        {
            CommonDialogs.Message(IntPtr.Zero, "Installing needs administrator rights.", Title, CommonDialogs.Icon.Error);
            return 1;
        }

        var (choices, update) = DecodeChoices(options);
        using var window = new SetupWindow(SetupWindow.Mode.Installing, (_, progress) => InstallCore(choices, update, progress));
        window.ShowDialog();
        return window.Result is null ? 1 : 0;
    }

    private static string InstallCore(SetupWindow.Choices choices, bool update, Action<string> progress)
    {
        var source = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the path to this exe.");
        // Read through a handle that stays open: without write or delete sharing, nobody can
        // rename or replace this exe (it may sit in a folder the user can write to) between
        // starting with admin rights and the copy into Program Files.
        using var sourceFile = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);

        progress("Closing the running copy…");
        StopOtherInstances();
        Directory.CreateDirectory(InstallDirectory);
        RemoveOldCopies();

        progress("Copying files…");
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
            CopyWithRetry(sourceFile, InstalledExe);

        progress("Setting up…");
        if (!update)
        {
            AppSettings.SetupStartWithWindows = choices.StartWithWindows;
            if (!choices.RecommendedSettings)
            {
                // Nothing changes by itself: no first-run defaults, colors stay native.
                AppSettings.FirstRunDefaultsApplied = true;
                AppSettings.ColorMode = ColorMode.Native;
            }
        }
        if (update ? Autostart.IsEnabled || AppSettings.SetupStartWithWindows != false : choices.StartWithWindows)
            Autostart.RegisterFor(InstalledExe);
        else
            Autostart.SetEnabled(false);
        RegisterUninstallEntry();
        CreateStartMenuShortcut();
        Log.Info("Installed to " + InstalledExe);

        progress("Starting…");
        // Started directly (inherits this elevated process), not through the logon
        // task: the task may still count the copy stopped a moment ago as running
        // and would ignore a new start.
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true })?.Dispose();
        return update
            ? $"{AppInfo.Name} is updated and running in the tray."
            : $"{AppInfo.Name} is installed and running in the tray. Left-click its icon to open it; it's also in the Start menu.";
    }

    private static string EncodeChoices(SetupWindow.Choices choices, bool update)
        => $"startup={(choices.StartWithWindows ? 1 : 0)};defaults={(choices.RecommendedSettings ? 1 : 0)};update={(update ? 1 : 0)}";

    private static (SetupWindow.Choices Choices, bool Update) DecodeChoices(string options)
    {
        var values = options.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1] == "1");
        bool Get(string key) => !values.TryGetValue(key, out var value) || value;
        return (new SetupWindow.Choices(Get("startup"), Get("defaults"), false), values.GetValueOrDefault("update"));
    }

    // ---- Start menu ----

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppInfo.Name + ".lnk");

    private static void CreateStartMenuShortcut()
    {
        try
        {
            ShellLink.Save(StartMenuShortcut, InstalledExe, InstallDirectory, AppInfo.Name);
        }
        catch (Exception exception)
        {
            Log.Error("Could not create the Start menu shortcut", exception);
        }
    }

    // ---- Running instance ----

    internal const string TrayWindowCaption = "Art14Helper.TrayWindow";
    internal const string TrayWindowClass = "Art14Helper.Tray";
    internal const int WmShowSettings = 0x8001; // WM_APP + 1
    // Asks the running copy to close cleanly (update, uninstall). Not let through from
    // processes without admin rights, so nothing else can close the app this way.
    internal const int WmExit = 0x8003; // WM_APP + 3
    // Asks a copy running without admin rights to close because one with admin rights is
    // starting. (WM_APP + 2 is the tray icon's callback.)
    internal const int WmHandOver = 0x8004; // WM_APP + 4

    /// <summary>Asks a running copy to open its settings window. true if one was found.</summary>
    private static bool TrySignalRunningInstance()
        => WindowMessaging.PostToAppWindow(TrayWindowClass, TrayWindowCaption, InstalledExe, WmShowSettings);

    /// <summary>
    /// The single-instance mutex. With admin rights it's in the global namespace (per session):
    /// only administrators can create names there, so no other program can take the name first.
    /// </summary>
    internal static string SingleInstanceName(bool elevated)
    {
        using var self = Process.GetCurrentProcess();
        return elevated ? @"Global\Art14Helper.SingleInstance." + self.SessionId : "Art14Helper.SingleInstance";
    }

    /// <summary>
    /// Started with admin rights while a copy without them runs (reopened after the elevated one
    /// closed, then started again through the task or "Restart with admin rights"): that copy is
    /// asked to close and this one carries on. Opening its window instead would leave the app
    /// without the BIOS until the next sign-in. false when there's no such copy, or when a copy
    /// with admin rights runs (it holds the global mutex): that one is asked for its window.
    /// </summary>
    private static bool TryReplaceLimitedInstance()
    {
        if (!Privileged.IsElevated)
            return false;
        if (Mutex.TryOpenExisting(SingleInstanceName(elevated: true), out var elevatedCopy))
        {
            elevatedCopy.Dispose();
            return false;
        }
        var window = WindowMessaging.FindAppWindow(TrayWindowClass, TrayWindowCaption, InstalledExe);
        if (window == IntPtr.Zero)
            return false;
        Log.Info("A copy without admin rights is running: asking it to close and taking over");
        WindowMessaging.Post(window, WmHandOver);
        if (!WindowMessaging.WaitUntilGone(window, 5000))
            Log.Error("The copy without admin rights didn't close within 5 s; starting anyway");
        return true;
    }

    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Art14Helper";

    /// <summary>Lists the app in Settings > Apps > Installed apps, with an Uninstall button.</summary>
    private static void RegisterUninstallEntry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(UninstallKeyPath, true);
            key.SetValue("DisplayName", Title);
            key.SetValue("DisplayVersion", AppInfo.Version);
            key.SetValue("Publisher", "Private build");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception exception)
        {
            Log.Error("Could not register the uninstall entry", exception);
        }
    }

    /// <summary>
    /// <c>--uninstall</c>: removes everything the app added. BIOS settings (charge
    /// limit, Fn lock, keyboard) stay as they are; the screen goes back to native
    /// colors. Asks for UAC if needed and confirms first.
    /// </summary>
    internal static int Uninstall()
    {
        if (!Privileged.IsElevated)
        {
            var self = Environment.ProcessPath;
            return self is not null && TryStartElevated(self, "--uninstall") ? 0 : 1;
        }

        using var window = new SetupWindow(SetupWindow.Mode.Uninstall, (choices, progress) => UninstallCore(choices, progress));
        window.ShowDialog();
        if (window.Result is null)
            return 1;

        // Started last, after the window is closed: this exe may live in the program
        // folder and stays locked while this process runs. The command waits a moment
        // for the exit, then removes the program folder (with its log) and the per-user folder.
        // Twice, a few seconds apart: a virus scan or a closing process can
        // hold a file for a moment and make the first attempt leave it behind.
        var remove = $"rmdir /s /q \"{InstallDirectory}\""
            + $" & rmdir /s /q \"{AppFolders.User}\"";
        var command = $"ping 127.0.0.1 -n 3 > nul & {remove} & ping 127.0.0.1 -n 4 > nul & {remove}";
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c " + command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory
        })?.Dispose();
        return 0;
    }

    private static string UninstallCore(SetupWindow.Choices choices, Action<string> progress)
    {
        progress("Closing the app…");
        StopOtherInstances();
        progress("Removing autostart and shortcuts…");
        Autostart.SetEnabled(false);
        TryRun(() => File.Delete(StartMenuShortcut));
        TryRun(() => Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, false));
        progress("Restoring the display colors…");
        ColorModeService.ResetToNative();
        // The lid closes to sleep again where the app had changed it (remote mode or its switches).
        foreach (var battery in new[] { false, true })
            if (AppSettings.GetLidBefore(battery) is not null)
                TryRun(() => LidClosed.SetKeepRunning(battery, false));
        AppSettings.RemoteMode = false;
        if (choices.RemoveChargeLimit)
        {
            progress("Turning off the charge limit…");
            TryRun(() => ChargeLimits.Apply(ChargeLimit.Off));
        }
        if (!choices.KeepSettings)
            TryRun(() => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Art14Helper", false));
        Log.Info("Uninstalled");
        return $"{AppInfo.Name} is removed. If you go back to HONOR PC Manager, turn its OSD service on again "
            + "(Set-Service OSDServer -StartupType Automatic).";
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Error("Uninstall step failed", exception);
        }
    }

    private const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>
    /// True for a member of Administrators, elevated or not. While UAC runs the app filtered,
    /// Administrators is in the token only as a deny-only group, and WindowsIdentity.Groups
    /// leaves deny-only groups out; the claims list them as denyonlysid. Checking Groups alone
    /// took every filtered admin for a standard user, so a copy started without admin rights
    /// never handed over to the elevated task and ran without fans, temperatures or the BIOS.
    /// </summary>
    internal static bool IsAdministratorAccount()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.Claims.Any(claim => claim.Value == AdministratorsSid
            && claim.Type is System.Security.Claims.ClaimTypes.GroupSid or System.Security.Claims.ClaimTypes.DenyOnlySid);
    }

    /// <summary>
    /// Closes running copies: first politely, so the tray copy removes its icon and stops its
    /// services, then by force for whatever is still running after a few seconds.
    /// </summary>
    private static void StopOtherInstances()
    {
        var self = Environment.ProcessId;
        if (WindowMessaging.PostToAppWindow(TrayWindowClass, TrayWindowCaption, InstalledExe, WmExit))
        {
            var deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline && Process.GetProcessesByName("Art14Helper").Count(process => Release(process) != self) > 0)
                Thread.Sleep(100);
        }
        foreach (var process in Process.GetProcessesByName("Art14Helper"))
        {
            using (process)
            {
                if (process.Id == self)
                    continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not stop running copy {process.Id}", exception);
                }
            }
        }
    }

    /// <summary>The process id, disposing the Process object on the way.</summary>
    private static int Release(Process process)
    {
        using (process)
            return process.Id;
    }

    private static void RemoveOldCopies()
    {
        foreach (var file in Directory.GetFiles(InstallDirectory, "Art14Helper.exe.old-*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still in use: removed at the next restart (scheduled when it was moved).
            }
        }
    }

    /// <summary>
    /// Replaces the installed exe. Right after a process exits, or while Windows
    /// finishes deleting the file of an uninstall, the old exe can refuse access
    /// for a few seconds (as "in use" or "access denied"): retried for up to 10 s.
    /// If it's still locked, the old file is renamed out of the way (Windows
    /// allows renaming an exe that is in use) and removed at the next restart.
    /// </summary>
    private static void CopyWithRetry(FileStream source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                    File.SetAttributes(destination, FileAttributes.Normal);
                CopyTo(source, destination);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 20)
            {
                if (attempt == 1)
                    Log.Info($"Installed exe is locked ({exception.GetType().Name}), retrying");
                Thread.Sleep(500);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var aside = destination + $".old-{Guid.NewGuid():N}";
                Log.Info($"Installed exe still locked, moving it aside: {exception.Message}");
                File.Move(destination, aside);
                FileApi.DeleteAtRestart(aside);
                CopyTo(source, destination);
                return;
            }
        }
    }

    private static void CopyTo(FileStream source, string destination)
    {
        source.Position = 0;
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static bool SameFile(string first, string second)
    {
        var a = new FileInfo(first);
        var b = new FileInfo(second);
        if (a.Length != b.Length)
            return false;

        using var streamA = a.OpenRead();
        using var streamB = b.OpenRead();
        return SHA256.HashData(streamA).AsSpan().SequenceEqual(SHA256.HashData(streamB));
    }

    /// <summary>Starts an exe elevated (UAC). false = declined or failed.</summary>
    /// <summary>
    /// "Restart with admin rights" from a copy running without them: starts the installed app
    /// through the sign-in task (no prompt), or through UAC when there's no task. The new copy
    /// then asks this one to close (<see cref="WmHandOver"/>). false when nothing started.
    /// </summary>
    internal static bool StartWithAdminRights()
        => Autostart.TryStartNow(InstalledExe) || TryStartElevated(InstalledExe, null);

    /// <summary>
    /// Running as the installed copy without admin rights on an account that could have them:
    /// the BIOS features are off, and "Restart with admin rights" can fix it.
    /// </summary>
    internal static bool IsLimitedMode => !Privileged.IsElevated && IsInstalledCopy && IsAdministratorAccount();

    private static bool TryStartElevated(string executable, string? argument, string? value = null)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        if (argument is not null)
            startInfo.ArgumentList.Add(argument);
        if (value is not null)
            startInfo.ArgumentList.Add(value);

        try
        {
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception declined) when (declined.NativeErrorCode == PrivilegedAction.PromptCancelled)
        {
            return false;
        }
        catch (Exception exception)
        {
            Log.Error("Could not start an elevated copy", exception);
            return false;
        }
    }
}
