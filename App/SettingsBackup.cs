namespace Art14Helper;

/// <summary>
/// Every choice made in the app saved to a small text file and read back, for a Windows
/// reinstall or another laptop of the same model: one "name = value" line each. What the
/// app only remembers about the hardware (last read modes, setup answers) stays out.
/// </summary>
internal static class SettingsBackup
{
    private const string Header = "# Art14 Helper settings";
    internal const string Filter = "Art14 Helper settings (*.txt)|*.txt";

    /// <summary>Values that describe the hardware's last state or the setup, not a choice.</summary>
    private static bool IsChoice(string name)
        => !name.StartsWith("Setup.", StringComparison.Ordinal)
           && !name.StartsWith("Lid.", StringComparison.Ordinal)
           && !name.StartsWith("Awake.Remote", StringComparison.Ordinal)
           && name is not ("Awake.Until" or "Awake.Minutes")
           && name is not ("Light.Current" or "Light.Emulated" or "Charge.Current" or "Charge.Once" or "Charge.OnceSawCharger");

    internal static string Write(IReadOnlyDictionary<string, object> values)
    {
        var lines = new List<string> { Header };
        foreach (var (name, value) in values.Where(pair => IsChoice(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (value is int number)
                lines.Add($"{name} = {number}");
            else if (value is string text && !text.Contains('\n'))
                lines.Add($"{name} = \"{text}\"");
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>The choices in a backup; numbers as int, text in quotes as string. Null when it isn't one.</summary>
    internal static Dictionary<string, object>? Read(string content)
    {
        var lines = content.Split('\n').Select(line => line.Trim()).ToList();
        if (lines.FirstOrDefault() != Header)
            return null;
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0 && !line.StartsWith('#')))
        {
            var equals = line.IndexOf(" = ", StringComparison.Ordinal);
            if (equals <= 0)
                continue;
            var name = line[..equals];
            var value = line[(equals + 3)..];
            if (!IsChoice(name))
                continue;
            if (value is ['"', .., '"'])
                values[name] = value[1..^1];
            else if (int.TryParse(value, out var number))
                values[name] = number;
        }
        return values;
    }

    /// <summary>Asks where to save, then writes the file.</summary>
    internal static void Export()
    {
        var file = CommonDialogs.AskForFile(User32.GetActiveWindow(), save: true, "Back up settings", Filter,
            $"Art14Helper settings {DateTime.Now:yyyy-MM-dd}.txt", "txt");
        if (file is null)
            return;
        try
        {
            File.WriteAllText(file, Write(AppSettings.All()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings backup not written", exception);
            CommonDialogs.Message(User32.GetActiveWindow(), exception.Message, AppInfo.Name, CommonDialogs.Icon.Error);
        }
    }

    /// <summary>Asks for a backup, stores its choices and applies them to the laptop in the background.</summary>
    internal static void Import()
    {
        var file = CommonDialogs.AskForFile(User32.GetActiveWindow(), save: false, "Restore settings", Filter);
        if (file is null)
            return;
        Dictionary<string, object>? values;
        try
        {
            values = Read(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            values = null;
            Log.Error("Settings backup not read", exception);
        }
        if (values is null)
        {
            CommonDialogs.Message(User32.GetActiveWindow(), "This file isn't an Art14 Helper settings backup.", AppInfo.Name, CommonDialogs.Icon.Error);
            return;
        }
        AppSettings.Store(values);
        AppSettings.ChargeOnce = false;
        Log.Info($"Settings restored from {Path.GetFileName(file)} ({values.Count} values)");
        _ = Task.Run(ApplyAsync);
    }

    /// <summary>Puts the restored choices on the hardware, as a fresh start would, colors included.</summary>
    private static async Task ApplyAsync()
    {
        await StoredSettings.ApplyAsync(AdminPrompt.UntilDeclined).ConfigureAwait(false);
        ColorModeService.Restore();
        Popup.Show(PopupIcon.Done, "Settings restored");
    }
}
