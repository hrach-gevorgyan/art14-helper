using System.Runtime.InteropServices;
namespace Art14Helper;

/// <summary>
/// Minimal binding to the Intel Graphics Control Library (ControlLib.dll, installed
/// with the Intel graphics driver) for the display's hardware 3D LUT. HONOR PC
/// Manager uses the same block to apply the panel's factory sRGB/P3 calibration.
///
/// Structures are built as raw buffers at the offsets of igcl_api.h (x64), so no
/// managed struct layout has to match the C one. Sizes and offsets were checked
/// against the header with an MSVC-target compiler.
/// </summary>
internal sealed class IntelColorPipeline : IDisposable
{

    // Structure sizes (igcl_api.h, x64).
    private const int InitArgsSize = 36;
    private const int DisplayPropertiesSize = 200;
    private const int PixelFormatSize = 120;
    private const int ColorPrimariesSize = 72;
    private const int BlockConfigSize = 144;
    private const int ThreeDLutConfigSize = 24;
    private const int PipeGetConfigSize = 272;
    private const int PipeSetConfigSize = 32;

    private const int BlockType3DLut = 2;
    private const int QueryCapability = 0;
    private const int OperationRestoreDefault = 1;
    private const int OperationSetCustom = 2;
    private const int FlagPersistAcrossPowerEvents = 1;
    private const int DisplayActive = 1;
    private const int DisplayAttached = 2;


    private IntPtr _api;
    private IntPtr _panel;
    private uint _lutBlockId;
    internal int LutSize { get; private set; }

    private IntelColorPipeline()
    {
    }

    /// <summary>
    /// Opens the library and finds the built-in panel and its 3D LUT block.
    /// Returns null (and logs why) when any step is not available.
    /// </summary>
    internal static IntelColorPipeline? Open()
    {
        if (!IntelControlLibrary.IsPresent())
        {
            Log.Info("Color: ControlLib.dll not found (no Intel graphics driver)");
            return null;
        }

        var pipeline = new IntelColorPipeline();
        try
        {
            if (!pipeline.Init() || !pipeline.FindPanel() || !pipeline.FindLutBlock())
            {
                pipeline.Dispose();
                return null;
            }
            return pipeline;
        }
        catch (Exception exception)
        {
            Log.Error("Color: Intel control library failed", exception);
            pipeline.Dispose();
            return null;
        }
    }

    /// <summary>Whether the library loads and starts (no panel needed): for the self-test.</summary>
    internal static bool CanStart()
    {
        if (!IntelControlLibrary.IsPresent())
            return false;
        using var pipeline = new IntelColorPipeline();
        return pipeline.Init();
    }

    private bool Init()
    {
        var args = Marshal.AllocHGlobal(InitArgsSize);
        try
        {
            Zero(args, InitArgsSize);
            Marshal.WriteInt32(args, 0, InitArgsSize);
            Marshal.WriteByte(args, 4, 0);
            Marshal.WriteInt32(args, 8, 0x00010001); // CTL_IMPL_VERSION 1.1
            // ApplicationUID stays zero: the driver rejects IDs it doesn't know
            // (CTL_RESULT_ERROR_UNKNOWN_APPLICATION_UID), as Intel's samples show.
            var result = IntelControlLibrary.Init(args, out _api);
            if (result != IntelControlLibrary.Success)
            {
                Log.Info($"Color: ctlInit failed 0x{result:X}");
                _api = IntPtr.Zero;
                return false;
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(args);
        }
    }

    /// <summary>
    /// The built-in panel: the Intel output whose Windows encoder id is the
    /// internal display's target id. With a single active display that one is used.
    /// </summary>
    private bool FindPanel()
    {
        if (DisplayPanel.FindBuiltIn() is not { TargetId: var targetId, ActivePaths: var activePaths })
        {
            Log.Info("Color: built-in display is not active");
            return false;
        }

        var activeOutputs = new List<(IntPtr Output, uint EncoderId)>();
        uint deviceCount = 0;
        if (IntelControlLibrary.EnumerateDevices(_api, ref deviceCount, null) != IntelControlLibrary.Success || deviceCount == 0)
            return false;
        var devices = new IntPtr[deviceCount];
        if (IntelControlLibrary.EnumerateDevices(_api, ref deviceCount, devices) != IntelControlLibrary.Success)
            return false;

        var properties = Marshal.AllocHGlobal(DisplayPropertiesSize);
        try
        {
            foreach (var device in devices)
            {
                uint outputCount = 0;
                if (IntelControlLibrary.EnumerateDisplayOutputs(device, ref outputCount, null) != IntelControlLibrary.Success || outputCount == 0)
                    continue;
                var outputs = new IntPtr[outputCount];
                if (IntelControlLibrary.EnumerateDisplayOutputs(device, ref outputCount, outputs) != IntelControlLibrary.Success)
                    continue;

                foreach (var output in outputs)
                {
                    Zero(properties, DisplayPropertiesSize);
                    Marshal.WriteInt32(properties, 0, DisplayPropertiesSize);
                    if (IntelControlLibrary.GetDisplayProperties(output, properties) != IntelControlLibrary.Success)
                        continue;
                    var flags = Marshal.ReadInt32(properties, 48);
                    if ((flags & DisplayActive) == 0 || (flags & DisplayAttached) == 0)
                        continue;
                    activeOutputs.Add((output, (uint)Marshal.ReadInt32(properties, 8)));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }

        foreach (var (output, encoderId) in activeOutputs)
        {
            if (encoderId == targetId)
            {
                _panel = output;
                return true;
            }
        }

        if (activeOutputs.Count == 1 && activePaths == 1)
        {
            _panel = activeOutputs[0].Output;
            return true;
        }

        Log.Info($"Color: built-in panel not matched (target {targetId}, Intel outputs: "
            + string.Join(", ", activeOutputs.Select(o => o.EncoderId)) + ")");
        return false;
    }

    private bool FindLutBlock()
    {
        var args = Marshal.AllocHGlobal(PipeGetConfigSize);
        var blocks = IntPtr.Zero;
        try
        {
            PrepareGetArgs(args);
            var result = IntelControlLibrary.GetPixelTransformation(_panel, args);
            var count = Marshal.ReadInt32(args, 256);
            if (result != IntelControlLibrary.Success || count <= 0)
            {
                Log.Info($"Color: capability query failed 0x{result:X}, blocks {count}");
                return false;
            }

            blocks = Marshal.AllocHGlobal(count * BlockConfigSize);
            Zero(blocks, count * BlockConfigSize);
            for (var i = 0; i < count; i++)
                Marshal.WriteInt32(blocks, i * BlockConfigSize, BlockConfigSize);
            PrepareGetArgs(args);
            Marshal.WriteInt32(args, 256, count);
            Marshal.WriteIntPtr(args, 264, blocks);
            result = IntelControlLibrary.GetPixelTransformation(_panel, args);
            if (result != IntelControlLibrary.Success)
            {
                Log.Info($"Color: block query failed 0x{result:X}");
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                var block = blocks + i * BlockConfigSize;
                if (Marshal.ReadInt32(block, 12) != BlockType3DLut)
                    continue;
                _lutBlockId = (uint)Marshal.ReadInt32(block, 8);
                LutSize = Marshal.ReadInt32(block, 16 + 8);
                Log.Info($"Color: 3D LUT block {_lutBlockId}, {LutSize} samples per channel");
                return LutSize > 1;
            }

            Log.Info("Color: the display pipe has no 3D LUT block");
            return false;
        }
        finally
        {
            if (blocks != IntPtr.Zero)
                Marshal.FreeHGlobal(blocks);
            Marshal.FreeHGlobal(args);
        }
    }

    private static void PrepareGetArgs(IntPtr args)
    {
        Zero(args, PipeGetConfigSize);
        Marshal.WriteInt32(args, 0, PipeGetConfigSize);
        Marshal.WriteInt32(args, 8, QueryCapability);
        foreach (var format in new[] { 16, 136 })
        {
            Marshal.WriteInt32(args, format, PixelFormatSize);
            Marshal.WriteInt32(args, format + 32, ColorPrimariesSize);
        }
    }

    /// <summary>
    /// Loads a 3D LUT. Samples are ordered red outermost, then green, then blue,
    /// each value 0..1. Kept by the driver across sleep (persist flag).
    /// </summary>
    internal bool SetLut(double[] samples)
    {
        if (samples.Length != LutSize * LutSize * LutSize * 3)
            return false;

        var data = Marshal.AllocHGlobal(samples.Length * sizeof(double));
        var block = Marshal.AllocHGlobal(BlockConfigSize);
        var args = Marshal.AllocHGlobal(PipeSetConfigSize);
        try
        {
            Marshal.Copy(samples, 0, data, samples.Length);
            Zero(block, BlockConfigSize);
            Marshal.WriteInt32(block, 0, BlockConfigSize);
            Marshal.WriteInt32(block, 8, (int)_lutBlockId);
            Marshal.WriteInt32(block, 12, BlockType3DLut);
            Marshal.WriteInt32(block, 16, ThreeDLutConfigSize);
            Marshal.WriteInt32(block, 16 + 8, LutSize);
            Marshal.WriteIntPtr(block, 16 + 16, data);
            return SetConfig(args, block, OperationSetCustom, FlagPersistAcrossPowerEvents, "set");
        }
        finally
        {
            Marshal.FreeHGlobal(args);
            Marshal.FreeHGlobal(block);
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>Turns the 3D LUT back off: the panel's native colors.</summary>
    internal bool RestoreDefault()
    {
        var block = Marshal.AllocHGlobal(BlockConfigSize);
        var args = Marshal.AllocHGlobal(PipeSetConfigSize);
        try
        {
            Zero(block, BlockConfigSize);
            Marshal.WriteInt32(block, 0, BlockConfigSize);
            Marshal.WriteInt32(block, 8, (int)_lutBlockId);
            Marshal.WriteInt32(block, 12, BlockType3DLut);
            return SetConfig(args, block, OperationRestoreDefault, 0, "restore");
        }
        finally
        {
            Marshal.FreeHGlobal(args);
            Marshal.FreeHGlobal(block);
        }
    }

    private bool SetConfig(IntPtr args, IntPtr block, int operation, int flags, string what)
    {
        Zero(args, PipeSetConfigSize);
        Marshal.WriteInt32(args, 0, PipeSetConfigSize);
        Marshal.WriteInt32(args, 8, operation);
        Marshal.WriteInt32(args, 12, flags);
        Marshal.WriteInt32(args, 16, 1);
        Marshal.WriteIntPtr(args, 24, block);
        var result = IntelControlLibrary.SetPixelTransformation(_panel, args);
        if (result != IntelControlLibrary.Success)
            Log.Info($"Color: 3D LUT {what} failed 0x{result:X}");
        return result == IntelControlLibrary.Success;
    }

    private static void Zero(IntPtr buffer, int size)
    {
        for (var i = 0; i < size; i++)
            Marshal.WriteByte(buffer, i, 0);
    }

    public void Dispose()
    {
        if (_api != IntPtr.Zero)
        {
            try
            {
                IntelControlLibrary.Close(_api);
            }
            catch (Exception exception)
            {
                Log.Error("Color: ctlClose failed", exception);
            }
            _api = IntPtr.Zero;
        }
    }
}
