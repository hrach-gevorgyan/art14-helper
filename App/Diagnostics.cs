using System.Text;

namespace Art14Helper;

/// <summary>
/// A plain-text report for a bug report or a question: versions, the laptop, what works,
/// the settings and the end of the log. No serial number; the user's folder becomes
/// %USERPROFILE% and program paths only show their file name. Copied, never sent.
/// </summary>
internal static class Diagnostics
{
    private const int LogLines = 60;

    /// <summary>
    /// Builds the report in the background (registry, devices, battery and log reads) and puts
    /// it on the clipboard from the UI thread, with a popup either way.
    /// </summary>
    internal static void Copy() => Task.Run(() =>
    {
        var report = Build();
        UiThread.Post(() =>
        {
            if (Clipboard.SetText(report))
                Popup.Show(PopupIcon.Copied, "Diagnostics copied", "Ready to paste");
            else
                Popup.Show(PopupIcon.Warning, "Diagnostics not copied", "Clipboard busy");
        });
    });

    internal static string Build()
    {
        var text = new StringBuilder();
        text.AppendLine($"{AppInfo.Name} {AppInfo.Version} diagnostics, {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine();
        Line(text, "Windows", Windows);
        Line(text, "Laptop", Laptop);
        Line(text, "Running", () => $"{(Privileged.IsElevated ? "with admin rights" : "without admin rights")}, "
                                    + $"{(Installer.IsInstalledCopy ? "installed copy" : "not the installed copy")}");
        Line(text, "Battery", Battery);
        Line(text, "Touchpad link", () => TouchpadLink.IsPresent ? "found" : "not found");
        Line(text, "Touchscreen", () => TouchscreenControl.IsSupported() ? "found" : "not found");
        Line(text, "Spatial sound", () => SpatialAudioController.IsSupported() ? "supported" : "not supported");
        Line(text, "Refresh rate", () => $"{RefreshRate.Current} Hz of {RefreshRate.Highest} Hz{(RefreshRate.IsAuto ? ", Auto" : "")}");
        Line(text, "Color modes", () => (ColorModeService.IsSupported switch { true => "supported", false => "not supported", _ => "not checked yet" })
                                        + (DisplayPanel.IsHdrOn() ? ", HDR on" : ""));
        Line(text, "Power mode", () => PowerMode.Current?.ToString() ?? "not offered");
        Line(text, "Keep awake", KeepAwake.Describe);

        text.AppendLine();
        text.AppendLine("Settings:");
        try
        {
            foreach (var (name, value) in AppSettings.All().OrderBy(pair => pair.Key, StringComparer.Ordinal))
                text.AppendLine($"  {name} = {Setting(name, value)}");
        }
        catch (Exception exception)
        {
            text.AppendLine($"  (unreadable: {exception.Message})");
        }

        text.AppendLine();
        text.AppendLine($"Last {LogLines} log lines:");
        foreach (var line in LastLogLines())
            text.AppendLine("  " + line);
        return Private(text.ToString());
    }

    /// <summary>A stored value as shown in the report: program paths only by file name.</summary>
    internal static string Setting(string name, object value)
        => value is string path && name.EndsWith("Command", StringComparison.Ordinal) ? Path.GetFileName(path) : value.ToString() ?? "";

    /// <summary>The user's own folder replaced, so the report doesn't carry the account name.</summary>
    internal static string Private(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 3 ? text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;
    }

    private static void Line(StringBuilder text, string name, Func<string> value)
    {
        string shown;
        try
        {
            shown = value();
        }
        catch (Exception exception)
        {
            shown = $"error: {exception.Message}";
        }
        text.AppendLine($"{name}: {shown}");
    }

    private static string Windows()
    {
        const string Key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string? Read(string name) => Convert.ToString(Microsoft.Win32.Registry.GetValue(Key, name, null));
        var name = Read("ProductName") ?? "Windows";
        // Windows 11 still calls itself Windows 10 there; the build number tells.
        if (int.TryParse(Read("CurrentBuild"), out var build) && build >= 22000)
            name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        return $"{name} {Read("DisplayVersion")}, build {build}.{Read("UBR")}".Trim();
    }

    private static string Laptop()
    {
        static string? Read(string name) => MachineProfile.BiosValue(name)?.Trim();
        return $"{Read("SystemFamily")} {Read("SystemProductName")}, BIOS {Read("BIOSVersion")} ({Read("BIOSReleaseDate")})";
    }

    private static string Battery()
    {
        var status = PowerSource.Battery;
        var text = $"{status.Percent?.ToString() ?? "?"}%, {(status.Charging ? "charging" : status.PluggedIn ? "plugged in" : "on battery")}";
        if (Native.Battery.ReadHealth() is { } health)
            text += $", health {health.Percent}% ({SettingsPages.HealthDetail(health)})";
        if (Native.Battery.ReadCharge() is { RateMilliwatts: not 0 } charge)
            text += $", {charge.RateMilliwatts / 1000.0:0.0} W";
        return text;
    }

    private static IEnumerable<string> LastLogLines()
    {
        try
        {
            if (!File.Exists(Log.Location))
                return ["(no log yet)"];
            // The app may be writing to it: share it.
            using var stream = new FileStream(Log.Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // The last lines are near the end: no need to read a log of up to a megabyte from the start.
            const int Tail = 16 * 1024;
            var cut = stream.Length > Tail;
            if (cut)
                stream.Seek(-Tail, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (cut)
                reader.ReadLine();   // most likely the middle of a line
            var lines = new Queue<string>(LogLines + 1);
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > LogLines)
                    lines.Dequeue();
            }
            return lines;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"(log unreadable: {exception.Message})"];
        }
    }
}
