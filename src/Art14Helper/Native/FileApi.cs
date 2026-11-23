using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>File operations .NET has no API for.</summary>
internal static partial class FileApi
{
    /// <summary>Marks a file that's in use to be deleted when Windows starts next time.</summary>
    internal static bool DeleteAtRestart(string path) => MoveFileExW(path, null, MovefileDelayUntilReboot) != 0;

    private const uint MovefileDelayUntilReboot = 0x4;

    [LibraryImport(Libraries.Kernel32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MoveFileExW(string existing, string? replacement, uint flags);
}
