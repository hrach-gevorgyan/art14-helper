namespace Art14Helper;

// TrayApp: the tray icon, its tooltip and the sensor reads behind it.
internal sealed partial class TrayApp
{
    private const uint SensorInterval = 5_000;
    // Reading the sensors while the pointer rests on the icon stops after this many rounds.
    private const int HoverRounds = 6;
    // Moving over the icon sends a stream of events; the tooltip text costs registry and WMI reads.
    private const int TooltipInterval = 750;

    private readonly UiTimer _hoverTimer;
    private int _hoverRound;
    private Point _hoverPoint;
    private long _tooltipBuiltAt;
    private int _sensorReadRunning;
    // Starts in the past, so the first read right after boot isn't skipped.
    private long _sensorsReadAt = -SensorInterval;

    private void OnTrayMouse(uint mouseMessage)
    {
        if (_disposed)
            return;
        switch (mouseMessage)
        {
            case WindowMessage.MouseMove:
                _hoverPoint = User32.CursorPosition;
                if (!_hoverTimer.Running)
                {
                    _hoverRound = 0;
                    _hoverTimer.Start();
                }
                UpdateTooltip();
                _ = ReadSensorsAsync();
                break;
            case WindowMessage.LeftUp:
                ShowSettings();
                break;
            case WindowMessage.RightUp:
                ShowMenu();
                break;
        }
    }

    private void OnHoverTick()
    {
        if (_disposed || User32.CursorPosition != _hoverPoint || ++_hoverRound > HoverRounds)
        {
            _hoverTimer.Stop();
            return;
        }
        UpdateTooltip();
        _ = ReadSensorsAsync();
    }

    /// <summary>Builds the tooltip at most every <see cref="TooltipInterval"/> ms unless <paramref name="now"/>.</summary>
    private void UpdateTooltip(bool now = false)
    {
        var time = Environment.TickCount64;
        if (_disposed || (!now && time - _tooltipBuiltAt < TooltipInterval))
            return;
        _tooltipBuiltAt = time;
        _tray.Tip = StatusReport.ForTrayTooltip();
    }

    /// <summary>Reads temperatures and fans in the background, at most every <see cref="SensorInterval"/> ms.</summary>
    private async Task ReadSensorsAsync()
    {
        if (_disposed || Environment.TickCount64 - Interlocked.Read(ref _sensorsReadAt) < SensorInterval
            || Interlocked.Exchange(ref _sensorReadRunning, 1) != 0)
            return;
        Interlocked.Exchange(ref _sensorsReadAt, Environment.TickCount64);
        try
        {
            if (await Task.Run(Sensors.Read) is null)
                return;
            // A read can update stored settings, which the icon shows too.
            UiThread.Post(() =>
            {
                if (_disposed)
                    return;
                UpdateTooltip(now: true);
                UpdateIcon();
            });
        }
        catch (Exception exception)
        {
            Log.Error("Sensor read failed", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _sensorReadRunning, 0);
        }
    }

    /// <summary>The light or dark taskbar, or the icon size, may have changed.</summary>
    private void UpdateIcon() => _tray.Icon = TrayIcons.Create();
}
