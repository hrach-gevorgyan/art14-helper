namespace Art14Helper;

/// <summary>
/// Touchscreen on or off, the way PC Manager's Control Panel switch did it: by disabling the
/// "HID-compliant touch screen" device (pnputil). On the Art 14 that is the FocalTech touch
/// collection HID\FTSC1000&amp;COL01.
/// </summary>
internal static class TouchscreenControl
{
    private const int ProblemDisabled = 22;   // CM_PROB_DISABLED

    // Found once: the devices stay the same, and the menu asks for their state on every open.
    private static List<string>? _devices;

    internal static bool IsSupported() => Devices().Count > 0;

    /// <summary>true = touchscreen is off. null = not found.</summary>
    internal static bool? IsDisabled()
    {
        var devices = Devices();
        if (devices.Count == 0)
            return null;
        var problems = devices.Select(Native.Devices.ProblemOf).ToList();
        if (problems.Contains(null))
        {
            _devices = null;   // gone or renamed: look again next time
            return null;
        }
        return problems.All(problem => problem == ProblemDisabled);
    }

    internal static void SetDisabled(bool disabled)
    {
        var devices = Devices();
        if (devices.Count == 0)
            throw Shown(m => new InvalidOperationException(m), N("Touchscreen not found."));   // the menu shows the message
        foreach (var device in devices)
        {
            if ((Native.Devices.ProblemOf(device) == ProblemDisabled) == disabled)
                continue;
            var change = SystemTool.Run("pnputil.exe", disabled ? "/disable-device" : "/enable-device", device);
            if (change.ExitCode != 0)
                throw Shown(m => new InvalidOperationException(m), N("Windows didn't switch the touchscreen: {0}"), change.Tail());
        }
    }

    private static List<string> Devices()
    {
        if (_devices is { } known)
            return known;
        try
        {
            // Recognised by the English name, or by the Art 14's touch collection (COL01 is the touch screen).
            return _devices = Native.Devices.ListPresent()
                .Where(device => string.Equals(device.Class, "HIDClass", StringComparison.OrdinalIgnoreCase)
                    && (device.DisplayName.Contains("touch screen", StringComparison.OrdinalIgnoreCase)
                        || device.InstanceId.StartsWith(@"HID\FTSC1000&COL01", StringComparison.OrdinalIgnoreCase)))
                .Select(device => device.InstanceId)
                .ToList();
        }
        catch (Exception exception)
        {
            Log.Error("Touchscreen devices not listed", exception);
            return [];
        }
    }
}
