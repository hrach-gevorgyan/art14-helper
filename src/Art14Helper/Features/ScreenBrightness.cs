namespace Art14Helper;

/// <summary>Brightness steps for the touchpad's left-edge swipe.</summary>
internal static class ScreenBrightness
{
    /// <summary>
    /// One step up or down. The BIOS step comes first: the firmware picks the step size and
    /// Windows shows its brightness flyout. Without it (no admin rights or no HONOR BIOS)
    /// the monitor's WMI brightness is changed by <paramref name="fallbackPercent"/>,
    /// which works but shows no flyout.
    /// </summary>
    internal static void Step(bool up, int fallbackPercent)
    {
        if (Bios.TryRun(Bios.Command(0x06, 0x35, up ? (byte)0 : (byte)1)))
            return;
        ChangeBy(up ? fallbackPercent : -fallbackPercent);
    }

    private static void ChangeBy(int delta)
    {
        const string Scope = @"root\WMI";
        var now = Wmi.FirstText("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True", "CurrentBrightness", Scope);
        if (!int.TryParse(now, out var percent))
            return;   // no internal panel that reports brightness
        var target = (byte)Math.Clamp(percent + delta, 0, 100);
        Wmi.CallOnEach(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True", "WmiSetBrightness",
            new WmiArgument("Timeout", 0), new WmiArgument("Brightness", target, IsByte: true));
    }
}
