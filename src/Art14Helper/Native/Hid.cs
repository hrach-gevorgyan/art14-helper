using System.Runtime.InteropServices;
using SafeFileHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;

namespace Art14Helper.Native;

/// <summary>One present HID interface and what its report descriptor says about it.</summary>
internal sealed record HidInterface(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportLength,
    ushort OutputReportLength)
{
    /// <summary>Opens for overlapped reads (input reports), sharing with other readers.</summary>
    internal SafeFileHandle OpenForReading() => Hid.Open(Path, Hid.ReadAccess, overlapped: true);

    /// <summary>Opens for writing output reports.</summary>
    internal SafeFileHandle OpenForWriting() => Hid.Open(Path, Hid.WriteAccess, overlapped: false);
}

/// <summary>
/// Lists HID interfaces through the configuration manager and reads their identity
/// and report sizes from hid.dll.
/// </summary>
internal static unsafe partial class Hid
{
    internal const uint ReadAccess = 0x80000000;  // GENERIC_READ
    internal const uint WriteAccess = 0x40000000; // GENERIC_WRITE

    /// <summary>The HID device interface class (GUID_DEVINTERFACE_HID).</summary>
    internal static Guid InterfaceClass
    {
        get
        {
            HidD_GetHidGuid(out var guid);
            return guid;
        }
    }

    /// <summary>
    /// Every present HID interface that could be queried. Interfaces opened exclusively
    /// by Windows (keyboards, mice) still show up: they're opened without access rights,
    /// which is enough to read attributes and capabilities.
    /// </summary>
    internal static List<HidInterface> ListPresent()
    {
        var found = new List<HidInterface>();
        foreach (var path in InterfacePaths(InterfaceClass))
        {
            using var handle = Open(path, access: 0, overlapped: false);
            if (handle.IsInvalid)
                continue;
            var attributes = new Attributes { Size = (uint)sizeof(Attributes) };
            if (HidD_GetAttributes(handle, ref attributes) == 0)
                continue;
            var caps = ReadCaps(handle);
            found.Add(new HidInterface(path, attributes.VendorId, attributes.ProductId,
                caps?.UsagePage ?? 0, caps?.Usage ?? 0, caps?.InputReportByteLength ?? 64, caps?.OutputReportByteLength ?? 0));
        }
        return found;
    }

    /// <summary>Writes one output report; false (with the Win32 error set) on failure or a short write.</summary>
    internal static bool WriteReport(SafeFileHandle device, byte[] report)
        => WriteFile(device, report, (uint)report.Length, out var written, IntPtr.Zero) != 0 && written == report.Length;

    internal static SafeFileHandle Open(string path, uint access, bool overlapped)
        => CreateFileW(path, access, ShareReadWrite, IntPtr.Zero, OpenExisting, overlapped ? FlagOverlapped : 0, IntPtr.Zero);

    private static Caps? ReadCaps(SafeFileHandle handle)
    {
        if (HidD_GetPreparsedData(handle, out var preparsed) == 0)
            return null;
        try
        {
            return HidP_GetCaps(preparsed, out var caps) == HidpStatusSuccess ? caps : null;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Paths of the present interfaces of a class, from CM_Get_Device_Interface_List.</summary>
    internal static List<string> InterfacePaths(Guid interfaceClass)
    {
        // The list can grow between the size query and the read; retry a few times.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out var length, ref interfaceClass, null, ListPresentOnly) != CrSuccess)
                break;
            var buffer = new char[length];
            fixed (char* list = buffer)
            {
                var result = CM_Get_Device_Interface_ListW(ref interfaceClass, null, list, length, ListPresentOnly);
                if (result == CrSuccess)
                    return SplitMultiString(buffer);
                if (result != CrBufferSmall)
                    break;
            }
        }
        throw new System.ComponentModel.Win32Exception("Could not list the devices of an interface class.");
    }

    /// <summary>"a\0b\0\0" → ["a", "b"].</summary>
    internal static List<string> SplitMultiString(ReadOnlySpan<char> text)
    {
        var items = new List<string>();
        while (!text.IsEmpty && text[0] != '\0')
        {
            var end = text.IndexOf('\0');
            if (end < 0)
                end = text.Length;
            items.Add(new string(text[..end]));
            text = end < text.Length ? text[(end + 1)..] : default;
        }
        return items;
    }

    private const uint ShareReadWrite = 0x1 | 0x2;     // FILE_SHARE_READ | FILE_SHARE_WRITE
    private const uint OpenExisting = 3;               // OPEN_EXISTING
    private const uint FlagOverlapped = 0x40000000;    // FILE_FLAG_OVERLAPPED
    private const int HidpStatusSuccess = 0x00110000;  // HIDP_STATUS_SUCCESS
    private const uint ListPresentOnly = 0;            // CM_GET_DEVICE_INTERFACE_LIST_PRESENT
    private const uint CrSuccess = 0;                  // CR_SUCCESS
    private const uint CrBufferSmall = 0x1A;           // CR_BUFFER_SMALL

    /// <summary>HIDD_ATTRIBUTES</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    /// <summary>HIDP_CAPS (only the leading fields are used).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public fixed ushort Counts[10];
    }

    [LibraryImport(Libraries.Hid)]
    private static partial void HidD_GetHidGuid(out Guid guid);

    [LibraryImport(Libraries.Hid)]
    private static partial byte HidD_GetAttributes(SafeFileHandle device, ref Attributes attributes);

    [LibraryImport(Libraries.Hid)]
    private static partial byte HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [LibraryImport(Libraries.Hid)]
    private static partial byte HidD_FreePreparsedData(IntPtr preparsed);

    [LibraryImport(Libraries.Hid)]
    private static partial int HidP_GetCaps(IntPtr preparsed, out Caps caps);

    [LibraryImport(Libraries.CfgMgr32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [LibraryImport(Libraries.CfgMgr32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char* buffer, uint length, uint flags);

    [LibraryImport(Libraries.Kernel32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    private static partial int WriteFile(SafeFileHandle file, byte[] buffer, uint length, out uint written, IntPtr overlapped);
}
