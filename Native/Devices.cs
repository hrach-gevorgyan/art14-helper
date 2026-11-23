using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>One present device as Configuration Manager sees it.</summary>
internal sealed record DeviceNode(
    string InstanceId,
    string? Description,
    string? FriendlyName,
    string? Class,
    string? DriverVersion,
    string? DriverProvider,
    int Problem)
{
    /// <summary>What Device Manager shows: the friendly name, else the description.</summary>
    internal string DisplayName => FriendlyName ?? Description ?? "";
}

/// <summary>
/// Present devices and their driver details straight from Configuration Manager
/// (cfgmgr32): the same facts as WMI's Win32_PnPSignedDriver and Win32_PnPEntity, in
/// milliseconds instead of seconds, and without waking the WMI provider host.
/// </summary>
internal static unsafe partial class Devices
{
    private const uint Success = 0, BufferSmall = 0x1A;   // CR_*
    private const uint OnlyPresent = 0x100;               // CM_GETIDLIST_FILTER_PRESENT
    private const uint StringType = 0x12;                 // DEVPROP_TYPE_STRING

    private static readonly Guid General = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");      // DEVPKEY_Device_*
    private static readonly Guid DriverInfo = new("A8B865DD-2E3D-4094-AD97-E593A70C75D6");   // DEVPKEY_Device_Driver*
    private static readonly PropertyKey DescriptionKey = new(General, 2);
    private static readonly PropertyKey FriendlyNameKey = new(General, 14);
    private static readonly PropertyKey ClassKey = new(General, 9);
    private static readonly PropertyKey DriverVersionKey = new(DriverInfo, 3);
    private static readonly PropertyKey DriverProviderKey = new(DriverInfo, 9);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid category, uint id)
    {
        public readonly Guid Category = category;
        public readonly uint Id = id;
    }

    /// <summary>Every device that is plugged in now, with its driver details.</summary>
    internal static List<DeviceNode> ListPresent()
    {
        var nodes = new List<DeviceNode>();
        foreach (var id in PresentIds())
        {
            if (CM_Locate_DevNodeW(out var node, id, 0) != Success)
                continue;
            var problem = CM_Get_DevNode_Status(out _, out var code, node, 0) == Success ? (int)code : 0;
            nodes.Add(new DeviceNode(id, Text(node, DescriptionKey), Text(node, FriendlyNameKey), Text(node, ClassKey),
                Text(node, DriverVersionKey), Text(node, DriverProviderKey), problem));
        }
        return nodes;
    }

    /// <summary>A present device's problem code (0 = working, 22 = disabled); null when it isn't there.</summary>
    internal static int? ProblemOf(string instanceId)
        => CM_Locate_DevNodeW(out var node, instanceId, 0) == Success && CM_Get_DevNode_Status(out _, out var problem, node, 0) == Success
            ? (int)problem
            : null;

    private static List<string> PresentIds()
    {
        for (var attempt = 0; attempt < 3; attempt++)   // the list can grow between the two calls
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, null, OnlyPresent) != Success || length == 0)
                return [];
            var buffer = new char[length];
            fixed (char* chars = buffer)
            {
                var result = CM_Get_Device_ID_ListW(null, chars, length, OnlyPresent);
                if (result == BufferSmall)
                    continue;
                if (result != Success)
                    return [];
            }
            return Hid.SplitMultiString(buffer);
        }
        return [];
    }

    /// <summary>A string property of a device node; null when it has none.</summary>
    private static string? Text(uint node, PropertyKey key)
    {
        Span<byte> small = stackalloc byte[512];
        var size = (uint)small.Length;
        uint type;
        uint result;
        fixed (byte* buffer = small)
            result = CM_Get_DevNode_PropertyW(node, &key, &type, buffer, &size, 0);
        if (result == Success)
            return type == StringType ? Decode(small[..(int)size]) : null;
        if (result != BufferSmall)
            return null;
        var large = new byte[size];
        fixed (byte* buffer = large)
            result = CM_Get_DevNode_PropertyW(node, &key, &type, buffer, &size, 0);
        return result == Success && type == StringType ? Decode(large.AsSpan(0, (int)size)) : null;
    }

    private static string? Decode(ReadOnlySpan<byte> bytes)
    {
        var text = MemoryMarshal.Cast<byte, char>(bytes).ToString().TrimEnd('\0');
        return text.Length == 0 ? null : text;
    }

    [LibraryImport(Libraries.CfgMgr32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [LibraryImport(Libraries.CfgMgr32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_ListW(string? filter, char* buffer, uint length, uint flags);

    [LibraryImport(Libraries.CfgMgr32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNodeW(out uint node, string id, uint flags);

    [LibraryImport(Libraries.CfgMgr32)]
    private static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    [LibraryImport(Libraries.CfgMgr32)]
    private static partial uint CM_Get_DevNode_PropertyW(uint node, PropertyKey* key, uint* type, byte* buffer, uint* size, uint flags);
}
