using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// The Intel Graphics Control Library (ControlLib.dll, installed with the Intel driver),
/// only the calls the panel's 3D LUT needs. Loaded from System32 only, never from the
/// app folder. Arguments are raw buffers laid out by the caller.
/// </summary>
internal static partial class IntelControlLibrary
{
    internal const string File = "ControlLib.dll";
    internal const uint Success = 0;

    internal static bool IsPresent()
    {
        if (!NativeLibrary.TryLoad(File, typeof(IntelControlLibrary).Assembly, DllImportSearchPath.System32, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    [LibraryImport(File, EntryPoint = "ctlInit"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint Init(IntPtr initArgs, out IntPtr api);

    [LibraryImport(File, EntryPoint = "ctlClose"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint Close(IntPtr api);

    [LibraryImport(File, EntryPoint = "ctlEnumerateDevices"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint EnumerateDevices(IntPtr api, ref uint count, [In, Out] IntPtr[]? devices);

    [LibraryImport(File, EntryPoint = "ctlEnumerateDisplayOutputs"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint EnumerateDisplayOutputs(IntPtr device, ref uint count, [In, Out] IntPtr[]? outputs);

    [LibraryImport(File, EntryPoint = "ctlGetDisplayProperties"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDisplayProperties(IntPtr output, IntPtr properties);

    [LibraryImport(File, EntryPoint = "ctlPixelTransformationGetConfig"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetPixelTransformation(IntPtr output, IntPtr args);

    [LibraryImport(File, EntryPoint = "ctlPixelTransformationSetConfig"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetPixelTransformation(IntPtr output, IntPtr args);
}
