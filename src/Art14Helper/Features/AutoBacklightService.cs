namespace Art14Helper;

/// <summary>
/// Keyboard light "Auto". In Auto mode (BIOS 0x1406 = 0x10) the firmware does not
/// pick a brightness: PC Manager's service read the ambient light sensor and sent
/// the level with BIOS 0x1506 (0-100). This class does that job.
///
/// Design (modelled on Windows 11 adaptive brightness):
/// - Overlapping light buckets. Readings inside the current bucket change nothing,
///   and a bucket is left only when the light is clearly outside it. This is the
///   hysteresis that stops flicker at a border.
/// - Dwell time. A new bucket must hold for a moment before it's used, so a hand
///   or shadow passing over the sensor does nothing. Lighting the keys up (room got
///   dark) reacts faster than dimming them (room got bright).
/// - Fade. Level changes ramp over about half a second instead of jumping.
/// - Battery. Nothing runs unless the keyboard is in Auto and the screen is on. Then the
///   sensor is read every <see cref="PollMilliseconds"/> (one quick call). This laptop's
///   driver can't report only real changes: with a change sensitivity it reports nothing at
///   all (so Auto used to follow the room only at start and wake), without one it reports
///   five times a second. Levels go through the direct ACPI path (one WMI call, no extra
///   process); the privileged task is only a fallback.
/// - Always working. The sensor is re-acquired if it appears late after boot, and
///   the level is re-sent after mode changes, restores and wake, when the firmware
///   resets it.
/// </summary>
internal sealed class AutoBacklightService : IDisposable
{
    internal readonly record struct Bucket(double MinLux, double MaxLux, byte Level);

    // On in a dim or dark room, off in bright light. In Auto mode this keyboard's firmware
    // knows only two states: level 0 is off and any other level is the same full brightness
    // (measured: 40% and 100% look alike, 0% turns the light off), so finer levels would
    // change nothing anyone can see. The buckets overlap: between 100 and 160 lux the
    // light keeps its state, so a lamp at the border doesn't make it flap.
    internal static readonly Bucket[] Buckets =
    [
        new(0, 160, 100),
        new(100, double.MaxValue, 0)
    ];

    private const uint SensorIntervalMilliseconds = 500;
    private const int BrightenDwellMilliseconds = 1500; // room got darker: light the keys
    private const int DimDwellMilliseconds = 4000;      // room got brighter: dim the keys
    private const int SettleMilliseconds = 1500;        // after a mode change or wake
    private const int FadeMilliseconds = 500;
    private const int FadeStepMilliseconds = 40;
    private const int SensorRetryMilliseconds = 10_000;  // the sensor can appear late after boot
    private const int SensorRetries = 12;                // two minutes, then only the next refresh tries
    private const int EmptyReadsBeforeReopen = 3;
    private const int EmptyReadRetryMilliseconds = 5000;
    internal const int PollMilliseconds = 2000;          // light checks while Auto runs and the screen is on

    private const byte GroupKeyboard = 0x06;
    private const byte CommandAutoLevel = 0x15;

    private static AutoBacklightService? _instance;

    private readonly Lock _gate = new();
    private readonly System.Threading.Timer _decisionTimer;
    private readonly System.Threading.Timer _retryTimer;
    private readonly System.Threading.Timer _pollTimer;
    private bool _screenOff;           // no checks while the screen is off (the keys are dark anyway)
    private int _retriesLeft = -1;     // -1 = not retrying
    private int _emptyReads;           // reads in a row that gave no value
    private AmbientLight? _sensor;
    private bool _listening;
    private int _bucket = -1;          // current bucket; -1 = not decided yet
    private int _candidate = -1;       // bucket waiting for its dwell time
    private byte? _output;             // last level sent to the keyboard
    private CancellationTokenSource? _fade;
    private bool _resendWithoutFade;
    private bool _disposed;

    internal AutoBacklightService()
    {
        _decisionTimer = new System.Threading.Timer(_ => Decide(), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new System.Threading.Timer(_ => RefreshSafely(), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        _instance = this;
    }

    // Called under _gate: checks run while Auto listens and the screen is on.
    private void UpdatePolling()
    {
        if (_disposed)
            return;
        try
        {
            if (_listening && !_screenOff && _sensor is not null)
                _pollTimer.Change(PollMilliseconds, PollMilliseconds);
            else
                _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>One light check (timer thread): hands the reading to <see cref="OnReading"/>; nothing escapes.</summary>
    private void Poll()
    {
        try
        {
            AmbientLight? sensor;
            lock (_gate)
            {
                if (_disposed || !_listening || _screenOff || (sensor = _sensor) is null)
                    return;
            }
            if (sensor.Read() is { } lux)
            {
                lock (_gate)
                    _emptyReads = 0;
                OnReading(lux);
                return;
            }
            bool lost;
            lock (_gate)
                lost = ++_emptyReads >= EmptyReadsBeforeReopen;
            if (lost)
            {
                lock (_gate)
                    _emptyReads = 0;
                SensorLost();
            }
        }
        catch (Exception exception)
        {
            Log.Error("Ambient light check failed", exception);
            SensorLost();
        }
    }

    /// <summary>The screen turned off: no light checks until it's on again.</summary>
    internal void ScreenOff()
    {
        lock (_gate)
        {
            _screenOff = true;
            UpdatePolling();
        }
    }

    /// <summary>The screen is on again (also without a wake, e.g. after Remote mode's short screen-off): checks resume.</summary>
    internal void ScreenOn()
    {
        lock (_gate)
        {
            _screenOff = false;
            UpdatePolling();
        }
    }

    internal void Start() => Refresh();

    // Timer threads: nothing may escape.
    private void RefreshSafely()
    {
        try
        {
            Refresh();
        }
        catch (Exception exception)
        {
            Log.Error("Keyboard auto refresh failed", exception);
        }
    }

    /// <summary>
    /// The sensor went away (driver restart or update) or stopped answering: drop it and open
    /// it again shortly. Off the sensor's own thread, since closing it waits for that thread.
    /// </summary>
    private void SensorLost()
    {
        AmbientLight? lost;
        lock (_gate)
        {
            if (_disposed || _sensor is null)
                return;
            (lost, _sensor) = (_sensor, null);
            _listening = false;
            UpdatePolling();
            _fade?.Cancel();
        }
        Log.Info("Ambient light sensor lost; opening it again");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lost.Dispose();
            }
            catch (Exception exception)
            {
                Log.Error("Closing the lost light sensor failed", exception);
            }
            lock (_gate)
                RetryLater();
        });
    }

    /// <summary>
    /// The keyboard mode may have changed (menu, Fn+Space, restore, first run).
    /// Callable from anywhere; does nothing before the service exists.
    /// </summary>
    internal static void NotifyModeChanged() => _instance?.Refresh();

    /// <summary>
    /// The screen came back on: send the current level again without a fade, in case
    /// the firmware dropped it during sleep. If it didn't, nothing visibly changes.
    /// </summary>
    internal void Resend()
    {
        bool startFresh;
        lock (_gate)
        {
            _screenOff = false;   // woke up: the screen is on
            UpdatePolling();
            startFresh = !_listening || _bucket < 0;
        }
        if (startFresh)
        {
            // Not running yet (e.g. the sensor wasn't ready at login): try again now.
            Refresh();
            return;
        }

        lock (_gate)
        {
            _output = null;
            _resendWithoutFade = true;
        }
        Schedule(SettleMilliseconds);
    }

    /// <summary>The keyboard mode was set (menu, Fn+Space, restore): decide from scratch, fade in.</summary>
    internal void Refresh()
    {
        var isAuto = AppSettings.CurrentLight == KeyboardLight.Auto;
        var start = false;
        lock (_gate)
        {
            if (_disposed)
                return;
            if (isAuto)
                EnsureSensor();
            if (_sensor is null)
            {
                if (isAuto)
                    RetryLater();
                return;
            }

            if (isAuto && !_listening)
                _listening = start = true;
            else if (!isAuto && _listening)
            {
                _listening = false;
                UpdatePolling();
                _fade?.Cancel();
            }

            // The firmware forgets the Auto level when the mode is set:
            // decide from scratch and send it again.
            _resendWithoutFade = false;
            _bucket = -1;
            _candidate = -1;
            _output = null;
        }

        // Checks every PollMilliseconds while Auto runs (see the class remarks: this driver's
        // change reports don't work); stopped outside Auto.
        lock (_gate)
        {
            UpdatePolling();
            if (start)
                _retriesLeft = -1;   // running: no more retries
        }

        if (isAuto)
            Schedule(SettleMilliseconds);
    }

    // Called under _gate. Keyboard Auto without its sensor stays dark: try again a few times.
    private void RetryLater()
    {
        if (_disposed)
            return;
        if (_retriesLeft < 0)
            _retriesLeft = SensorRetries;   // a new round
        if (_retriesLeft-- > 0)
            _retryTimer.Change(SensorRetryMilliseconds, Timeout.Infinite);
    }

    // Called under _gate.
    private void EnsureSensor()
    {
        if (_sensor is not null)
            return;

        try
        {
            _sensor = AmbientLight.Open(SensorIntervalMilliseconds);
            if (_sensor is null && _retriesLeft < 0)   // once per round of retries
                Log.Info("No ambient light sensor yet: keyboard Auto will retry on the next refresh");
        }
        catch (Exception exception)
        {
            _sensor = null;
            Log.Error("Ambient light sensor unavailable", exception);
        }
    }

    // Sensor thread (the sink catches what escapes).
    private void OnReading(double lux)
    {
        lock (_gate)
        {
            if (!_listening || _bucket < 0)
                return; // the first decision comes from the settle timer

            var wanted = Classify(lux, _bucket);
            if (wanted == _bucket)
            {
                // Back inside the current bucket: a shadow or flicker, forget it.
                _candidate = -1;
                return;
            }

            if (wanted == _candidate)
                return; // already waiting for this one

            _candidate = wanted;
            var dwell = Buckets[wanted].Level > Buckets[_bucket].Level
                ? BrightenDwellMilliseconds
                : DimDwellMilliseconds;
            Schedule(dwell);
        }
    }

    /// <summary>Runs the decision after <paramref name="milliseconds"/>; nothing once disposed.</summary>
    private void Schedule(int milliseconds)
    {
        try
        {
            _decisionTimer.Change(milliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Runs after the dwell (or settle) time: re-reads the light and commits the
    /// bucket only if the change still holds.
    /// </summary>
    // Timer thread: an exception here would end the process, so nothing escapes.
    private void Decide()
    {
        try
        {
            DecideCore();
        }
        catch (Exception exception)
        {
            Log.Error("Keyboard auto decision failed", exception);
        }
    }

    private void DecideCore()
    {
        AmbientLight? sensor;
        lock (_gate)
        {
            if (_disposed || !_listening || (sensor = _sensor) is null)
                return;
        }

        // Read outside the lock: the sensor's callback thread takes it too.
        double lux;
        try
        {
            if (sensor.Read() is not { } current)
            {
                // Not ready yet, or the sensor stopped answering (a driver reset without a
                // "gone" event): try again shortly, and after a few empty reads open it anew.
                bool lost;
                lock (_gate)
                    lost = ++_emptyReads >= EmptyReadsBeforeReopen;
                if (lost)
                {
                    lock (_gate)
                        _emptyReads = 0;
                    SensorLost();
                }
                else
                    Schedule(EmptyReadRetryMilliseconds);
                return;
            }
            lux = current;
            lock (_gate)
                _emptyReads = 0;
        }
        catch (Exception exception)
        {
            Log.Error("Ambient light reading failed", exception);
            SensorLost();
            return;
        }

        byte from;
        byte to;
        lock (_gate)
        {
            if (_disposed || !_listening)
                return;

            var wanted = Classify(lux, _bucket);
            _candidate = -1;
            if (wanted == _bucket && _output is not null)
                return;

            _bucket = wanted;
            to = Buckets[wanted].Level;
            // Unknown after a mode change: the firmware starts dark, so fade up from 0.
            // After a screen wake the level is just re-sent: no fade.
            from = _resendWithoutFade ? to : _output ?? 0;
            _resendWithoutFade = false;
            Log.Info($"Keyboard auto: {lux:0} lux -> {to}%");
        }

        StartFade(from, to);
    }

    /// <summary>
    /// Bucket for a reading. Stays in <paramref name="current"/> while the reading
    /// is inside it (hysteresis); otherwise the bucket whose range fits best.
    /// </summary>
    internal static int Classify(double lux, int current)
    {
        if (current >= 0 && lux >= Buckets[current].MinLux && lux <= Buckets[current].MaxLux)
            return current;

        // Darkest-first: the first bucket whose upper bound covers the reading.
        for (var i = 0; i < Buckets.Length; i++)
        {
            if (lux <= Buckets[i].MaxLux)
                return i;
        }
        return Buckets.Length - 1;
    }

    private void StartFade(byte from, byte to)
    {
        CancellationTokenSource fade;
        lock (_gate)
        {
            _fade?.Cancel();
            _fade = fade = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            var steps = from == to ? 1 : Math.Max(1, FadeMilliseconds / FadeStepMilliseconds);
            for (var i = 1; i <= steps; i++)
            {
                if (fade.IsCancellationRequested)
                    return;

                // Ease-out: big steps first, gentle landing.
                var t = 1 - Math.Pow(1 - (double)i / steps, 2);
                var level = (byte)Math.Round(from + (to - from) * t);
                if (!Send(level, isFinal: i == steps))
                    return;

                if (i < steps)
                {
                    try { await Task.Delay(FadeStepMilliseconds, fade.Token); }
                    catch (OperationCanceledException) { return; }
                }
            }
        });
    }

    /// <summary>
    /// Sends one level. A missed fade step doesn't matter (true); only a missed final
    /// level is reported (false).
    /// </summary>
    private bool Send(byte level, bool isFinal)
    {
        lock (_gate)
        {
            if (_output == level)
                return true;
        }

        if (!Bios.TryRun(Bios.Command(GroupKeyboard, CommandAutoLevel, level)))
            return !isFinal;

        lock (_gate)
            _output = level;
        return true;
    }

    public void Dispose()
    {
        AmbientLight? sensor;
        lock (_gate)
        {
            _disposed = true;
            _listening = false;
            (sensor, _sensor) = (_sensor, null);
            _fade?.Cancel();
        }
        sensor?.Dispose();
        _pollTimer.Dispose();
        _decisionTimer.Dispose();
        _retryTimer.Dispose();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
