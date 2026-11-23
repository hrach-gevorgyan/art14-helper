namespace Art14Helper;

/// <summary>
/// Repeats a check a few times with a pause in between, for state the firmware may
/// still change while the laptop finishes starting or waking (it can reset the charge
/// limit or the keyboard light seconds after resume). One run at a time; a run that's
/// asked for while another is going is skipped.
/// </summary>
internal sealed class SettleWatch(string what, int checks, TimeSpan pause, Func<Task> check) : IDisposable
{
    private readonly SemaphoreSlim _running = new(1, 1);
    private volatile bool _disposed;

    internal void Start() => _ = RunAsync();

    internal async Task RunAsync()
    {
        // ConfigureAwait(false) throughout: the checks talk to the BIOS and must not land on
        // the UI thread just because a run was started from there.
        if (_disposed || !await _running.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            for (var round = 0; round < checks && !_disposed; round++)
            {
                await Task.Delay(pause).ConfigureAwait(false);
                if (_disposed)
                    return;
                try
                {
                    await check().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Log.Error($"{what} failed", exception);
                }
            }
        }
        finally
        {
            if (!_disposed)
                _running.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _running.Dispose();
    }
}
