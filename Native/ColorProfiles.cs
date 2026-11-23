using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Windows color management: which ICC profiles a display uses, and its default.</summary>
internal static partial class ColorProfiles
{
    internal const int SystemWide = 0, CurrentUser = 1;   // WCS_PROFILE_MANAGEMENT_SCOPE

    /// <summary>Makes an installed profile the display's default SDR profile; returns an HRESULT.</summary>
    internal static int SetDefault(int scope, string profile, AdapterId adapter, uint sourceId)
        => Call(() => ColorProfileAddDisplayAssociation(scope, profile, adapter, sourceId, 1, 0));

    /// <summary>Takes a profile off the display's list; returns an HRESULT (also when it wasn't on it).</summary>
    internal static int Remove(int scope, string profile, AdapterId adapter, uint sourceId)
        => Call(() => ColorProfileRemoveDisplayAssociation(scope, profile, adapter, sourceId, 0));

    // Windows 10 before 2004 lacks these calls; there is nothing to manage then.
    private static int Call(Func<int> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return unchecked((int)0x80004001);   // E_NOTIMPL
        }
    }

    [LibraryImport(Libraries.Mscms, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ColorProfileAddDisplayAssociation(int scope, string profile, AdapterId adapter, uint sourceId,
        int setAsDefault, int advancedColor);

    [LibraryImport(Libraries.Mscms, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ColorProfileRemoveDisplayAssociation(int scope, string profile, AdapterId adapter, uint sourceId,
        int advancedColor);
}
