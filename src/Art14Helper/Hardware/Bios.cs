namespace Art14Helper;

/// <summary>The BIOS received a command and refused it; <see cref="Status"/> is its answer code.</summary>
internal sealed class BiosRefusedException(byte status, string message) : InvalidOperationException(message)
{
    internal byte Status { get; } = status;
}

/// <summary>
/// HONOR's BIOS interface: the ACPI-WMI method OemWMIMethod.OemWMIfun (the one HONOR
/// PC Manager uses). A command is a 64-bit number sent as the first bytes of a
/// 64-byte input: byte 0 is the group, byte 1 the command, bytes 2.. the arguments.
/// The reply's byte 0 is a status (0 = done), the rest is the answer.
/// </summary>
/// <remarks>
/// Windows lets only administrators use this interface. Without admin rights
/// <see cref="TryRun"/> gives up immediately and the caller takes its other path.
/// </remarks>
internal static class Bios
{
    private const int InputLength = 64;

    // Some firmware exposes a second instance; HONOR's software talks to this one.
    private const string HonorInstance = "HWMI_0";

    private static readonly Lock Gate = new();
    private static WmiMethod? _method;

    /// <summary>
    /// After the interface wasn't found, how long to wait before looking again. Right after
    /// boot or wake Windows may not have it ready yet; giving up for good would leave the app
    /// without fans, temperatures and every BIOS feature until it restarts.
    /// </summary>
    internal const long RetryMissingAfterMs = 60_000;

    private static long? _missingSince;   // tick count of the last search that found nothing
    private static bool _missingLogged;

    /// <summary>Builds a command: group, command, then up to six argument bytes.</summary>
    internal static ulong Command(byte group, byte command, params ReadOnlySpan<byte> arguments)
    {
        if (arguments.Length > 6)
            throw new ArgumentOutOfRangeException(nameof(arguments), "A BIOS command has at most six argument bytes.");
        var code = group | (ulong)command << 8;
        for (var i = 0; i < arguments.Length; i++)
            code |= (ulong)arguments[i] << (16 + 8 * i);
        return code;
    }

    /// <summary>A little-endian 16-bit value from a reply, or null when the reply is too short.</summary>
    internal static int? Word(byte[] reply, int offset)
        => reply.Length >= offset + 2 ? reply[offset] | reply[offset + 1] << 8 : null;

    /// <summary>A byte from a reply, or null when the reply is too short.</summary>
    internal static int? Byte(byte[] reply, int offset) => reply.Length > offset ? reply[offset] : null;

    /// <summary>
    /// Runs a command and returns the reply. Throws <see cref="BiosRefusedException"/>
    /// when the BIOS refuses (retrying won't help), or another exception when the
    /// interface can't be reached.
    /// </summary>
    internal static byte[] Run(ulong command)
    {
        if (!Privileged.IsElevated)
            throw Shown(m => new InvalidOperationException(m), N("Administrator rights are needed to talk to the HONOR BIOS."));
        lock (Gate)
        {
            byte[] reply;
            try
            {
                reply = Send(command);
            }
            catch (WmiException)
            {
                // A WMI service restart leaves the cached object dead: reconnect once.
                Forget();
                try
                {
                    reply = Send(command);
                }
                catch (Exception second) when (second is WmiException or InvalidOperationException)
                {
                    LogFailure(command, second);
                    throw;
                }
            }
            if (reply.Length == 0)
                throw Shown(m => new InvalidOperationException(m), N("The BIOS gave no answer to command 0x{0:X}."), command);
            if (reply[0] != 0)
                throw Shown(m => new BiosRefusedException(reply[0], m), N("The BIOS refused command 0x{0:X} (code {1})."), command, reply[0]);
            return reply;
        }
    }

    private static long _lastFailureLog = -60_000;

    /// <summary>At most one log entry a minute: gestures can fail dozens of times a second.</summary>
    private static void LogFailure(ulong command, Exception exception)
    {
        var now = Environment.TickCount64;
        if (now - _lastFailureLog < 60_000)
            return;
        _lastFailureLog = now;
        Log.Error($"BIOS command 0x{command:X} failed", exception);
    }

    /// <summary>
    /// <see cref="Run"/> that reports failure as false instead of an exception (no
    /// admin rights, no interface, or a refusal). Meant for frequent or background
    /// calls that can live without the result.
    /// </summary>
    internal static bool TryRun(ulong command, out byte[] reply)
    {
        reply = [];
        if (!Privileged.IsElevated)
            return false;
        try
        {
            reply = Run(command);
            return true;
        }
        catch (BiosRefusedException)
        {
            return false;
        }
        catch (Exception exception) when (exception is WmiException or InvalidOperationException)
        {
            return false; // already logged by Run
        }
    }

    internal static bool TryRun(ulong command) => TryRun(command, out _);

    // Called under Gate.
    private static byte[] Send(ulong command)
    {
        var now = Environment.TickCount64;
        if (_method is null && !ShouldSearch(_missingSince, now))
            throw Shown(m => new InvalidOperationException(m), N("The HONOR BIOS interface isn't available."));
        _method ??= WmiMethod.Find(@"\\.\root\wmi", "OemWMIMethod", "OemWMIfun", HonorInstance);
        if (_method is null)
        {
            _missingSince = now;
            if (!_missingLogged)   // once: on a laptop without it, every minute would fill the log
            {
                _missingLogged = true;
                Log.Error($"HONOR BIOS interface (OemWMIMethod) not found; looking again every {RetryMissingAfterMs / 1000} s when needed");
            }
            throw Shown(m => new InvalidOperationException(m), N("The HONOR BIOS interface isn't available."));
        }
        if (_missingSince is not null)
        {
            _missingSince = null;
            Log.Info("HONOR BIOS interface found");
        }
        var input = new byte[InputLength];
        BitConverter.TryWriteBytes(input, command);
        return _method.Invoke("u8Input", input, "u8Output");
    }

    /// <summary>Whether to look for the interface now: always until a search found nothing, then once a minute.</summary>
    internal static bool ShouldSearch(long? missingSince, long now)
        => missingSince is not { } since || now - since >= RetryMissingAfterMs;

    // Called under Gate.
    private static void Forget()
    {
        _method?.Dispose();
        _method = null;
    }
}
