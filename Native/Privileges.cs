using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Turns on the process's privileges for one call, on the calling thread only.</summary>
internal static unsafe partial class Privileges
{
    /// <summary>
    /// Runs <paramref name="call"/> while the thread impersonates the process with every
    /// privilege it holds enabled (an elevated token has most of them present but off).
    /// Without the rights to do so the call simply runs as is.
    /// </summary>
    internal static T WithAllEnabled<T>(Func<T> call)
    {
        if (ImpersonateSelf(2) == 0)   // SecurityImpersonation
            return call();
        try
        {
            EnableAll();
            return call();
        }
        finally
        {
            RevertToSelf();
        }
    }

    private static void EnableAll()
    {
        const uint AdjustAndQuery = 0x0020 | 0x0008;   // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
        if (OpenThreadToken(GetCurrentThread(), AdjustAndQuery, 1, out var token) == 0)
            return;
        try
        {
            GetTokenInformation(token, 3, null, 0, out var size);   // TokenPrivileges
            if (size == 0)
                return;
            var buffer = stackalloc byte[(int)size];
            if (GetTokenInformation(token, 3, buffer, size, out _) == 0)
                return;
            // TOKEN_PRIVILEGES: a count, then 12-byte entries (8-byte LUID, 4-byte attributes).
            var count = *(uint*)buffer;
            for (var i = 0; i < count; i++)
                *(uint*)(buffer + 4 + i * 12 + 8) |= 0x2;   // SE_PRIVILEGE_ENABLED
            AdjustTokenPrivileges(token, 0, buffer, size, null, null);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [LibraryImport(Libraries.Advapi32)]
    private static partial int ImpersonateSelf(int level);

    [LibraryImport(Libraries.Advapi32)]
    private static partial int RevertToSelf();

    [LibraryImport(Libraries.Advapi32)]
    private static partial int OpenThreadToken(IntPtr thread, uint access, int openAsSelf, out IntPtr token);

    [LibraryImport(Libraries.Advapi32)]
    private static partial int GetTokenInformation(IntPtr token, int infoClass, byte* buffer, uint length, out uint needed);

    [LibraryImport(Libraries.Advapi32)]
    private static partial int AdjustTokenPrivileges(IntPtr token, int disableAll, byte* state, uint length, byte* previous, uint* returned);

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport(Libraries.Kernel32)]
    private static partial int CloseHandle(IntPtr handle);
}
