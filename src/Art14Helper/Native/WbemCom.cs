using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// The WMI COM interfaces (wbemcli.h), called through their vtables with function
/// pointers: no runtime COM interop, so it works with NativeAOT. Only the slots the app
/// uses are named; numbers are the vtable positions, IUnknown's three first.
/// Every call returns an HRESULT. Objects belong to the multithreaded apartment.
/// </summary>
internal static unsafe partial class WbemCom
{
    internal static readonly Guid LocatorClass = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    internal static readonly Guid LocatorInterface = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    internal const int ReturnImmediately = 0x10, ForwardOnly = 0x20;   // WBEM_FLAG_*
    internal const int Infinite = -1;                                    // WBEM_INFINITE
    internal const int TimedOut = 0x40004;                               // WBEM_S_TIMEDOUT
    internal const int Disconnected = unchecked((int)0x80010108), ServerGone = unchecked((int)0x800706BA);

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    internal static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(instance, 2))(instance);
    }

    // ---- IWbemLocator ----

    internal static int ConnectServer(IntPtr locator, string scope, out IntPtr services)
    {
        var path = Marshal.StringToBSTR(scope);
        try
        {
            IntPtr result;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Slot(locator, 3))(
                locator, path, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, &result);
            services = hr >= 0 ? result : IntPtr.Zero;
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(path);
        }
    }

    // ---- IWbemServices ----

    internal static int GetObject(IntPtr services, string path, out IntPtr instance)
    {
        var text = Marshal.StringToBSTR(path);
        try
        {
            IntPtr result;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, IntPtr*, IntPtr, int>)Slot(services, 6))(
                services, text, 0, IntPtr.Zero, &result, IntPtr.Zero);
            instance = hr >= 0 ? result : IntPtr.Zero;
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(text);
        }
    }

    /// <summary>ExecQuery (slot 20) or, with <paramref name="events"/>, ExecNotificationQuery (slot 22).</summary>
    internal static int Query(IntPtr services, string query, bool events, out IntPtr enumerator)
    {
        var language = Marshal.StringToBSTR("WQL");
        var text = Marshal.StringToBSTR(query);
        try
        {
            IntPtr result;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr*, int>)Slot(services, events ? 22 : 20))(
                services, language, text, ReturnImmediately | ForwardOnly, IntPtr.Zero, &result);
            enumerator = hr >= 0 ? result : IntPtr.Zero;
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(language);
            Marshal.FreeBSTR(text);
        }
    }

    internal static int ExecMethod(IntPtr services, string objectPath, string method, IntPtr input, out IntPtr output)
    {
        var path = Marshal.StringToBSTR(objectPath);
        var name = Marshal.StringToBSTR(method);
        try
        {
            IntPtr result = IntPtr.Zero;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, IntPtr, int>)Slot(services, 24))(
                services, path, name, 0, IntPtr.Zero, input, &result, IntPtr.Zero);
            output = hr >= 0 ? result : IntPtr.Zero;
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(path);
            Marshal.FreeBSTR(name);
        }
    }

    // ---- IEnumWbemClassObject ----

    /// <summary>Up to <paramref name="batch"/>.Length objects; <paramref name="count"/> says how many came.</summary>
    internal static int Next(IntPtr enumerator, int timeout, Span<IntPtr> batch, out int count)
    {
        uint returned;
        int hr;
        fixed (IntPtr* objects = batch)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, uint*, int>)Slot(enumerator, 4))(
                enumerator, timeout, (uint)batch.Length, objects, &returned);
        count = (int)returned;
        return hr;
    }

    // ---- IWbemClassObject ----

    /// <summary>VARIANT: type tag, then up to 16 bytes of value (8 on 32-bit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Variant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Value;
        public IntPtr Extra;
    }

    internal static int Get(IntPtr instance, string property, Variant* value)
    {
        fixed (char* name = property)
            return ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int*, int*, int>)Slot(instance, 4))(
                instance, name, 0, value, null, null);
    }

    internal static int Put(IntPtr instance, string property, Variant* value)
    {
        fixed (char* name = property)
            return ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int, int>)Slot(instance, 5))(instance, name, 0, value, 0);
    }

    internal static int SpawnInstance(IntPtr classObject, out IntPtr instance)
    {
        IntPtr result;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(classObject, 15))(classObject, 0, &result);
        instance = hr >= 0 ? result : IntPtr.Zero;
        return hr;
    }

    /// <summary>The in-parameters class of a method; zero when the method takes none.</summary>
    internal static int GetMethod(IntPtr classObject, string method, out IntPtr inSignature)
    {
        IntPtr input = IntPtr.Zero;
        int hr;
        fixed (char* name = method)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr*, IntPtr*, int>)Slot(classObject, 19))(classObject, name, 0, &input, null);
        inSignature = hr >= 0 ? input : IntPtr.Zero;
        return hr;
    }

    // ---- Plumbing ----

    internal const uint InProcess = 0x1;   // CLSCTX_INPROC_SERVER

    /// <summary>
    /// Calls on the proxy authenticate per call and let WMI act as the caller, with the
    /// caller's current (possibly impersonated) token: what WMI expects from local clients.
    /// </summary>
    internal static int SetSecurity(IntPtr proxy)
        => CoSetProxyBlanket(proxy, 10, 0, IntPtr.Zero, 3, 3, IntPtr.Zero, 0x40);   // WINNT, no authz, CALL, IMPERSONATE, DYNAMIC_CLOAKING

    [LibraryImport(Libraries.Ole32)]
    internal static partial int CoInitializeEx(IntPtr reserved, uint model);

    [LibraryImport(Libraries.Ole32)]
    internal static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [LibraryImport(Libraries.Ole32)]
    private static partial int CoSetProxyBlanket(IntPtr proxy, uint authentication, uint authorization, IntPtr principal,
        uint level, uint impersonation, IntPtr identity, uint capabilities);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial int VariantClear(Variant* value);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial IntPtr SafeArrayCreateVector(ushort type, int lowerBound, uint length);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial int SafeArrayAccessData(IntPtr array, void** data);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial int SafeArrayUnaccessData(IntPtr array);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial int SafeArrayGetLBound(IntPtr array, uint dimension, out int bound);

    [LibraryImport(Libraries.OleAut32)]
    internal static partial int SafeArrayGetUBound(IntPtr array, uint dimension, out int bound);
}
