namespace Art14Helper;

/// <summary>The BIOS received a command and refused it; <see cref="Status"/> is its answer code.</summary>
internal sealed class BiosRefusedException(ulong command, byte status)
    : InvalidOperationException($"The BIOS refused command 0x{command:X} (code {status}).")
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
    private static bool _missing;

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
            throw new InvalidOperationException("Administrator rights are needed to talk to the HONOR BIOS.");
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
                throw new InvalidOperationException($"The BIOS gave no answer to command 0x{command:X}.");
            if (reply[0] != 0)
                throw new BiosRefusedException(command, reply[0]);
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
        if (_missing)
            throw new InvalidOperationException("This laptop has no HONOR BIOS interface.");
        _method ??= WmiMethod.Find(@"\\.\root\wmi", "OemWMIMethod", "OemWMIfun", HonorInstance);
        if (_method is null)
        {
            _missing = true;
            Log.Error("HONOR BIOS interface (OemWMIMethod) not found");
            throw new InvalidOperationException("This laptop has no HONOR BIOS interface.");
        }
        var input = new byte[InputLength];
        BitConverter.TryWriteBytes(input, command);
        return _method.Invoke("u8Input", input, "u8Output");
    }

    // Called under Gate.
    private static void Forget()
    {
        _method?.Dispose();
        _method = null;
    }
}
