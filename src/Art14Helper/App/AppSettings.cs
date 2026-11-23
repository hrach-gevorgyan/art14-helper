using Win32 = Microsoft.Win32;

namespace Art14Helper;

/// <summary>
/// The app's stored state in HKCU\Software\Art14Helper. From 1.0 on the value names
/// never change, so an update keeps every choice. Enums and text are stored as strings,
/// numbers and on/off flags as DWORDs; a missing value reads as null (never chosen).
/// </summary>
/// <remarks>
/// One open key serves the whole run, since a menu refresh reads many values. Reads
/// aren't cached, so a change made by a UAC-launched copy shows up immediately. A lock
/// guards the key because several threads use it.
/// </remarks>
internal static class AppSettings
{
    private const string KeyPath = @"Software\Art14Helper";


    private static readonly Lock KeyLock = new();
    private static Win32.RegistryKey? _openKey;

    // Keyboard light. Current = last mode read from the firmware; Chosen = what to put
    // back after a restart or sleep; IdleSeconds = auto-off delay (0 = stay on, default 60).
    internal static KeyboardLight? CurrentLight { get => GetEnum<KeyboardLight>("Light.Current"); set => SetEnum("Light.Current", value); }
    internal static KeyboardLight? ChosenLight { get => GetEnum<KeyboardLight>("Light.Chosen"); set => SetEnum("Light.Chosen", value); }
    // A level made from Auto mode on firmware that refuses fixed levels (see KeyboardLights.Set).
    internal static KeyboardLight? EmulatedLight { get => GetEnum<KeyboardLight>("Light.Emulated"); set => SetEnum("Light.Emulated", value); }
    internal static ushort LightIdleSeconds { get => (ushort)(GetNumber("Light.IdleSeconds") is { } s and >= 0 and <= ushort.MaxValue && Array.IndexOf(KeyboardLights.IdleChoices, (ushort)s) >= 0 ? s : 60); set => SetNumber("Light.IdleSeconds", value); }

    // Charge limit. Current = last mode read from the EC; Chosen = what to put back when
    // the EC loses it.
    internal static ChargeLimit? CurrentCharge { get => GetEnum<ChargeLimit>("Charge.Current"); set => SetEnum("Charge.Current", value); }
    internal static ChargeLimit? ChosenCharge { get => GetEnum<ChargeLimit>("Charge.Chosen"); set => SetEnum("Charge.Chosen", value); }
    // Charging to 100% this one time; the chosen limit comes back at the next unplug.
    internal static bool ChargeOnce { get => GetFlag("Charge.Once") ?? false; set => SetFlag("Charge.Once", value ? true : null); }
    // The charger was connected while "100% once" was on: only then does starting on battery end it.
    internal static bool ChargeOnceSawCharger { get => GetFlag("Charge.OnceSawCharger") ?? false; set => SetFlag("Charge.OnceSawCharger", value ? true : null); }

    // Keys: Fn lock mode, plus the actions bound to F10 (event 0x28A) and F11 (0x28B).
    internal static FnKeyMode? FnMode { get => GetEnum<FnKeyMode>("Keys.FnMode"); set => SetEnum("Keys.FnMode", value); }
    internal static HotkeyAction F10Action { get => GetEnum<HotkeyAction>("Keys.F10") ?? HotkeyAction.Nothing; set => SetEnum<HotkeyAction>("Keys.F10", value); }
    internal static string? F10Command { get => GetText("Keys.F10Command"); set => SetText("Keys.F10Command", value); }
    internal static HotkeyAction F11Action { get => GetEnum<HotkeyAction>("Keys.F11") ?? HotkeyAction.Nothing; set => SetEnum<HotkeyAction>("Keys.F11", value); }
    internal static string? F11Command { get => GetText("Keys.F11Command"); set => SetText("Keys.F11Command", value); }

    // Touchpad. Edge gestures without a stored value are left at the firmware default.
    internal static TouchpadVibration? TouchpadVibration { get => GetEnum<TouchpadVibration>("Touchpad.Vibration"); set => SetEnum("Touchpad.Vibration", value); }
    internal static TouchpadPress? TouchpadPress { get => GetEnum<TouchpadPress>("Touchpad.PressSensitivity"); set => SetEnum("Touchpad.PressSensitivity", value); }
    internal static bool? KnuckleScreenshot { get => GetFlag("Touchpad.KnuckleScreenshot"); set => SetFlag("Touchpad.KnuckleScreenshot", value); }
    internal static bool? GetTouchpadEdge(TouchpadEdge edge) => GetFlag("Touchpad.Edge." + edge);
    internal static void SetTouchpadEdge(TouchpadEdge edge, bool? on) => SetFlag("Touchpad.Edge." + edge, on);

    // Display: a fixed refresh rate to reapply (Windows drops it on restart and sleep) or
    // automatic switching by power source; the color mode; and NativeWhite, which makes
    // calibrated modes use the panel's white point rather than D65 (default on).
    internal static int? ScreenRefreshRate { get => GetNumber("Display.RefreshRate"); set => SetNumber("Display.RefreshRate", value); }
    internal static bool ScreenRateAuto { get => GetFlag("Display.AutoRate") ?? false; set => SetFlag("Display.AutoRate", value); }
    internal static ColorMode? ColorMode
    {
        get => GetNumber("Display.ColorMode") is { } mode && Enum.IsDefined((ColorMode)mode) ? (ColorMode)mode : null;
        set => SetNumber("Display.ColorMode", (int?)value);
    }
    internal static bool ColorNativeWhite { get => GetFlag("Display.NativeWhite") ?? true; set => SetFlag("Display.NativeWhite", value); }

    // Keep awake: whether the screen stays on too, and the F15 presence taps; both off by default.
    internal static bool KeepScreenOn { get => GetFlag("Awake.Screen") ?? false; set => SetFlag("Awake.Screen", value); }
    internal static bool KeepAppsActive { get => GetFlag("Awake.Apps") ?? false; set => SetFlag("Awake.Apps", value); }
    // Keep awake while it runs: when it ends (UTC, or "until turned off") and the minutes chosen
    // (0 = until turned off), so a restart of the app goes on with it. Missing while it's off.
    internal static string? KeepAwakeUntil { get => GetText("Awake.Until"); set => SetText("Awake.Until", value); }
    internal static int? KeepAwakeMinutes { get => GetNumber("Awake.Minutes"); set => SetNumber("Awake.Minutes", value); }
    // Remote mode, and which lid actions it changed (to put back only those); the lid action
    // before "keep running with the lid closed" was turned on, per power source.
    internal static bool RemoteMode { get => GetFlag("Awake.Remote") ?? false; set => SetFlag("Awake.Remote", value ? true : null); }
    internal static bool GetRemoteChangedLid(bool onBattery) => GetFlag(PerSource("Awake.RemoteLid", onBattery)) ?? false;
    internal static void SetRemoteChangedLid(bool onBattery, bool changed) => SetFlag(PerSource("Awake.RemoteLid", onBattery), changed ? true : null);
    internal static LidAction? GetLidBefore(bool onBattery)
        => GetNumber(PerSource("Lid.Before", onBattery)) is { } value && Enum.IsDefined((LidAction)value) ? (LidAction)value : null;
    internal static void SetLidBefore(bool onBattery, LidAction? action) => SetNumber(PerSource("Lid.Before", onBattery), (int?)action);
    // The power plan that change was made in, so it's put back there even if another plan is active by then.
    internal static Guid? GetLidPlan(bool onBattery) => Guid.TryParse(GetText(PerSource("Lid.Plan", onBattery)), out var plan) ? plan : null;
    internal static void SetLidPlan(bool onBattery, Guid? plan) => SetText(PerSource("Lid.Plan", onBattery), plan?.ToString());
    private static string PerSource(string name, bool onBattery) => name + (onBattery ? "Battery" : "Charger");

    // Sound: true = spatial output, false = plain stereo.
    internal static bool? SpatialAudio { get => GetFlag("Sound.Spatial"); set => SetFlag("Sound.Spatial", value); }

    // Setup: the autostart answer given during install, and whether defaults were applied.
    internal static bool? SetupStartWithWindows { get => GetFlag("Setup.StartWithWindows"); set => SetFlag("Setup.StartWithWindows", value); }
    internal static bool FirstRunDefaultsApplied { get => GetFlag("Setup.DefaultsApplied") ?? false; set => SetFlag("Setup.DefaultsApplied", value); }
    /// <summary>The defaults are stored (once); the hardware may still be waiting for them.</summary>
    internal static bool FirstRunDefaultsStored { get => GetFlag("Setup.DefaultsStored") ?? false; set => SetFlag("Setup.DefaultsStored", value); }

    // ---- Backup ----

    /// <summary>Every stored value by name: text as string, numbers and flags as int.</summary>
    internal static Dictionary<string, object> All() => WithKey(key =>
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is { } value and (string or int))
                values[name] = value;
        return values;
    });

    /// <summary>Writes values read from a backup: text as REG_SZ, numbers as DWORD.</summary>
    internal static void Store(IReadOnlyDictionary<string, object> values)
    {
        foreach (var (name, value) in values)
            Store(name, value, value is int ? Win32.RegistryValueKind.DWord : Win32.RegistryValueKind.String);
    }

    // ---- Storage ----

    private static T WithKey<T>(Func<Win32.RegistryKey, T> work)
    {
        lock (KeyLock)
        {
            _openKey ??= Win32.Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            return work(_openKey);
        }
    }

    private static void Store(string name, object? value, Win32.RegistryValueKind kind) => WithKey(key =>
    {
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value, kind);
        return true;
    });

    private static string? GetText(string name) => WithKey(key => key.GetValue(name) as string);
    private static void SetText(string name, string? value) => Store(name, value, Win32.RegistryValueKind.String);
    private static int? GetNumber(string name) => WithKey(key => key.GetValue(name) as int?);
    private static void SetNumber(string name, int? value) => Store(name, value, Win32.RegistryValueKind.DWord);
    private static bool? GetFlag(string name) => GetNumber(name) is { } number ? number != 0 : null;
    private static void SetFlag(string name, bool? value) => SetNumber(name, value is null ? null : value.Value ? 1 : 0);
    private static T? GetEnum<T>(string name) where T : struct, Enum => EnumText.TryParse<T>(GetText(name), out var parsed) ? parsed : null;
    private static void SetEnum<T>(string name, T? value) where T : struct, Enum => SetText(name, value?.ToString());
}
