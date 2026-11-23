using System.Text.Json;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ZipFile = System.IO.Compression.ZipFile;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Art14Helper;

/// <summary>How a backup or restore went: packages handled, whether Windows wants a restart, or why it failed.</summary>
internal sealed record DriverBackupResult(bool Done, int Packages, bool NeedsRestart, string? Problem)
{
    internal static DriverBackupResult Failed(string problem) => new(false, 0, false, problem);
}

/// <summary>
/// A driver backup for a clean Windows reinstall: every third-party driver in the
/// driver store (exported with pnputil) plus HONOR's factory color calibration, in one
/// zip. Restore installs such a zip, a folder of drivers, or a single .inf.
/// </summary>
/// <remarks>
/// pnputil requires admin rights for both directions. An elevated tray app does the job
/// itself; a non-elevated one starts the exe again through UAC (<see cref="BackupSwitch"/>,
/// <see cref="RestoreSwitch"/>) and reports through a small JSON file.
/// </remarks>
internal static class DriverBackup
{
    internal const string BackupSwitch = "--backup-drivers";
    internal const string RestoreSwitch = "--restore-drivers";

    /// <summary>pnputil's exit code for "done, restart needed".</summary>
    private const int RestartNeeded = 3010;

    /// <summary>Folder in the zip holding the color calibration.</summary>
    private const string ColorFolder = "Color";

    // In the program folder: every account can read it (the elevated copy may run as another
    // administrator, the one who typed the password), but only administrators can write it,
    // so nothing can forge the result or turn the elevated write against another file.
    private static readonly string ReportPath = Path.Combine(Installer.InstallDirectory, "Work", "driver-backup.json");

    internal static Task<DriverBackupResult> BackupAsync(string zipPath, CancellationToken stop) => RunAsync(BackupSwitch, zipPath, stop);

    internal static Task<DriverBackupResult> RestoreAsync(string source, CancellationToken stop) => RunAsync(RestoreSwitch, source, stop);

    /// <summary>
    /// The UAC-launched copy's entry point. Exit code: 0 done, 1 failed (details in the
    /// report file), 2 nothing to work with.
    /// </summary>
    internal static int RunFromCommandLine(string @switch, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 2;
        // Any program can start the exe with these switches and a path of its choosing, and the UAC
        // prompt only names Art14 Helper. So the elevated copy shows what it is about to do and
        // where, and asks again.
        var backup = @switch == BackupSwitch;
        if (backup && UnsafeBackupTarget(path) is { } refusal)
        {
            WriteReport(DriverBackupResult.Failed(refusal));
            return 1;
        }
        var question = backup
            ? $"Save a backup of all drivers to\n{Path.GetFullPath(path)}?"
            : $"Install the drivers from\n{Path.GetFullPath(path)}?\n\nOnly continue if you chose this in Art14 Helper just now.";
        if (!CommonDialogs.Message(IntPtr.Zero, question, AppInfo.Name, CommonDialogs.Icon.Warning, yesNo: true))
        {
            WriteReport(DriverBackupResult.Failed("Cancelled."));
            return 1;
        }
        var result = backup ? Backup(path) : Restore(path);
        WriteReport(result);
        return result.Done ? 0 : 1;
    }

    private static async Task<DriverBackupResult> RunAsync(string @switch, string path, CancellationToken stop)
    {
        if (Privileged.IsElevated)
            return await Task.Run(() => @switch == BackupSwitch ? Backup(path) : Restore(path), stop);

        var started = DateTime.UtcNow;   // a report older than this run is a stale one
        var exe =Environment.ProcessPath ?? throw new InvalidOperationException("The app's own path is unknown.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add(@switch);
        start.ArgumentList.Add(path);
        try
        {
            using var copy = Process.Start(start) ?? throw new InvalidOperationException("Windows didn't start the elevated copy.");
            await copy.WaitForExitAsync(stop);
            return ReadReport(started) ?? DriverBackupResult.Failed($"The elevated copy ended with code {copy.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception declined) when (declined.NativeErrorCode == PrivilegedAction.PromptCancelled)
        {
            throw new OperationCanceledException("UAC was declined.", declined, stop);
        }
    }

    /// <summary>Why a backup may not go to <paramref name="zipPath"/>, or null: only a .zip, never into Windows or Program Files.</summary>
    internal static string? UnsafeBackupTarget(string zipPath)
    {
        var full = Path.GetFullPath(zipPath);
        if (!Path.GetExtension(full).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return "A driver backup is saved as a .zip file.";
        foreach (var system in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                 })
            if (system.Length > 0 && full.StartsWith(Path.TrimEndingDirectorySeparator(system) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Choose a folder of your own for the backup, not a system folder.";
        return null;
    }

    private static DriverBackupResult Backup(string zipPath)
    {
        if (UnsafeBackupTarget(zipPath) is { } refusal)
            return DriverBackupResult.Failed(refusal);
        var work = WorkFolder("backup");
        try
        {
            var export = SystemTool.Run("pnputil.exe", "/export-driver", "*", work);
            var packages = Directory.GetFiles(work, "*.inf", SearchOption.AllDirectories).Length;
            if (packages == 0)
                return DriverBackupResult.Failed($"Windows exported no third-party drivers (pnputil {export.ExitCode}: {export.Tail()}).");

            CopyColorCalibration(ColorModeService.ColorDirectory, Directory.CreateDirectory(Path.Combine(work, ColorFolder)).FullName, fromColorFolder: true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            File.Delete(zipPath);
            ZipFile.CreateFromDirectory(work, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Log.Info($"Driver backup: {packages} packages in {zipPath}");
            return new DriverBackupResult(true, packages, false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Driver backup failed", exception);
            return DriverBackupResult.Failed(exception.Message);
        }
        finally
        {
            Remove(work);
        }
    }

    private static DriverBackupResult Restore(string source)
    {
        string? work = null;
        try
        {
            string[] arguments;
            int packages;
            if (File.Exists(source) && Path.GetExtension(source).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            {
                arguments = ["/add-driver", Path.GetFullPath(source), "/install"];
                packages = 1;
            }
            else
            {
                var folder = source;
                if (File.Exists(source))
                {
                    work = WorkFolder("restore");
                    DriverPackage.UnpackInside(source, work);   // refuses entries that point outside the folder
                    folder = work;
                }
                else if (!Directory.Exists(source))
                    return DriverBackupResult.Failed("That file or folder doesn't exist any more.");

                var colors = Path.Combine(folder, ColorFolder);
                if (Directory.Exists(colors))
                    CopyColorCalibration(colors, ColorModeService.ColorDirectory, fromColorFolder: false);
                packages = Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories).Length;
                if (packages == 0)
                    return DriverBackupResult.Failed("There are no driver files (.inf) in it.");
                arguments = ["/add-driver", Path.Combine(Path.GetFullPath(folder), "*.inf"), "/subdirs", "/install"];
            }

            var install = SystemTool.Run("pnputil.exe", arguments);
            if (install.ExitCode is not (0 or RestartNeeded))
                return DriverBackupResult.Failed($"Windows didn't install the drivers (pnputil {install.ExitCode}: {install.Tail()}).");
            Log.Info($"Driver restore: {packages} packages from {source}");
            return new DriverBackupResult(true, packages, install.ExitCode == RestartNeeded, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            Log.Error("Driver restore failed", exception);
            return DriverBackupResult.Failed(exception.Message);
        }
        finally
        {
            if (work is not null)
                Remove(work);
        }
    }

    /// <summary>
    /// HONOR's factory color calibration of this panel (the most accurate source for the
    /// color modes). It isn't part of any driver package, so it travels separately.
    /// Users may add files to the color folder: from there only files an administrator owns
    /// are taken, and into it each file is created anew (removed first), so a link planted
    /// under the same name can't redirect the write.
    /// </summary>
    internal static int CopyColorCalibration(string fromFolder, string toFolder, bool fromColorFolder)
    {
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(fromFolder, "*_display_factory_calibration_*.xml"))
        {
            if (fromColorFolder && !ColorModeService.IsOwnedBySystem(file))
                continue;
            var target = Path.Combine(toFolder, Path.GetFileName(file));
            File.Delete(target);
            using (var source = File.OpenRead(file))
            using (var copy = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(copy);
            copied++;
        }
        return copied;
    }

    // ---- The report file of a UAC-launched copy ----

    internal static string ToJson(DriverBackupResult result)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("done", result.Done);
            json.WriteNumber("packages", result.Packages);
            json.WriteBoolean("needsRestart", result.NeedsRestart);
            json.WriteString("problem", result.Problem);
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static DriverBackupResult FromJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new DriverBackupResult(root.GetProperty("done").GetBoolean(), root.GetProperty("packages").GetInt32(),
            root.GetProperty("needsRestart").GetBoolean(), root.GetProperty("problem").GetString());
    }

    private static void WriteReport(DriverBackupResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, ToJson(result));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Driver backup report not written", exception);
        }
    }

    private static DriverBackupResult? ReadReport(DateTime notBefore)
    {
        try
        {
            return File.Exists(ReportPath) && File.GetLastWriteTimeUtc(ReportPath) >= notBefore.AddSeconds(-2)
                ? FromJson(File.ReadAllText(ReportPath))
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Error("Driver backup report unreadable", exception);
            return null;
        }
    }

    /// <summary>In the app's own folder, where only administrators can write (see <see cref="AppFolders"/>).</summary>
    private static string WorkFolder(string purpose) => AppFolders.NewWorkFolder(purpose);

    private static void Remove(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Temporary folder {folder} left behind", exception);
        }
    }
}
