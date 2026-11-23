using Win32 = Microsoft.Win32;

namespace Art14Helper;

/// <summary>
/// The F8 key: a privacy switch for the camera. HONOR's camera driver reads a registry
/// value on every frame (CameraStatus: 1 = picture, 0 = black frames), so flipping it
/// takes effect at once, even in a running call, while the camera stays a working
/// device for Windows. The key lives in HKLM, so writing needs admin rights.
/// </summary>
internal static class CameraSwitch
{
    private const string KeyPath = @"SOFTWARE\HONOR\ASvidDMFT\Settings";
    private const string ValueName = "CameraStatus";
    private static readonly Lock ToggleLock = new();

    /// <summary>True when apps get black frames. A missing value (other camera driver) counts as on.</summary>
    internal static bool IsOff()
    {
        try
        {
            using var key = Win32.Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Camera switch unreadable", exception);
            return false;
        }
    }

    /// <summary>Writes the switch. Needs admin rights; run through <see cref="Privileged.CameraOff"/>.</summary>
    internal static void Set(bool off)
    {
        using var key = Win32.Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, off ? 0 : 1, Win32.RegistryValueKind.DWord);
    }

    /// <summary>Flips the switch: true means the camera is now off; null if nothing changed.</summary>
    internal static bool? Toggle()
    {
        lock (ToggleLock)
        {
            var off = !IsOff();
            try
            {
                // Asking for UAC is fine here (no admin rights case): the user pressed the key.
                if (!Privileged.CameraOff.Run(off))
                {
                    Log.Error("Camera switch not changed");
                    return null;
                }
            }
            catch (Exception exception)
            {
                Log.Error("Camera switch not changed", exception);   // includes "No" to UAC
                return null;
            }
            Log.Info(off ? "Camera turned off with F8" : "Camera turned on with F8");
            return off;
        }
    }
}
