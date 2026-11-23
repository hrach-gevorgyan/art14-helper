using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Art14Helper.Native;

/// <summary>
/// The ambient light sensor in lux, through the Windows Sensor API (sensorsapi.dll):
/// one reading on request, or a callback whenever the driver reports a change. Plain COM
/// called by vtable slot, plus a source-generated event sink, so NativeAOT can compile it.
/// </summary>
internal sealed unsafe partial class AmbientLight : IDisposable
{
    private static readonly Guid ManagerClass = new("77A1C827-FCD2-4689-8915-9D613CC5FA3E");
    private static readonly Guid ManagerInterface = new("BD77DB67-45A8-42DC-8D00-6DCF15F8377A");
    private static readonly Guid AmbientLightType = new("97F115C8-599A-4153-8894-D2D12899918A");
    private static readonly Guid ValuesClass = new("0C15D503-D017-47CE-9016-7B3F978721CC");
    private static readonly Guid ValuesInterface = new("6848F6F2-3155-4F86-B6F5-263EEEAB3143");
    private static readonly PropertyKey LightLux = new(new Guid("E4C77CE2-DCB7-46E9-8439-4FEC548833A6"), 2);
    private static readonly PropertyKey ReportInterval = new(new Guid("7F8383EC-D3EC-495C-A8CF-B8BBE85C2920"), 13);
    private static readonly PropertyKey ChangeSensitivity = new(new Guid("7F8383EC-D3EC-495C-A8CF-B8BBE85C2920"), 14);
    private const double ChangePercent = 10;

    // Guards the two pointers: a late read (a timer at exit) must not use a released sensor.
    // The sink's callbacks never take it, so stopping the sink under it can't deadlock.
    private readonly Lock _lock = new();
    private IntPtr _sensor;      // ISensor
    private IntPtr _sinkPointer; // our ISensorEvents, while listening

    private AmbientLight(IntPtr sensor) => _sensor = sensor;

    /// <summary>The first ambient light sensor, or null when there is none (yet: some appear late after boot).</summary>
    internal static AmbientLight? Open(uint reportIntervalMilliseconds) => Mta.Run(() =>
    {
        if (CoCreateInstance(in ManagerClass, IntPtr.Zero, 0x1, in ManagerInterface, out var manager) < 0)
            return null;
        IntPtr sensors = IntPtr.Zero, sensor = IntPtr.Zero;
        try
        {
            // ISensorManager.GetSensorsByType, then ISensorCollection.GetCount and GetAt.
            var type = AmbientLightType;
            if (Call(manager, 4, &type, &sensors) < 0)
                return null;   // includes "none of this type"
            uint count;
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(sensors, 4))(sensors, &count) < 0 || count == 0)
                return null;
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(sensors, 3))(sensors, 0, &sensor) < 0)
                return null;
            var light = new AmbientLight(sensor);
            sensor = IntPtr.Zero;
            light.SetReporting(reportIntervalMilliseconds);
            return light;
        }
        finally
        {
            Release(sensor);
            Release(sensors);
            Release(manager);
        }
    });

    /// <summary>The current light level; null while the sensor isn't ready.</summary>
    internal double? Read() => Mta.Run<double?>(() =>
    {
        IntPtr report;
        lock (_lock)
        {
            if (_sensor == IntPtr.Zero)
                return null;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_sensor, 13))(_sensor, &report) < 0)   // ISensor.GetData
                return null;
        }
        try
        {
            return LuxIn(report);
        }
        finally
        {
            Release(report);
        }
    });

    /// <summary>
    /// Calls <paramref name="onLux"/> on a sensor thread for every reported change, until
    /// <see cref="StopListening"/>; <paramref name="onGone"/> when the sensor goes away (its
    /// driver restarted or was updated). This object is useless then: open the sensor again.
    /// </summary>
    internal void Listen(Action<double> onLux, Action onGone) => Mta.Run(() =>
    {
        using var _ = _lock.EnterScope();
        if (_sensor == IntPtr.Zero || _sinkPointer != IntPtr.Zero)
            return;
        var sink = new Sink(onLux, onGone);
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(sink, CreateComInterfaceFlags.None);
        try
        {
            var eventsId = typeof(ISensorEvents).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in eventsId, out var events));
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(_sensor, 17))(_sensor, events);   // ISensor.SetEventSink
            if (hr < 0)
            {
                Marshal.Release(events);
                Marshal.ThrowExceptionForHR(hr);   // no readings would ever come: the caller retries
            }
            _sinkPointer = events;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    });

    internal void StopListening() => Mta.Run(() =>
    {
        lock (_lock)
            StopSink();
    });

    private void StopSink()
    {
        if (_sinkPointer == IntPtr.Zero)
            return;
        if (_sensor != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(_sensor, 17))(_sensor, IntPtr.Zero);
        Marshal.Release(_sinkPointer);
        _sinkPointer = IntPtr.Zero;
    }

    public void Dispose() => Mta.Run(() =>
    {
        lock (_lock)
        {
            StopSink();
            Release(_sensor);
            _sensor = IntPtr.Zero;
        }
    });

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>The lux value of an ISensorDataReport.</summary>
    private static double? LuxIn(IntPtr report)
    {
        Variant value = default;
        var key = LightLux;
        if (((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, Variant*, int>)Slot(report, 4))(report, &key, &value) < 0)   // GetSensorValue
            return null;
        try
        {
            var raw = value.Value;
            return value.Type switch
            {
                4 => *(float*)&raw,    // VT_R4, what drivers report
                5 => *(double*)&raw,   // VT_R8
                _ => null
            };
        }
        finally
        {
            PropVariantClear(&value);
        }
    }

    /// <summary>
    /// Tells the driver to report only real changes: a change of <see cref="ChangePercent"/>
    /// in the light level, at most every <paramref name="milliseconds"/>. Without the
    /// sensitivity this laptop's driver reports five times a second even in steady light.
    /// </summary>
    private void SetReporting(uint milliseconds)
    {
        IntPtr values = IntPtr.Zero, perField = IntPtr.Zero, results = IntPtr.Zero;
        try
        {
            if (CoCreateInstance(in ValuesClass, IntPtr.Zero, 0x1, in ValuesInterface, out values) < 0
                || CoCreateInstance(in ValuesClass, IntPtr.Zero, 0x1, in ValuesInterface, out perField) < 0)
                return;
            // IPortableDeviceValues: SetValue (5), SetUnsignedIntegerValue (9), SetIPortableDeviceValuesValue (31).
            var lux = LightLux;
            var sensitivity = new Variant { Type = 5 };   // VT_R8
            *(double*)&sensitivity.Value = ChangePercent;
            ((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, Variant*, int>)Slot(perField, 5))(perField, &lux, &sensitivity);
            var sensitivityKey = ChangeSensitivity;
            ((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, IntPtr, int>)Slot(values, 31))(values, &sensitivityKey, perField);
            var intervalKey = ReportInterval;
            ((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, uint, int>)Slot(values, 9))(values, &intervalKey, milliseconds);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(_sensor, 10))(_sensor, values, &results);   // ISensor.SetProperties
        }
        finally
        {
            Release(results);
            Release(perField);
            Release(values);
        }
    }

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    private static int Call(IntPtr instance, int slot, Guid* argument, IntPtr* result)
        => ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(instance, slot))(instance, argument, result);

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            Marshal.Release(instance);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    /// <summary>PROPVARIANT: same shape as VARIANT for the scalar types read here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Value;
        public IntPtr Extra;
    }

    [GeneratedComInterface]
    [Guid("5D8DCC91-4641-47E7-B7C3-B74F48A6C391")]
    internal partial interface ISensorEvents
    {
        [PreserveSig] int OnStateChanged(IntPtr sensor, int state);
        [PreserveSig] int OnDataUpdated(IntPtr sensor, IntPtr report);
        [PreserveSig] int OnEvent(IntPtr sensor, IntPtr eventId, IntPtr data);
        [PreserveSig] int OnLeave(IntPtr sensorId);
    }

    /// <summary>Receives the driver's reports. Runs on a sensor thread; nothing may escape.</summary>
    [GeneratedComClass]
    internal sealed partial class Sink(Action<double> onLux, Action onGone) : ISensorEvents
    {
        public int OnStateChanged(IntPtr sensor, int state) => 0;

        public int OnDataUpdated(IntPtr sensor, IntPtr report)
        {
            try
            {
                if (LuxIn(report) is { } lux)
                    onLux(lux);
            }
            catch (Exception exception)
            {
                Log.Error("Light sensor report not handled", exception);
            }
            return 0;
        }

        public int OnEvent(IntPtr sensor, IntPtr eventId, IntPtr data) => 0;

        public int OnLeave(IntPtr sensorId)
        {
            try
            {
                onGone();
            }
            catch (Exception exception)
            {
                Log.Error("Light sensor removal not handled", exception);
            }
            return 0;
        }
    }

    [LibraryImport(Libraries.Ole32)]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [LibraryImport(Libraries.Ole32)]
    private static partial int PropVariantClear(Variant* value);
}
