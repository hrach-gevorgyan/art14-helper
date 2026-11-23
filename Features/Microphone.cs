using System.Runtime.InteropServices.Marshalling;

namespace Art14Helper;

/// <summary>
/// The F7 key: mutes the default microphone for every app (the endpoint's own mute, the
/// one Windows sound settings show) and keeps the key's LED in step. The firmware only
/// reports the key press; it neither mutes nor lights anything itself. Mute changes made
/// elsewhere (sound settings, a mixer) reach the LED too, through a volume callback.
/// </summary>
internal sealed partial class MicrophoneMute : IDisposable
{
    private readonly Lock _lock = new();
    private CoreAudio.Microphones? _microphones;
    private CoreAudio.IAudioEndpointVolume? _control;
    private MuteWatcher? _watcher;
    private string? _endpointId;
    private bool _problemLogged;
    private volatile bool _disposed;

    // LED state as last written (1 lit, 0 dark, -1 not known). The volume callback also fires
    // when only the level moves, so writes happen only when this value would change.
    private int _led = -1;

    /// <summary>Flips the mute: true means the microphone is now muted; null if nothing changed.</summary>
    internal bool? Toggle()
    {
        lock (_lock)
        {
            if (_disposed || Current() is not { } volume || volume.GetMute(out var muted) != 0)
                return null;
            var mute = muted == 0;
            if (volume.SetMute(mute ? 1 : 0, IntPtr.Zero) != 0)
            {
                Forget();   // the device probably went away; the next press looks again
                return null;
            }
            SetLed(mute, always: false);
            Log.Info(mute ? "Microphone muted with F7" : "Microphone unmuted with F7");
            return mute;
        }
    }

    /// <summary>Sets the LED from the real mute state (it can be stale after sleep or a restart).</summary>
    internal void Sync() => SyncLed(always: true);

    /// <summary>Sets the LED from the mute state read now, under the lock, so a late call can't undo a newer change.</summary>
    private void SyncLed(bool always)
    {
        lock (_lock)
        {
            if (!_disposed && Current() is { } volume && volume.GetMute(out var muted) == 0)
                SetLed(muted != 0, always);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            Forget();
            _microphones?.Dispose();
            _microphones = null;
        }
    }

    /// <summary>
    /// The volume control of the default microphone, reopened when the default changes
    /// (otherwise F7 would keep muting a mic that's no longer used). Called under the lock.
    /// </summary>
    private CoreAudio.IAudioEndpointVolume? Current()
    {
        try
        {
            _microphones ??= new CoreAudio.Microphones();
            var result = _microphones.DefaultId(out var id);
            if (result != 0 || id is null)
            {
                if (result != CoreAudio.NoDevice)   // no microphone at all is a normal state
                    Report($"No default microphone (0x{result:X8})");
                Forget();
                return null;
            }
            if (id == _endpointId && _control is { } unchanged)
                return unchanged;   // same device as last time

            Forget();
            if (_microphones.OpenVolume(id, out var volume) != 0 || volume is null)
            {
                Report("The microphone's volume control couldn't be opened");
                return null;
            }
            _control = volume;
            _endpointId = id;
            _watcher = new MuteWatcher(this);
            if (volume.RegisterControlChangeNotify(_watcher) != 0)
                _watcher = null;
            _problemLogged = false;
            return volume;
        }
        catch (Exception exception)
        {
            Report($"Microphone mute unavailable: {exception.Message}");
            Forget();
            _microphones?.Dispose();
            _microphones = null;
            return null;
        }
    }

    // Called under the lock.
    private void Forget()
    {
        if (_control is not null)
        {
            if (_watcher is not null)
                _control.UnregisterControlChangeNotify(_watcher);   // harmless if the device is gone
            CoreAudio.Release(_control);
        }
        _control = null;
        _watcher = null;
        _endpointId = null;
    }

    /// <summary>Lights the LED for "muted". Skips a write that wouldn't change it unless <paramref name="always"/>.</summary>
    private void SetLed(bool on, bool always)
    {
        var state = on ? 1 : 0;
        if (Interlocked.Exchange(ref _led, state) == state && !always)
            return;
        if (!Bios.TryRun(Bios.Command(0x04, 0x0B, (byte)state)))
            Interlocked.CompareExchange(ref _led, -1, state);   // unknown again, so the next change retries
    }

    private void Report(string problem)
    {
        if (_problemLogged)
            return;
        _problemLogged = true;
        Log.Error(problem);
    }

    /// <summary>
    /// Receives mute and volume changes from the audio engine, which expects a quick
    /// return; the LED is written on a pool thread instead.
    /// </summary>
    [GeneratedComClass]
    private sealed partial class MuteWatcher(MicrophoneMute owner) : CoreAudio.IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notification)
        {
            if (notification == IntPtr.Zero || owner._disposed)
                return 0;
            var muted = CoreAudio.IsMutedIn(notification);
            if (Volatile.Read(ref owner._led) != (muted ? 1 : 0))
                _ = Task.Run(() => owner.SyncLed(always: false));   // re-reads: notifications can overtake each other
            return 0;
        }
    }
}
