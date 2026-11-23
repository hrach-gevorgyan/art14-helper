namespace Art14Helper.Native;

/// <summary>
/// Names of the system DLLs the app calls. Win32 BOOL results are declared as int
/// (non-zero = success), so no call needs runtime marshalling.
/// </summary>
internal static class Libraries
{
    internal const string User32 = "user32.dll";
    internal const string Kernel32 = "kernel32.dll";
    internal const string Hid = "hid.dll";
    internal const string CfgMgr32 = "cfgmgr32.dll";
    internal const string Ole32 = "ole32.dll";
    internal const string WinTrust = "wintrust.dll";
    internal const string Gdi32 = "gdi32.dll";
    internal const string Dwmapi = "dwmapi.dll";
    internal const string UxTheme = "uxtheme.dll";
    internal const string Shell32 = "shell32.dll";
    internal const string Comdlg32 = "comdlg32.dll";
    internal const string Shcore = "shcore.dll";
    internal const string Mscms = "mscms.dll";
    internal const string OleAut32 = "oleaut32.dll";
    internal const string Advapi32 = "advapi32.dll";
    internal const string PowrProf = "powrprof.dll";
}
