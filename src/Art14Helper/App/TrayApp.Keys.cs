using System.Diagnostics;

namespace Art14Helper;

// TrayApp: keys the BIOS reports as WMI events. They arrive on the WMI event thread, which
// every firmware event shares, so anything slow is moved to the thread pool.
internal sealed partial class TrayApp
{
    private void OnBiosKey(BiosKey key)
    {
        if (_disposed)
            return;
        switch (key)
        {
            case BiosKey.Microphone:
                // F7: the audio engine and the BIOS LED both take a moment.
                _ = Task.Run(() =>
                {
                    if (_micMute.Toggle() is { } muted)
                        AnnounceMicrophone(muted);
                });
                break;
            case BiosKey.Camera:
                // F8: a registry write, and on the first press the privileged task starting.
                _ = Task.Run(() =>
                {
                    if (CameraSwitch.Toggle() is { } off)
                        Popup.Show(off ? PopupIcon.CameraOff : PopupIcon.Camera, off ? T("Camera off") : T("Camera on"), off ? T("Apps see black") : null);
                });
                break;
            case BiosKey.ScreenSnip:
                _ = Task.Run(OpenScreenSnip);
                break;
            case BiosKey.F10:
                _ = Task.Run(() => HotkeyActions.Run(AppSettings.F10Action, AppSettings.F10Command));
                break;
            case BiosKey.F11:
                _ = Task.Run(() => HotkeyActions.Run(AppSettings.F11Action, AppSettings.F11Command));
                break;
        }
    }

    // The key's LED hides under the keyboard and silence is easy to miss, so say it on screen.
    private static void AnnounceMicrophone(bool muted)
    {
        if (muted)
            Popup.Show(PopupIcon.MicrophoneOff, T("Microphone off"), T("Muted for all apps"));
        else
            Popup.Show(PopupIcon.Microphone, T("Microphone on"));
    }

    // The snip key: the same area selection as Win+Shift+S.
    private static void OpenScreenSnip()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Error("Screen snip not opened", exception);
        }
    }

    // Fn+Space: the firmware changed the light itself and reports the new level.
    private void OnLightChanged(KeyboardLight level)
    {
        if (_disposed)
            return;
        // Stored first: the Auto service decides from the stored mode (a mode change also
        // resets the firmware's Auto brightness, so it must send its level again).
        KeyboardLights.FirmwareChanged();
        AppSettings.CurrentLight = level;
        AppSettings.ChosenLight = level;
        _autoBacklight.Refresh();
        // Dim and Bright as a level bar, like the brightness flyout; Off and Auto in words.
        if (level is KeyboardLight.Dim or KeyboardLight.Bright)
            Popup.Show(PopupIcon.KeyboardLight, T("Keyboard light"), level: level == KeyboardLight.Dim ? 0.5f : 1f);
        else
            Popup.Show(level == KeyboardLight.Off ? PopupIcon.KeyboardLightOff : PopupIcon.KeyboardLight, T("Keyboard light"), level == KeyboardLight.Off ? T("Off") : T("Auto"));
        RefreshSettingsSoon();
    }

    // The magnetic camera was put on or taken off. Put back while the F8 privacy switch is
    // on, apps get black frames, which looks like a broken camera: say so.
    private void OnCameraDocked(bool attached)
    {
        if (_disposed)
            return;
        Log.Info(attached ? "Camera attached" : "Camera removed");
        if (!attached)
        {
            Popup.Show(PopupIcon.CameraOff, T("Camera removed"));
            return;
        }
        _ = Task.Run(() =>   // a registry read, off the WMI event thread
        {
            if (CameraSwitch.IsOff())
                Popup.Show(PopupIcon.CameraOff, T("Camera attached"), T("Privacy is on: press F8"));
            else
                Popup.Show(PopupIcon.Camera, T("Camera attached"));
        });
    }

    // The firmware switches the touchpad itself; the event carries the result.
    private void OnTouchpadSwitched(bool enabled) => Popup.Show(enabled ? PopupIcon.Touchpad : PopupIcon.TouchpadOff, enabled ? T("Touchpad on") : T("Touchpad off"));
}
