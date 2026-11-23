namespace Art14Helper;

/// <summary>
/// Reads the touchpad's gesture reports and acts on them: left-edge swipe = screen
/// brightness, right-edge swipe = volume, knuckle double-tap = screenshot. The firmware
/// only reports these (PC Manager did the rest), and only for the gestures switched on
/// in <see cref="TouchpadSettings"/>.
/// </summary>
internal sealed class TouchpadEvents(Action<string> reportProblem) : IDisposable
{
    /// <summary>A swipe sends a stream of reports; one step per this many milliseconds is plenty.</summary>
    private const int StepIntervalMilliseconds = 150;

    /// <summary>Brightness change per step when the BIOS step isn't available; small, so a swipe feels smooth.</summary>
    private const int FallbackBrightnessPercent = 3;

    private readonly Lock _lifecycle = new();
    private readonly Lock _stepLock = new();
    private CancellationTokenSource _readers = new();
    private (TouchpadEvent Event, bool Up, long At) _lastStep;
    private bool _disposed;

    /// <summary>A parsed gesture report.</summary>
    internal readonly record struct Gesture(TouchpadEvent Event, bool Up);

    /// <summary>
    /// The gesture in an input report, or null for anything else (other report ids,
    /// unknown events, a swipe without a valid direction).
    /// </summary>
    internal static Gesture? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3 || report[0] != TouchpadLink.ReportId)
            return null;
        var kind = (TouchpadEvent)report[1];
        return kind switch
        {
            TouchpadEvent.KnuckleDoubleTap => new Gesture(kind, false),
            TouchpadEvent.BrightnessSwipe or TouchpadEvent.VolumeSwipe when report[2] is 1 or 2 => new Gesture(kind, report[2] == 1),
            _ => null
        };
    }

    internal void Start()
    {
        lock (_lifecycle)
        {
            if (!_disposed && !StartReaders(_readers.Token))
                reportProblem("No HONOR touchpad found: edge gestures won't work.");
        }
    }

    /// <summary>After the touchpad reconnects (sleep, driver update) its old handles are dead: start over.</summary>
    internal void Restart()
    {
        lock (_lifecycle)
        {
            if (_disposed)
                return;
            _readers.Cancel();
            _readers.Dispose();
            _readers = new CancellationTokenSource();
            if (StartReaders(_readers.Token))
                Log.Info("Touchpad readers started again after a device change");
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
                return;
            _disposed = true;
            _readers.Cancel();
            _readers.Dispose();
        }
    }

    private bool StartReaders(CancellationToken stop)
    {
        try
        {
            var sources = Hid.ListPresent().Where(TouchpadLink.IsEventSource).ToList();
            foreach (var source in sources)
                _ = ReadAsync(source, stop);
            return sources.Count > 0;
        }
        catch (Exception exception)
        {
            reportProblem($"The touchpad couldn't be opened: {exception.Message}");
            return false;
        }
    }

    private async Task ReadAsync(HidInterface source, CancellationToken stop)
    {
        using var handle = source.OpenForReading();
        if (handle.IsInvalid)
            return;
        var size = Math.Max((int)source.InputReportLength, 64);
        try
        {
            // ConfigureAwait(false): gestures are handled on the pool even when the readers were
            // restarted from the UI thread (after a device change).
            var reports = new FileStream(handle, FileAccess.Read, size, isAsync: true);
            await using var _ = reports.ConfigureAwait(false);
            var report = new byte[size];
            while (!stop.IsCancellationRequested)
            {
                var length = await reports.ReadAsync(report, stop).ConfigureAwait(false);
                if (length == 0)
                    return;   // end of stream: the device is gone
                if (Parse(report.AsSpan(0, length)) is { } gesture)
                    Handle(gesture);
            }
        }
        catch (Exception) when (stop.IsCancellationRequested)
        {
            // Stopped on purpose.
        }
        catch (Exception exception)
        {
            // The device went away; the plug/unplug notification starts a new reader.
            Log.Error($"Touchpad reader ended: {exception.Message}");
        }
    }

    private void Handle(Gesture gesture)
    {
        switch (gesture.Event)
        {
            case TouchpadEvent.KnuckleDoubleTap:
                if (AppSettings.KnuckleScreenshot == true)
                    KnuckleScreenshot.Take();
                break;
            case TouchpadEvent.VolumeSwipe:
                if (IsNextStep(gesture) && TouchpadSettings.IsEdgeOn(TouchpadEdge.Volume))
                    MediaKeys.Volume(gesture.Up);   // the volume key also shows the Windows flyout
                break;
            case TouchpadEvent.BrightnessSwipe:
                if (!IsNextStep(gesture) || !TouchpadSettings.IsEdgeOn(TouchpadEdge.Brightness))
                    break;
                try
                {
                    ScreenBrightness.Step(gesture.Up, FallbackBrightnessPercent);
                }
                catch (Exception exception)
                {
                    reportProblem($"Brightness not changed: {exception.Message}");
                }
                break;
        }
    }

    /// <summary>False for a repeat of the same swipe direction within the step interval.</summary>
    private bool IsNextStep(Gesture gesture)
    {
        lock (_stepLock)
        {
            var now = Environment.TickCount64;
            if (_lastStep.Event == gesture.Event && _lastStep.Up == gesture.Up && now - _lastStep.At < StepIntervalMilliseconds)
                return false;
            _lastStep = (gesture.Event, gesture.Up, now);
            return true;
        }
    }
}
