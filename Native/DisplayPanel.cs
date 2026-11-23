using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>An adapter id as Windows display configuration reports it (LUID).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AdapterId
{
    public uint Low;
    public int High;
}

/// <summary>Where the built-in panel sits in the current display configuration.</summary>
internal sealed record PanelPath(AdapterId Adapter, uint SourceId, uint TargetId, int ActivePaths, string? GdiName, string? DevicePath);

/// <summary>
/// The laptop's own panel through the Windows display configuration API: which active
/// path it is, whether HDR is on, and its refresh rate (changing that needs no admin rights).
/// </summary>
internal static unsafe partial class DisplayPanel
{
    private const uint OnlyActivePaths = 0x2;                 // QDC_ONLY_ACTIVE_PATHS
    private const uint TechnologyInternal = 0x80000000;       // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
    private const uint TechnologyEmbeddedDisplayPort = 11;    // ..._DISPLAYPORT_EMBEDDED
    private const int GetSourceName = 1, GetTargetName = 2, GetAdvancedColor = 9;   // DISPLAYCONFIG_DEVICE_INFO_*
    private const int CurrentMode = -1;                       // ENUM_CURRENT_SETTINGS
    private const uint FrequencyField = 0x400000;             // DM_DISPLAYFREQUENCY
    private const uint SaveToRegistry = 0x1;                  // CDS_UPDATEREGISTRY

    /// <summary>Highest rate seen; kept for the moment a mode list comes back empty (display waking up).</summary>
    private static int _highestSeen;

    /// <summary>The built-in panel's path, or null when it's off (lid closed with an external screen, etc.).</summary>
    internal static PanelPath? FindBuiltIn()
    {
        if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) != 0)
            return null;
        var paths = new PathInfo[pathCount];
        var modes = new byte[modeCount * ModeInfoSize];
        fixed (PathInfo* pathData = paths)
        fixed (byte* modeData = modes)
            if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, pathData, ref modeCount, modeData, IntPtr.Zero) != 0)
                return null;

        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var target = new TargetName { Header = Header(GetTargetName, sizeof(TargetName), path.TargetAdapter, path.TargetId) };
            if (DisplayConfigGetDeviceInfo(&target.Header) != 0
                || target.OutputTechnology is not (TechnologyInternal or TechnologyEmbeddedDisplayPort))
                continue;
            var source = new SourceName { Header = Header(GetSourceName, sizeof(SourceName), path.SourceAdapter, path.SourceId) };
            var gdiName = DisplayConfigGetDeviceInfo(&source.Header) == 0 ? new string(source.GdiDeviceName) : null;
            return new PanelPath(path.TargetAdapter, path.SourceId, path.TargetId, (int)pathCount, gdiName, new string(target.DevicePath));
        }
        return null;
    }

    /// <summary>Is HDR switched on for the panel? Color modes need SDR.</summary>
    internal static bool IsHdrOn()
    {
        if (FindBuiltIn() is not { } panel)
            return false;
        var color = new AdvancedColor { Header = Header(GetAdvancedColor, sizeof(AdvancedColor), panel.Adapter, panel.TargetId) };
        // Bit 1: advanced color enabled; bit 2: wide color forced (SDR wide gamut, not HDR).
        return DisplayConfigGetDeviceInfo(&color.Header) == 0 && (color.Flags & 0b110) == 0b010;
    }

    /// <summary>The panel's current refresh rate in Hz, or -1.</summary>
    internal static int CurrentRate()
        => FindBuiltIn()?.GdiName is { } name && ReadMode(name, CurrentMode, out var mode) ? (int)mode.DisplayFrequency : -1;

    /// <summary>The highest refresh rate any mode of the panel offers, or -1.</summary>
    internal static int HighestRate()
    {
        if (FindBuiltIn()?.GdiName is not { } name)
            return -1;
        var highest = -1;
        for (var index = 0; ReadMode(name, index, out var mode); index++)
            highest = Math.Max(highest, (int)mode.DisplayFrequency);
        if (highest > 0)
            _highestSeen = highest;
        return highest > 0 ? highest : (_highestSeen > 0 ? _highestSeen : -1);
    }

    /// <summary>Switches the panel to <paramref name="hz"/> in its current resolution and saves it; true when Windows accepted.</summary>
    internal static bool SetRate(int hz)
    {
        if (FindBuiltIn()?.GdiName is not { } name || !ReadMode(name, CurrentMode, out var mode))
            return false;
        if (mode.DisplayFrequency == hz)
            return true;
        mode.DisplayFrequency = (uint)hz;
        mode.Fields = FrequencyField;
        var result = ChangeDisplaySettingsExW(name, &mode, IntPtr.Zero, SaveToRegistry, IntPtr.Zero);
        Log.Info($"Refresh rate {hz} Hz: {(result == 0 ? "applied" : "refused, code " + result)}");
        return result == 0;
    }

    private static bool ReadMode(string gdiName, int index, out DevMode mode)
    {
        mode = new DevMode { Size = (ushort)sizeof(DevMode) };
        fixed (DevMode* data = &mode)
            return EnumDisplaySettingsW(gdiName, index, data) != 0;
    }

    private static DeviceInfoHeader Header(int type, int size, AdapterId adapter, uint id)
        => new() { Type = type, Size = (uint)size, Adapter = adapter, Id = id };

    private const int ModeInfoSize = 64;   // sizeof(DISPLAYCONFIG_MODE_INFO); its content isn't needed

    /// <summary>DISPLAYCONFIG_PATH_INFO</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public AdapterId SourceAdapter;
        public uint SourceId, SourceModeIndex, SourceFlags;
        public AdapterId TargetAdapter;
        public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetFlags, Flags;
    }

    /// <summary>DISPLAYCONFIG_DEVICE_INFO_HEADER</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public AdapterId Adapter;
        public uint Id;
    }

    /// <summary>DISPLAYCONFIG_TARGET_DEVICE_NAME</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TargetName
    {
        public DeviceInfoHeader Header;
        public uint Flags, OutputTechnology;
        public ushort EdidManufacturer, EdidProduct;
        public uint ConnectorInstance;
        public fixed char FriendlyName[64];
        public fixed char DevicePath[128];
    }

    /// <summary>DISPLAYCONFIG_SOURCE_DEVICE_NAME</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceName
    {
        public DeviceInfoHeader Header;
        public fixed char GdiDeviceName[32];
    }

    /// <summary>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColor
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    /// <summary>DEVMODEW, display layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DevMode
    {
        public fixed char DeviceName[32];
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        public fixed char FormName[32];
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [LibraryImport(Libraries.User32)]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport(Libraries.User32)]
    private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, PathInfo* paths, ref uint modeCount, byte* modes, IntPtr topology);

    [LibraryImport(Libraries.User32)]
    private static partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* request);

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int EnumDisplaySettingsW(string deviceName, int modeIndex, DevMode* mode);

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ChangeDisplaySettingsExW(string deviceName, DevMode* mode, IntPtr window, uint flags, IntPtr parameter);
}
