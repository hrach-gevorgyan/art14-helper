namespace Art14Helper;

/// <summary>Tray menu items for the Art 14 additions.</summary>
internal static class Art14Menu
{
    // ---- Fn lock ----

    internal static void BuildFnLock(IMenuBuilder menu)
    {
        var current = AppSettings.FnMode;
        menu.AddItem("Standard F-keys (F5 = reload)",
            async () => await ApplyFnModeAsync(FnKeyMode.Standard),
            @checked: current == FnKeyMode.Standard,
            tooltip: "F1-F12 work as F-keys. Hold Fn for volume, brightness and so on.");
        menu.AddItem("Media keys (F5 = volume)",
            async () => await ApplyFnModeAsync(FnKeyMode.Media),
            @checked: current == FnKeyMode.Media,
            tooltip: "Keys change volume, brightness and so on. Hold Fn for F1-F12.");
    }

    private static Task ApplyFnModeAsync(FnKeyMode mode)
        => Privileged.FnMode.ApplyFromMenuAsync(mode, "Could not change the Fn key mode.",
            () => AppSettings.FnMode = mode);

    // ---- Spatial audio ----

    internal static void BuildSound(IMenuBuilder menu)
    {
        var spatial = SpatialAudioController.IsSpatial();
        menu.AddItem("Spatial", async () => await ApplySpatialAsync(true), @checked: spatial == true,
            tooltip: "Wider, more room-filling sound from the speakers: good for films and games.");
        menu.AddItem("Stereo", async () => await ApplySpatialAsync(false), @checked: spatial == false,
            tooltip: "Plain left and right, as recorded: good for music and calls.");
    }

    private static Task ApplySpatialAsync(bool spatial)
        => Privileged.SpatialAudio.ApplyFromMenuAsync(spatial, "Could not change the sound mode.",
            () => AppSettings.SpatialAudio = spatial);

    // ---- Touchscreen ----

    internal static void AddTouchscreen(IMenuBuilder menu)
    {
        if (!TouchscreenControl.IsSupported())
            return;

        var off = TouchscreenControl.IsDisabled() == true;
        menu.AddItem("Touchscreen", async () => await ApplyTouchscreenAsync(TouchscreenControl.IsDisabled() != true), @checked: !off,
            tooltip: "Turn the touchscreen on or off.", toggle: true);
    }

    private static Task ApplyTouchscreenAsync(bool off)
        => Privileged.TouchscreenOff.ApplyFromMenuAsync(off, "Could not change the touchscreen.");

    // ---- F10 / F11 ----

    internal static void BuildHotkeys(IMenuBuilder menu)
    {
        BuildKey(menu.AddSubMenu("F10"),
            () => AppSettings.F10Action, value => AppSettings.F10Action = value,
            () => AppSettings.F10Command, value => AppSettings.F10Command = value);
        BuildKey(menu.AddSubMenu("F11"),
            () => AppSettings.F11Action, value => AppSettings.F11Action = value,
            () => AppSettings.F11Command, value => AppSettings.F11Command = value);
    }

    private static void BuildKey(IMenuBuilder menu,
        Func<HotkeyAction> getAction, Action<HotkeyAction> setAction,
        Func<string?> getCommand, Action<string?> setCommand)
    {
        var current = getAction();
        foreach (var (action, text) in HotkeyActions.Choices)
            menu.AddItem(text, () => setAction(action), @checked: current == action);

        var command = getCommand();
        var customText = current == HotkeyAction.Custom && !string.IsNullOrEmpty(command)
            ? $"Program: {Path.GetFileName(command)}"
            : "Choose a program…";
        menu.AddItem(customText, () =>
        {
            if (CommonDialogs.AskForFile(User32.GetActiveWindow(), save: false, "Program to open",
                    "Programs (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*") is not { } program)
                return;
            setCommand(program);
            setAction(HotkeyAction.Custom);
        }, @checked: current == HotkeyAction.Custom);
    }
}
