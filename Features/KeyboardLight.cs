namespace Art14Helper;

/// <summary>Keyboard light modes; Auto follows the ambient light sensor.</summary>
internal enum KeyboardLight
{
    Off,
    Dim,
    Bright,
    Auto
}

/// <summary>Keyboard light through the BIOS: mode, idle timeout and the brightness used in Auto.</summary>
internal static class KeyboardLights
{
    private const byte Group = 0x06;
    private const byte SetModeCommand = 0x14, SetAutoBrightnessCommand = 0x15, SetTimeoutCommand = 0x11, ReadModeCommand = 0x13;

    /// <summary>The firmware's byte for each mode.</summary>
    private static readonly (byte Code, KeyboardLight Light)[] Modes =
        [(0x02, KeyboardLight.Off), (0x03, KeyboardLight.Dim), (0x04, KeyboardLight.Bright), (0x10, KeyboardLight.Auto)];

    internal static KeyboardLight? FromModeByte(int? code)
    {
        foreach (var (byteValue, light) in Modes)
            if (byteValue == code)
                return light;
        return null;
    }

    internal static byte ModeByte(KeyboardLight light) => Modes.First(pair => pair.Light == light).Code;

    // The level made from Auto mode on firmware that refuses fixed levels (see Set); null
    // when the firmware is in the mode it reports. Kept in the settings, so it survives an
    // app restart while the firmware stays in that Auto mode.
    private static KeyboardLight? Emulated
    {
        get => AppSettings.EmulatedLight;
        set
        {
            if (AppSettings.EmulatedLight != value)
                AppSettings.EmulatedLight = value;
        }
    }

    /// <summary>
    /// The mode as the user chose it: Auto mode standing in for a fixed level still counts as
    /// that level, so readings don't turn Dim into Auto.
    /// </summary>
    internal static KeyboardLight? Effective(KeyboardLight? read)
        => Effective(read, Emulated);

    internal static KeyboardLight? Effective(KeyboardLight? read, KeyboardLight? emulated)
        => read == KeyboardLight.Auto && emulated is { } level ? level : read;

    /// <summary>The firmware changed the mode itself (Fn+Space): what it reports is real again.</summary>
    internal static void FirmwareChanged() => Emulated = null;

    private static byte EmulatedPercent(KeyboardLight light) => light switch { KeyboardLight.Dim => 50, KeyboardLight.Bright => 100, _ => 0 };

    /// <summary>
    /// Sets a mode. Some firmware answers status 1 to a fixed level; then the same look is
    /// made from Auto mode with a fixed Auto brightness (0, 50 or 100%).
    /// </summary>
    internal static void Set(KeyboardLight light)
    {
        try
        {
            Bios.Run(Bios.Command(Group, SetModeCommand, ModeByte(light)));
            Emulated = null;
        }
        catch (BiosRefusedException refused) when (refused.Status == 1 && light != KeyboardLight.Auto)
        {
            Bios.Run(Bios.Command(Group, SetModeCommand, ModeByte(KeyboardLight.Auto)));
            Thread.Sleep(10);
            SetAutoBrightness(EmulatedPercent(light));
            Emulated = light;
        }
    }

    /// <summary>The timeouts HONOR's own software offers; nothing else goes to the BIOS.</summary>
    internal static readonly ushort[] IdleChoices = [0, 15, 30, 60, 300];

    /// <summary>Seconds without typing before the light goes out; 0 keeps it on.</summary>
    internal static void SetIdleTimeout(ushort seconds)
    {
        // The stored value is in HKCU, which any program of the user can change.
        if (Array.IndexOf(IdleChoices, seconds) < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Not a keyboard light timeout the laptop offers.");
        Bios.Run(TimeoutCommand(seconds));
    }

    /// <summary>Mode and timeout together, as the restore after wake needs them.</summary>
    internal static void SetWithTimeout(KeyboardLight light, ushort seconds)
    {
        Set(light);
        SetIdleTimeout(seconds);
    }

    /// <summary>Brightness while in Auto, 0-100%.</summary>
    internal static void SetAutoBrightness(byte percent)
        => Bios.Run(Bios.Command(Group, SetAutoBrightnessCommand, Math.Min(percent, (byte)100)));

    internal static bool TrySetAutoBrightness(byte percent)
        => Bios.TryRun(Bios.Command(Group, SetAutoBrightnessCommand, Math.Min(percent, (byte)100)));

    /// <summary>The mode the firmware is in now, or null if the BIOS didn't answer.</summary>
    internal static KeyboardLight? ReadMode()
        => Bios.TryRun(Bios.Command(Group, ReadModeCommand), out var reply) ? FromModeByte(Bios.Byte(reply, 1)) : null;

    /// <summary>Mode and timeout without exceptions or the status-1 workaround; false if either failed.</summary>
    private static bool TrySetWithTimeout(KeyboardLight light, ushort seconds)
    {
        if (!Bios.TryRun(TimeoutCommand(seconds)) || !Bios.TryRun(Bios.Command(Group, SetModeCommand, ModeByte(light))))
            return false;
        Emulated = null;
        return true;
    }

    private static ulong TimeoutCommand(ushort seconds)
        => Bios.Command(Group, SetTimeoutCommand, (byte)seconds, (byte)(seconds >> 8));

    /// <summary>
    /// Puts the chosen mode back after startup and wake: the firmware may come back in
    /// another one. Three checks, 2 s apart; a mode that's already right is left alone,
    /// because writing it again restarts Auto and makes the keys flash.
    /// </summary>
    internal static SettleWatch CreateRestore(Action onRestored) => new("Keyboard light restore", 3, TimeSpan.FromSeconds(2), async () =>
    {
        if (AppSettings.ChosenLight is not { } light)
            return;
        if (Effective(ReadMode()) == light)
        {
            // Right already. A level made from Auto may have lost its brightness in sleep:
            // that alone is sent again (no mode write, so no flash).
            if (Emulated is { } level)
                TrySetAutoBrightness(EmulatedPercent(level));
            AppSettings.CurrentLight = light;
            return;
        }
        var seconds = AppSettings.LightIdleSeconds;
        // The quiet path first; the full one also knows the status-1 workaround.
        if (TrySetWithTimeout(light, seconds) || await Privileged.KeyboardLightState.RunAsync((light, seconds), AdminPrompt.Never))
        {
            AppSettings.CurrentLight = light;
            onRestored();
        }
    });
}

/// <summary>The keyboard light choices for the tray menu and the settings window.</summary>
internal static class KeyboardLightMenu
{
    private static readonly (KeyboardLight Light, string Text)[] Brightness =
        [(KeyboardLight.Off, "Off"), (KeyboardLight.Dim, "Dim"), (KeyboardLight.Bright, "Bright"), (KeyboardLight.Auto, "Auto")];

    private static readonly (ushort Seconds, string Text)[] IdleTimes =
        [(0, "Stay on"), (15, "After 15 s"), (30, "After 30 s"), (60, "After 1 min"), (300, "After 5 min")];

    internal static void Build(IMenuBuilder menu)
    {
        menu.AddItem("Brightness", null, enabled: false);
        var light = AppSettings.CurrentLight;
        foreach (var (value, text) in Brightness)
            menu.AddItem(text, async () => await ChooseLightAsync(value), @checked: light == value,
                tooltip: value == KeyboardLight.Auto ? "Follows the room's light: off in daylight, brighter as it gets darker." : null);

        menu.AddSeparator();
        menu.AddItem("Turn off when idle", null, enabled: false);
        var idle = AppSettings.LightIdleSeconds;
        foreach (var (seconds, text) in IdleTimes)
            menu.AddItem(text, async () => await ChooseIdleAsync(seconds), @checked: idle == seconds);
    }

    private static Task ChooseLightAsync(KeyboardLight light)
        => Privileged.KeyboardLight.ApplyFromMenuAsync(light, "The keyboard light wasn't changed.", () =>
        {
            AppSettings.CurrentLight = light;
            AppSettings.ChosenLight = light;
            AutoBacklightService.NotifyModeChanged();
        });

    private static Task ChooseIdleAsync(ushort seconds)
        => Privileged.KeyboardLightTimeout.ApplyFromMenuAsync(seconds, "The idle timeout wasn't changed.",
            () => AppSettings.LightIdleSeconds = seconds);
}
