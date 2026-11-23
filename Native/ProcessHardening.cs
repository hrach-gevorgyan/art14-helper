using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// Windows' own protections, switched on first thing in Main. The exe may run with admin
/// rights from a folder the user can write to (setup, a UAC-started change), so:
/// - DLLs load from System32 only, never from the exe's folder or the current directory;
/// - with admin rights, the loader prefers System32 copies of DLLs, and the process doesn't
///   follow junctions that a program without admin rights created ("redirection trust"),
///   so no such program can steer an elevated file operation elsewhere.
/// Older Windows versions that lack a protection simply don't get it.
/// </summary>
internal static unsafe partial class ProcessHardening
{
    private const uint SearchSystem32 = 0x00000800;                 // LOAD_LIBRARY_SEARCH_SYSTEM32
    private const int ImageLoadPolicy = 10, RedirectionTrustPolicy = 16;   // PROCESS_MITIGATION_POLICY
    private const uint PreferSystem32Images = 0x4, EnforceRedirectionTrust = 0x1;

    /// <summary>What was switched on, for the log.</summary>
    internal static string Apply()
    {
        var applied = new List<string>();
        if (SetDefaultDllDirectories(SearchSystem32) != 0)
            applied.Add("System32-only DLLs");
        if (Privileged.IsElevated)
        {
            var images = PreferSystem32Images;
            if (SetProcessMitigationPolicy(ImageLoadPolicy, &images, sizeof(uint)) != 0)
                applied.Add("System32 images first");
            var trust = EnforceRedirectionTrust;
            if (SetProcessMitigationPolicy(RedirectionTrustPolicy, &trust, sizeof(uint)) != 0)
                applied.Add("redirection trust");
        }
        return string.Join(", ", applied);
    }

    [LibraryImport(Libraries.Kernel32)]
    private static partial int SetDefaultDllDirectories(uint flags);

    [LibraryImport(Libraries.Kernel32)]
    private static partial int SetProcessMitigationPolicy(int policy, void* buffer, nuint length);
}
