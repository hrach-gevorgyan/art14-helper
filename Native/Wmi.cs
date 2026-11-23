using System.Globalization;
using System.Runtime.InteropServices;
using static Art14Helper.Native.WbemCom;

namespace Art14Helper.Native;

/// <summary>A named argument of a WMI method: uint32, or uint8 when <see cref="IsByte"/>.</summary>
internal readonly record struct WmiArgument(string Name, uint Value, bool IsByte = false);

/// <summary>A WMI call failed: the service, the class, the provider or the method.</summary>
internal sealed class WmiException : Exception
{
    internal WmiException(string message, int result) : base($"{message} (0x{result:X8})") => HResult = result;
}

/// <summary>
/// The app's WMI client: queries, method calls and event watching, straight on WMI's COM
/// interfaces. Objects live in the multithreaded apartment (see <see cref="Mta"/>).
/// </summary>
internal static unsafe class Wmi
{
    private const int Batch = 32;
    private static readonly Lock ConnectGate = new();
    private static readonly Dictionary<string, IntPtr> Connections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One object of a query or event; valid only inside the callback that received it.</summary>
    internal sealed class Row : IDisposable
    {
        private IntPtr _object;
        private List<Row>? _inner;

        internal Row(IntPtr instance) => _object = instance;

        internal IntPtr Object => _object;

        /// <summary>The value as text (numbers invariant); empty when null.</summary>
        internal string Text(string property) => Read(property, TextOf) ?? "";

        internal double Number(string property)
            => Read(property, value => double.TryParse(TextOf(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0);

        /// <summary>A uint8[] property.</summary>
        internal byte[] Bytes(string property) => Read(property, BytesOf);

        /// <summary>A property that holds another WMI object; released together with this row.</summary>
        internal Row Inner(string property)
        {
            var inner = new Row(Read(property, value =>
            {
                if (value.Type != 13 || value.Value == IntPtr.Zero)   // VT_UNKNOWN
                    throw new WmiException($"Property {property} holds no object", 0);
                // The variant's reference goes away with VariantClear; keep one of our own.
                Marshal.AddRef(value.Value);
                return value.Value;
            }));
            (_inner ??= []).Add(inner);
            return inner;
        }

        private T Read<T>(string property, Func<Variant, T> convert)
        {
            Variant value = default;
            var hr = Get(_object, property, &value);
            if (hr < 0)
                throw new WmiException($"WMI property {property} not readable", hr);
            try
            {
                return convert(value);
            }
            finally
            {
                VariantClear(&value);
            }
        }

        public void Dispose()
        {
            if (_inner is not null)
                foreach (var inner in _inner)
                    inner.Dispose();
            _inner = null;
            WbemCom.Release(_object);
            _object = IntPtr.Zero;
        }
    }

    private static string? TextOf(Variant value)
    {
        var raw = value.Value;
        var invariant = CultureInfo.InvariantCulture;
        return value.Type switch
        {
            2 => ((short)raw).ToString(invariant),           // VT_I2
            3 => ((int)raw).ToString(invariant),             // VT_I4
            4 => (*(float*)&raw).ToString(invariant),        // VT_R4
            5 => (*(double*)&raw).ToString(invariant),       // VT_R8
            8 => raw == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(raw),   // VT_BSTR (WMI's 64-bit integers come as text too)
            11 => (short)raw != 0 ? "True" : "False",        // VT_BOOL
            16 => ((sbyte)raw).ToString(invariant),          // VT_I1
            17 => ((byte)raw).ToString(invariant),           // VT_UI1
            18 => ((ushort)raw).ToString(invariant),         // VT_UI2
            19 => ((uint)raw).ToString(invariant),           // VT_UI4
            20 => ((long)raw).ToString(invariant),           // VT_I8
            21 => ((ulong)raw).ToString(invariant),          // VT_UI8
            _ => null                                        // empty, null, arrays, objects
        };
    }

    private static byte[] BytesOf(Variant value)
    {
        const ushort ByteArray = 0x2011;   // VT_ARRAY | VT_UI1
        if (value.Type != ByteArray || value.Value == IntPtr.Zero)
            return [];
        SafeArrayGetLBound(value.Value, 1, out var lower);
        SafeArrayGetUBound(value.Value, 1, out var upper);
        var bytes = new byte[Math.Max(0, upper - lower + 1)];
        void* data;
        if (bytes.Length > 0 && SafeArrayAccessData(value.Value, &data) >= 0)
        {
            new ReadOnlySpan<byte>(data, bytes.Length).CopyTo(bytes);
            SafeArrayUnaccessData(value.Value);
        }
        return bytes;
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
            throw new WmiException(what, hr);
    }

    // ---- Apartment and connections ----

    /// <summary>A new connection to a namespace such as root\cimv2; the caller releases it.</summary>
    internal static IntPtr Open(string scope)
    {
        Check(CoCreateInstance(in LocatorClass, IntPtr.Zero, InProcess, in LocatorInterface, out var locator), "WMI is not available");
        try
        {
            // A leading \\.\ names this computer; the plain namespace path means the same.
            var path = scope.StartsWith(@"\\.\", StringComparison.Ordinal) ? scope[4..] : scope;
            Check(ConnectServer(locator, path, out var services), $"WMI namespace {path} not available");
            SetSecurity(services);
            return services;
        }
        finally
        {
            WbemCom.Release(locator);
        }
    }

    /// <summary>
    /// The shared connection to a namespace, made on first use. The caller gets its own
    /// reference and releases it when done, so a connection dropped meanwhile by another
    /// thread stays valid until every user is finished with it.
    /// </summary>
    private static IntPtr Shared(string scope)
    {
        lock (ConnectGate)
        {
            if (!Connections.TryGetValue(scope, out var services))
                Connections[scope] = services = Open(scope);
            Marshal.AddRef(services);
            return services;
        }
    }

    /// <summary>
    /// After the WMI service restarted, <paramref name="dead"/> no longer works: forget it, so
    /// the next user connects again. Only if it's still the cached one; another thread may
    /// already have replaced it.
    /// </summary>
    private static void Drop(string scope, IntPtr dead)
    {
        lock (ConnectGate)
        {
            if (Connections.TryGetValue(scope, out var cached) && cached == dead)
            {
                Connections.Remove(scope);
                WbemCom.Release(cached);
            }
        }
    }

    private static bool IsDisconnect(int hr) => hr is Disconnected or ServerGone;

    /// <summary>Starts a query on the shared connection, reconnecting once if WMI was restarted.</summary>
    private static IntPtr StartQuery(string scope, string query)
    {
        var services = Shared(scope);
        try
        {
            var hr = WbemCom.Query(services, query, events: false, out var enumerator);
            if (IsDisconnect(hr))
            {
                Drop(scope, services);
                WbemCom.Release(services);
                services = IntPtr.Zero;   // nothing to release if reconnecting fails
                services = Shared(scope);
                hr = WbemCom.Query(services, query, events: false, out enumerator);
            }
            Check(hr, $"WMI query failed ({query})");
            return enumerator;
        }
        finally
        {
            WbemCom.Release(services);
        }
    }

    /// <summary>Hands every result of <paramref name="enumerator"/> to <paramref name="visit"/>, releasing each after.</summary>
    private static void Drain(IntPtr enumerator, string query, Action<Row> visit)
    {
        Span<IntPtr> batch = stackalloc IntPtr[Batch];
        while (true)
        {
            var hr = Next(enumerator, Infinite, batch, out var count);
            Check(hr, $"WMI query failed ({query})");
            var position = 0;
            try
            {
                for (; position < count; position++)
                {
                    using var row = new Row(batch[position]);
                    batch[position] = IntPtr.Zero;
                    visit(row);
                }
            }
            finally
            {
                for (var rest = position; rest < count; rest++)
                    WbemCom.Release(batch[rest]);
            }
            if (count < batch.Length)
                return;   // WBEM_S_FALSE: no more
        }
    }

    // ---- Queries ----

    /// <summary>Calls <paramref name="visit"/> for every result, as they arrive. Rows are valid only inside it.</summary>
    internal static void ForEach(string query, Action<Row> visit, string scope = @"root\cimv2") => Mta.Run(() =>
    {
        var enumerator = StartQuery(scope, query);
        try
        {
            Drain(enumerator, query, visit);
        }
        finally
        {
            WbemCom.Release(enumerator);
        }
    });

    /// <summary>The first non-blank value of <paramref name="property"/>, or null.</summary>
    internal static string? FirstText(string query, string property, string scope = @"root\cimv2")
    {
        string? found = null;
        ForEach(query, row =>
        {
            if (found is null && row.Text(property) is var text && !string.IsNullOrWhiteSpace(text))
                found = text;
        }, scope);
        return found;
    }

    /// <summary>Calls <paramref name="method"/> on every object the query returns; returns how many were called.</summary>
    internal static int CallOnEach(string scope, string query, string method, params WmiArgument[] arguments) => Mta.Run(() =>
    {
        var targets = new List<(string Path, string Class)>();
        ForEach(query, row => targets.Add((row.Text("__RELPATH"), row.Text("__CLASS"))), scope);
        if (targets.Count == 0)
            return 0;
        var services = Shared(scope);
        IntPtr classObject = IntPtr.Zero, input = IntPtr.Zero;
        try
        {
            Check(GetObject(services, targets[0].Class, out classObject), $"WMI class {targets[0].Class} not available");
            input = SpawnInput(classObject, method);
            foreach (var argument in arguments)
                PutNumber(input, argument);
            foreach (var target in targets)
            {
                Check(ExecMethod(services, target.Path, method, input, out var output), $"WMI method {method} failed");
                WbemCom.Release(output);
            }
            return targets.Count;
        }
        finally
        {
            WbemCom.Release(input);
            WbemCom.Release(classObject);
            WbemCom.Release(services);
        }
    });

    /// <summary>A fresh in-parameters object for a method of a class; zero when it takes none.</summary>
    internal static IntPtr SpawnInput(IntPtr classObject, string method)
    {
        Check(GetMethod(classObject, method, out var signature), $"WMI method {method} not found");
        if (signature == IntPtr.Zero)
            return IntPtr.Zero;
        try
        {
            Check(SpawnInstance(signature, out var input), $"WMI method {method}: no parameters");
            return input;
        }
        finally
        {
            WbemCom.Release(signature);
        }
    }

    /// <summary>WMI takes uint32 as VT_I4 and uint8 as VT_UI1.</summary>
    private static void PutNumber(IntPtr instance, WmiArgument argument)
    {
        var value = argument.IsByte
            ? new Variant { Type = 17, Value = (nint)(byte)argument.Value }   // VT_UI1
            : new Variant { Type = 3, Value = (nint)(int)argument.Value };    // VT_I4
        Check(Put(instance, argument.Name, &value), $"WMI argument {argument.Name} not accepted");
    }

    /// <summary>Puts a uint8[] value, as WMI methods take buffers.</summary>
    internal static void PutBytes(IntPtr instance, string property, ReadOnlySpan<byte> bytes)
    {
        var array = SafeArrayCreateVector(17, 0, (uint)bytes.Length);   // VT_UI1
        if (array == IntPtr.Zero)
            throw new OutOfMemoryException("No memory for a WMI argument.");
        void* data;
        if (SafeArrayAccessData(array, &data) >= 0)
        {
            bytes.CopyTo(new Span<byte>(data, bytes.Length));
            SafeArrayUnaccessData(array);
        }
        var value = new Variant { Type = 0x2011, Value = array };   // VT_ARRAY | VT_UI1
        try
        {
            Check(Put(instance, property, &value), $"WMI argument {property} not accepted");
        }
        finally
        {
            VariantClear(&value);   // Put kept its own copy
        }
    }

    // ---- Events ----

    /// <summary>
    /// Hands every event of a WMI event query to <paramref name="onEvent"/> on a background
    /// thread until disposed. Throws if the query can't be started; if WMI restarts later,
    /// it reconnects by itself.
    /// </summary>
    internal static IDisposable Watch(string scope, string eventQuery, Action<Row> onEvent)
    {
        var watch = new EventWatch(scope, eventQuery, onEvent);
        watch.Start();
        return watch;
    }

    private sealed class EventWatch(string scope, string query, Action<Row> onEvent) : IDisposable
    {
        // The thread waits for the next event without a timeout, so it costs nothing while
        // idle (a timed wait is a call into the WMI service each time). A stop is therefore
        // noticed at the next event; it only happens at exit, and the thread is a background one.
        private volatile bool _stopped;

        internal void Start()
        {
            Exception? failure = null;
            using var started = new ManualResetEventSlim();
            var thread = new Thread(() => Listen(started, error => failure = error)) { IsBackground = true, Name = "WMI events" };
            thread.Start();
            started.Wait();
            if (failure is not null)
                throw failure;
        }

        private void Listen(ManualResetEventSlim started, Action<Exception> fail)
        {
            CoInitializeEx(IntPtr.Zero, 0);
            var first = true;
            while (!_stopped)
            {
                IntPtr services = IntPtr.Zero, enumerator = IntPtr.Zero;
                try
                {
                    services = Open(scope);
                    Check(WbemCom.Query(services, query, events: true, out enumerator), $"WMI event query failed ({query})");
                    if (first)
                    {
                        first = false;
                        started.Set();
                    }
                    Receive(enumerator);
                }
                catch (Exception exception) when (first)
                {
                    fail(exception);
                    started.Set();
                    return;
                }
                catch (Exception exception)
                {
                    Log.Error("WMI events stopped, reconnecting", exception);
                    Thread.Sleep(5000);
                }
                finally
                {
                    WbemCom.Release(enumerator);
                    WbemCom.Release(services);
                }
            }
        }

        private void Receive(IntPtr enumerator)
        {
            Span<IntPtr> one = stackalloc IntPtr[1];
            while (!_stopped)
            {
                var hr = Next(enumerator, Infinite, one, out var count);
                if (count == 1)
                {
                    using var row = new Row(one[0]);
                    if (_stopped)
                        return;
                    try
                    {
                        onEvent(row);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("WMI event not handled", exception);
                    }
                }
                else if (hr != TimedOut)
                    Check(hr, "WMI event wait failed");
            }
        }

        public void Dispose() => _stopped = true;
    }
}

/// <summary>
/// One method of one WMI object, found once and called many times (the BIOS interface:
/// every key press and brightness step). Keeps its own connection.
/// </summary>
internal sealed unsafe class WmiMethod : IDisposable
{
    private readonly string _path;
    private readonly string _method;
    private IntPtr _services;
    private IntPtr _input;

    private WmiMethod(IntPtr services, IntPtr input, string path, string method)
        => (_services, _input, _path, _method) = (services, input, path, method);

    /// <summary>
    /// Finds <paramref name="className"/> in <paramref name="scope"/>; of several instances,
    /// the one whose InstanceName ends with <paramref name="preferredInstance"/>, else the
    /// first. Null when there is none.
    /// </summary>
    internal static WmiMethod? Find(string scope, string className, string method, string preferredInstance) => Mta.Run(() =>
    {
        var services = Wmi.Open(scope);
        var input = IntPtr.Zero;
        try
        {
            string? first = null, preferred = null;
            var query = $"SELECT InstanceName FROM {className}";
            if (WbemCom.Query(services, query, events: false, out var enumerator) is var hr and < 0)
                throw new WmiException($"WMI class {className} not available", hr);
            try
            {
                Span<IntPtr> one = stackalloc IntPtr[1];
                while (true)
                {
                    // Errors surface here (access denied without admin rights) and must not read as "no such class".
                    if (WbemCom.Next(enumerator, WbemCom.Infinite, one, out var count) is var nextHr and < 0)
                        throw new WmiException($"WMI class {className} not readable", nextHr);
                    if (count == 0)
                        break;
                    using var row = new Wmi.Row(one[0]);
                    var path = row.Text("__RELPATH");
                    first ??= path;
                    if (preferred is null && row.Text("InstanceName").EndsWith(preferredInstance, StringComparison.OrdinalIgnoreCase))
                        preferred = path;
                }
            }
            finally
            {
                WbemCom.Release(enumerator);
            }
            if ((preferred ?? first) is not { } chosen)
            {
                WbemCom.Release(services);
                return null;
            }
            if (WbemCom.GetObject(services, className, out var classObject) is var classHr and < 0)
                throw new WmiException($"WMI class {className} not available", classHr);
            try
            {
                input = Wmi.SpawnInput(classObject, method);
            }
            finally
            {
                WbemCom.Release(classObject);
            }
            return new WmiMethod(services, input, chosen, method);
        }
        catch
        {
            WbemCom.Release(input);
            WbemCom.Release(services);
            throw;
        }
    });

    /// <summary>
    /// Calls the method with one byte-array input and returns its byte-array output. Like
    /// System.Management's EnablePrivileges, the call runs with every privilege the
    /// process holds turned on, for this thread only.
    /// </summary>
    internal byte[] Invoke(string inputName, byte[] input, string outputName) => Mta.Run(() =>
    {
        if (_services == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(WmiMethod));
        Wmi.PutBytes(_input, inputName, input);
        var output = IntPtr.Zero;
        var hr = Privileges.WithAllEnabled(() => WbemCom.ExecMethod(_services, _path, _method, _input, out output));
        if (hr < 0)
            throw new WmiException($"WMI method {_method} failed", hr);
        if (output == IntPtr.Zero)
            throw new WmiException($"WMI method {_method} returned nothing", 0);
        using var result = new Wmi.Row(output);
        return result.Bytes(outputName);
    });

    public void Dispose() => Mta.Run(() =>
    {
        WbemCom.Release(_input);
        WbemCom.Release(_services);
        _input = _services = IntPtr.Zero;
    });
}
