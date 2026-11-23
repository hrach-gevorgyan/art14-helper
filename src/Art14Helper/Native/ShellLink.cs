using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Writes .lnk shortcuts through the shell's IShellLinkW and IPersistFile, called by vtable slot.</summary>
internal static unsafe partial class ShellLink
{
    private static readonly Guid LinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid LinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterface = new("0000010B-0000-0000-C000-000000000046");

    /// <summary>Creates or replaces the shortcut <paramref name="file"/> pointing at <paramref name="target"/>.</summary>
    internal static void Save(string file, string target, string workingFolder, string description)
    {
        Check(CoCreateInstance(in LinkClass, IntPtr.Zero, 0x1, in LinkInterface, out var link));   // CLSCTX_INPROC_SERVER
        var persist = IntPtr.Zero;
        try
        {
            SetText(link, 20, target);          // SetPath
            SetText(link, 9, workingFolder);    // SetWorkingDirectory
            SetText(link, 7, description);      // SetDescription
            fixed (char* icon = target)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)Slot(link, 17))(link, icon, 0));   // SetIconLocation
            Check(Marshal.QueryInterface(link, in PersistFileInterface, out persist));
            fixed (char* path = file)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)Slot(persist, 6))(persist, path, 1));   // IPersistFile.Save
        }
        finally
        {
            if (persist != IntPtr.Zero)
                Marshal.Release(persist);
            Marshal.Release(link);
        }
    }

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    private static void SetText(IntPtr link, int slot, string text)
    {
        fixed (char* chars = text)
            Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int>)Slot(link, slot))(link, chars));
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [LibraryImport(Libraries.Ole32)]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);
}
