namespace Art14Helper;

internal enum TouchpadVibration : byte
{
    Soft = 0,
    Medium = 1,
    Strong = 2
}

internal enum TouchpadPress : byte
{
    LessSensitive = 0,
    MoreSensitive = 1
}

/// <summary>Edge swipes; each value is the firmware switch that turns its reports on or off.</summary>
internal enum TouchpadEdge : byte
{
    Brightness = TouchpadCommand.BrightnessEdge,   // left edge
    Volume = TouchpadCommand.VolumeEdge            // right edge
}

/// <summary>
/// The touchpad's firmware settings. The firmware forgets all of them when it reconnects
/// or the laptop sleeps, so the stored choices are written again at startup, after wake
/// and after a reconnect (<see cref="Reapply"/>). Writing needs no admin rights.
/// </summary>
internal static class TouchpadSettings
{
    internal static void SetVibration(TouchpadVibration strength)
    {
        TouchpadLink.Send(TouchpadCommand.Vibration, (byte)strength);
        AppSettings.TouchpadVibration = strength;
    }

    internal static void SetPress(TouchpadPress press)
    {
        TouchpadLink.Send(TouchpadCommand.PressSensitivity, (byte)press);
        AppSettings.TouchpadPress = press;
    }

    /// <summary>
    /// Turns an edge swipe's reports on or off. The firmware only reports the swipe (it
    /// changes nothing itself), and <see cref="TouchpadEvents"/> acts on it.
    /// </summary>
    internal static void SetEdge(TouchpadEdge edge, bool on)
    {
        TouchpadLink.Send((TouchpadCommand)edge, On(on));
        AppSettings.SetTouchpadEdge(edge, on);
    }

    internal static void SetKnuckleScreenshot(bool on)
    {
        TouchpadLink.Send(TouchpadCommand.KnuckleScreenshot, On(on));
        AppSettings.KnuckleScreenshot = on;
    }

    /// <summary>On unless the user turned it off (the firmware's own default is on).</summary>
    internal static bool IsEdgeOn(TouchpadEdge edge) => AppSettings.GetTouchpadEdge(edge) ?? true;

    /// <summary>Writes every stored choice back to the firmware; choices never made are left alone.</summary>
    internal static void Reapply()
    {
        if (!TouchpadLink.IsPresent)
            return;
        var writes = new List<(TouchpadCommand, byte)>();
        if (AppSettings.TouchpadVibration is { } strength)
            writes.Add((TouchpadCommand.Vibration, (byte)strength));
        if (AppSettings.TouchpadPress is { } press)
            writes.Add((TouchpadCommand.PressSensitivity, (byte)press));
        foreach (var edge in Enum.GetValues<TouchpadEdge>())
            if (AppSettings.GetTouchpadEdge(edge) is { } on)
                writes.Add(((TouchpadCommand)edge, On(on)));
        if (AppSettings.KnuckleScreenshot is { } knuckle)
            writes.Add((TouchpadCommand.KnuckleScreenshot, On(knuckle)));

        foreach (var (command, value) in writes)
        {
            try
            {
                TouchpadLink.Send(command, value);
            }
            catch (Exception exception)
            {
                Log.Error($"Touchpad setting {command} not restored", exception);
            }
        }
    }

    private static byte On(bool on) => on ? (byte)1 : (byte)0;
}

/// <summary>The touchpad menu for the tray and the settings window.</summary>
internal static class TouchpadSettingsMenu
{
    internal static void Build(IMenuBuilder menu)
    {
        menu.AddItem(T("Vibration"), null, enabled: false);
        var strength = AppSettings.TouchpadVibration;
        foreach (var (value, text) in new[] { (TouchpadVibration.Soft, N("Soft")), (TouchpadVibration.Medium, N("Medium")), (TouchpadVibration.Strong, N("Strong")) })
            menu.AddItem(T(text), () => Change(() => TouchpadSettings.SetVibration(value)), @checked: strength == value);

        menu.AddSeparator();
        menu.AddItem(T("Press"), null, enabled: false);
        var press = AppSettings.TouchpadPress;
        menu.AddItem(T("Less sensitive"), () => Change(() => TouchpadSettings.SetPress(TouchpadPress.LessSensitive)),
            @checked: press == TouchpadPress.LessSensitive, tooltip: T("A click needs a firmer press."));
        menu.AddItem(T("More sensitive"), () => Change(() => TouchpadSettings.SetPress(TouchpadPress.MoreSensitive)),
            @checked: press == TouchpadPress.MoreSensitive, tooltip: T("A light press is enough to click."));

        menu.AddSeparator();
        menu.AddItem(T("Gestures"), null, enabled: false);
        AddEdge(menu, TouchpadEdge.Brightness, T("Brightness on the left edge"), T("Slide one finger up or down along the left edge."));
        AddEdge(menu, TouchpadEdge.Volume, T("Volume on the right edge"), T("Slide one finger up or down along the right edge."));
        var knuckle = AppSettings.KnuckleScreenshot == true;
        menu.AddItem(T("Knuckle double-tap takes a screenshot"), () => Change(() => TouchpadSettings.SetKnuckleScreenshot(AppSettings.KnuckleScreenshot != true)),
            @checked: knuckle, toggle: true,
            tooltip: T("Knock twice on the touchpad with a knuckle; the whole screen is saved to Pictures\\Screenshots."));
    }

    private static void AddEdge(IMenuBuilder menu, TouchpadEdge edge, string text, string hint)
    {
        var on = TouchpadSettings.IsEdgeOn(edge);
        menu.AddItem(text, () => Change(() => TouchpadSettings.SetEdge(edge, !TouchpadSettings.IsEdgeOn(edge))), @checked: on, tooltip: hint, toggle: true);
    }

    private static void Change(Action write)
    {
        try
        {
            write();
        }
        catch (Exception exception)
        {
            Log.Error("Touchpad setting not changed", exception);
            CommonDialogs.Message(User32.GetActiveWindow(), exception.Message, AppInfo.Name, CommonDialogs.Icon.Error);
        }
    }
}
