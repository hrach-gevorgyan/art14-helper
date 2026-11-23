namespace Art14Helper;

/// <summary>What the top row of keys does without Fn.</summary>
internal enum FnKeyMode
{
    /// <summary>F1-F12 work as F-keys (F5 reloads a page). BIOS value 1.</summary>
    Standard = 1,

    /// <summary>Keys change volume, brightness and so on (F5 = volume). BIOS value 2.</summary>
    Media = 2
}

/// <summary>
/// Fn lock. PC Manager (MBAMessageCenter.exe, OnRecvFnSet) calls BIOS WMI
/// with 0x10704 for standard keys and 0x20704 for media keys. Verified on the
/// Art 14: after 0x10704 F5 reloads the page, after 0x20704 F5 changes volume.
/// Tapping Fn switches the mode in firmware as well (LED on = standard).
/// </summary>
internal static class FnLockController
{
    private const ulong SetCommand = 0x0704;

    internal static void SetMode(FnKeyMode mode)
        => Bios.Run(SetCommand | ((ulong)mode << 16));
}
