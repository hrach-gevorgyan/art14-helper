namespace Art14Helper;

/// <summary>Keys the firmware reports as BIOS events instead of keystrokes.</summary>
internal enum BiosKey
{
    Microphone,   // F7
    Camera,       // F8
    F10,
    F11,
    ScreenSnip    // F12
}

/// <summary>
/// Listens to the BIOS event class (OemWMIEvent) and turns its codes into key presses,
/// touchpad on/off reports and keyboard light changes. Events arrive on a WMI thread;
/// handlers that do real work should hand it off.
/// </summary>
internal sealed class BiosKeys(Func<bool> ignoreLightEvents) : IDisposable
{
    /// <summary>
    /// Some firmware repeats a press. An event this soon after the previous one is ignored
    /// and restarts the window, so however long the repeats go on, the handler runs once.
    /// </summary>
    private const int RepeatWindowMilliseconds = 400;

    private static readonly Dictionary<uint, BiosKey> PressCodes = new()
    {
        [0x287] = BiosKey.Microphone,
        [0x288] = BiosKey.Camera,
        [0x28A] = BiosKey.F10,
        [0x28B] = BiosKey.F11,
        [0x28E] = BiosKey.ScreenSnip
    };

    private static readonly Dictionary<uint, KeyboardLight> LightCodes = new()
    {
        [0x2B1] = KeyboardLight.Off,
        [0x2B2] = KeyboardLight.Dim,
        [0x2B3] = KeyboardLight.Bright,
        [0x2B4] = KeyboardLight.Auto
    };

    private const uint TouchpadTurnedOn = 0x283, TouchpadTurnedOff = 0x2A3;
    private const uint CameraRemoved = 0x2E0, CameraAttached = 0x2E1;

    private readonly Dictionary<BiosKey, long> _lastSeen = [];
    private readonly HashSet<uint> _unknownLogged = [];   // only the WMI event thread touches it
    private bool? _cameraAttached;                        // the same
    private IDisposable? _subscription;

    /// <summary>A key was pressed (repeats already filtered out).</summary>
    internal event Action<BiosKey>? KeyPressed;

    /// <summary>The firmware switched the touchpad itself; true = now on.</summary>
    internal event Action<bool>? TouchpadSwitched;

    /// <summary>The firmware changed the keyboard light (Fn+Space).</summary>
    internal event Action<KeyboardLight>? LightChanged;

    /// <summary>The magnetic camera was put on (true) or taken off (false) the screen.</summary>
    internal event Action<bool>? CameraDocked;

    internal void Start() => _subscription = Wmi.Watch(@"root\wmi", "SELECT * FROM OemWMIEvent", OnEvent);

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnEvent(Wmi.Row row)
    {
        try
        {
            if (uint.TryParse(row.Text("Force"), out var raw))
                Dispatch(raw & 0xFFFF);
        }
        catch (Exception exception)
        {
            Log.Error("BIOS key event not handled", exception);
        }
    }

    internal void Dispatch(uint code)
    {
        if (PressCodes.TryGetValue(code, out var key))
        {
            if (IsFirstOfBurst(key, Environment.TickCount64))
                KeyPressed?.Invoke(key);
        }
        else if (code is TouchpadTurnedOn or TouchpadTurnedOff)
            TouchpadSwitched?.Invoke(code == TouchpadTurnedOn);   // a state, not a toggle: repeats are harmless
        else if (LightCodes.TryGetValue(code, out var light))
        {
            if (!ignoreLightEvents())
                LightChanged?.Invoke(light);   // ignored right after wake, when the firmware re-announces its own mode
        }
        else if (code is CameraRemoved or CameraAttached)
        {
            // A state: only a change counts, so a repeated or re-announced event shows nothing.
            var attached = code == CameraAttached;
            if (_cameraAttached != attached)
            {
                _cameraAttached = attached;
                CameraDocked?.Invoke(attached);
            }
        }
        else if (_unknownLogged.Add(code))
            Log.Info($"BIOS event 0x{code:X3} not used");   // once per code: brightness and charger events repeat a lot
    }

    internal bool IsFirstOfBurst(BiosKey key, long now)
    {
        lock (_lastSeen)
        {
            var first = !_lastSeen.TryGetValue(key, out var last) || now - last >= RepeatWindowMilliseconds;
            _lastSeen[key] = now;
            return first;
        }
    }
}
