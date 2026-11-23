namespace Art14Helper;

/// <summary>
/// The defaults, applied once on a fresh install (or after a Windows
/// reinstall). After that, whatever is picked in the menu is kept.
///
/// Charge limit Home, keyboard Auto with 30 s timeout, Fn standard F-keys,
/// F10/F11 nothing, touchpad: low vibration, edge gestures on, knuckle
/// screenshot on, low press sensitivity, spatial sound, refresh rate Auto, start with Windows.
/// Display color P3 with native white is set by ColorModeService once the graphics driver answers.
/// </summary>
internal static class FirstRunDefaults
{
    internal static async Task ApplyIfNeededAsync()
    {
        if (AppSettings.FirstRunDefaultsApplied)
            return;

        try
        {
            // Stored once, so the menu shows them even before the hardware answers. When a
            // hardware step below fails, the next start tries the hardware again with what
            // is stored by then, without resetting choices made in the meantime.
            if (!AppSettings.FirstRunDefaultsStored)
            {
                AppSettings.ChosenCharge = ChargeLimit.Home;
                AppSettings.ChosenLight = KeyboardLight.Auto;
                AppSettings.LightIdleSeconds = 30;
                AppSettings.FnMode = FnKeyMode.Standard;
                AppSettings.F10Action = HotkeyAction.Nothing;
                AppSettings.F11Action = HotkeyAction.Nothing;
                AppSettings.TouchpadVibration = TouchpadVibration.Soft;
                AppSettings.SetTouchpadEdge(TouchpadEdge.Brightness, true);
                AppSettings.SetTouchpadEdge(TouchpadEdge.Volume, true);
                AppSettings.KnuckleScreenshot = true;
                AppSettings.TouchpadPress = TouchpadPress.LessSensitive;
                AppSettings.SpatialAudio = true;
                AppSettings.ScreenRateAuto = true;   // the panel's fastest rate on the charger, 60 Hz on battery
                AppSettings.FirstRunDefaultsStored = true;
            }

            // Without admin rights the charge limit may show one UAC prompt; if it is
            // declined, the hardware steps are tried again on the next start.
            if (!await StoredSettings.ApplyAsync(AdminPrompt.UntilDeclined))
                return;
            // Respect the setup's choice: no autostart if it was switched off there.
            if (!Autostart.IsEnabled && AppSettings.SetupStartWithWindows != false)
                await Privileged.StartupEnabled.RunAsync(true, AdminPrompt.UntilDeclined);

            AppSettings.FirstRunDefaultsApplied = true;
            Log.Info("First-run defaults applied");
        }
        catch (Exception exception)
        {
            Log.Error("First-run defaults failed", exception);
        }
    }
}
