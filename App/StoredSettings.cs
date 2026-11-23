namespace Art14Helper;

/// <summary>
/// Puts the stored choices on the hardware: after the first-run defaults are stored, and
/// after a settings backup is restored. Each step on its own, so one failing doesn't skip
/// the rest; the "current" values are stored only once the hardware took them.
/// </summary>
internal static class StoredSettings
{
    /// <summary>
    /// False when a step failed, or when the charge limit (the first step that needs admin
    /// rights, which may ask through UAC per <paramref name="chargePrompt"/>) wasn't applied:
    /// then the remaining admin steps are skipped too.
    /// </summary>
    internal static async Task<bool> ApplyAsync(AdminPrompt chargePrompt)
    {
        var allApplied = true;

        async Task Step(string name, Func<Task> work)
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                allApplied = false;
                Log.Error($"Stored setting not applied: {name}", exception);
            }
        }

        // No admin rights needed for these.
        await Step("touchpad", () => Task.Run(TouchpadSettings.Reapply));
        await Step("refresh rate", () => Task.Run(() => RefreshRate.Restore()));

        var charge = AppSettings.ChosenCharge ?? ChargeLimit.Home;
        var chargeApplied = false;
        await Step("charge limit", async () =>
        {
            if (chargeApplied = await Privileged.ChargeLimit.RunAsync(charge, chargePrompt).ConfigureAwait(false))
                AppSettings.CurrentCharge = charge;
        });
        if (!chargeApplied)
            return false;   // declined or refused: the others would fail the same way

        await Step("keyboard light", async () =>
        {
            // Stored as current only once the keyboard really is in that mode, or the Auto
            // service would send levels to a keyboard in another mode.
            var light = AppSettings.ChosenLight ?? KeyboardLight.Auto;
            if (await Privileged.KeyboardLightState.RunAsync((light, AppSettings.LightIdleSeconds), AdminPrompt.Never).ConfigureAwait(false))
            {
                AppSettings.CurrentLight = light;
                AutoBacklightService.NotifyModeChanged();
            }
        });
        await Step("Fn mode", () => Privileged.FnMode.RunAsync(AppSettings.FnMode ?? FnKeyMode.Standard, AdminPrompt.Never));
        await Step("sound", async () =>
        {
            if (SpatialAudioController.IsSupported())
                await Privileged.SpatialAudio.RunAsync(AppSettings.SpatialAudio != false, AdminPrompt.Never).ConfigureAwait(false);
        });
        return allApplied;
    }
}
