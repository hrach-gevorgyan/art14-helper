using System.Diagnostics;

namespace Art14Helper;

/// <summary>Presses the Volume Up / Down media key.</summary>
internal static class MediaKeys
{
    internal static void Volume(bool up)
    {
        User32.TapKey(up ? (byte)0xAF : (byte)0xAE);   // VK_VOLUME_UP, VK_VOLUME_DOWN
    }
}

/// <summary>What F10 / F11 do. PC Manager used them for itself and for YOYO.</summary>
internal enum HotkeyAction
{
    Nothing,
    Calculator,
    TaskManager,
    Snipping,
    Settings,
    Explorer,
    Custom
}

internal static class HotkeyActions
{
    /// <summary>The fixed choices; the text is English, translated where it's shown.</summary>
    internal static readonly (HotkeyAction Action, string Text)[] Choices =
    [
        (HotkeyAction.Nothing, N("Nothing")),
        (HotkeyAction.Calculator, N("Calculator")),
        (HotkeyAction.TaskManager, N("Task Manager")),
        (HotkeyAction.Snipping, N("Screen snip")),
        (HotkeyAction.Settings, N("Windows Settings")),
        (HotkeyAction.Explorer, N("File Explorer"))
    ];

    /// <summary>Runs the action set for the key. Called off the WMI event thread.</summary>
    internal static void Run(HotkeyAction action, string? customCommand)
    {
        string? target = action switch
        {
            HotkeyAction.Calculator => "calculator:",
            HotkeyAction.TaskManager => "taskmgr.exe",
            HotkeyAction.Snipping => "ms-screenclip:",
            HotkeyAction.Settings => "ms-settings:",
            HotkeyAction.Explorer => "explorer.exe",
            HotkeyAction.Custom => customCommand,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(target))
            return;

        if (action == HotkeyAction.Custom && !File.Exists(target))
        {
            Log.Error($"Hotkey program not found: {target}");
            Popup.Show(PopupIcon.Warning, T("Program not found"), Path.GetFileName(target));
            return;
        }

        // Programs and File Explorer go through the shell, so they don't inherit the app's
        // admin rights. URIs (calculator:, ms-settings:, ms-screenclip:) open store apps,
        // which never run elevated; Task Manager may.
        if (action is HotkeyAction.Custom or HotkeyAction.Explorer && Privileged.IsElevated)
        {
            Shell.Open(target == "explorer.exe" ? "," : target);
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Log.Error($"Hotkey action {action} failed: {target}", exception);
        }
    }
}
