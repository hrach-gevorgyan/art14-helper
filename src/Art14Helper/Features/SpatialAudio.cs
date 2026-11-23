using Microsoft.Win32;

namespace Art14Helper;

/// <summary>
/// Spatial / stereo sound. PC Manager writes two DWORDs in HKLM (seen in
/// Procmon from MBAMessageCenter.exe); the audio driver picks them up live.
/// 11 = spatial, 1 = stereo. Writing HKLM needs admin, so the write goes
/// through the privileged task; reading does not.
/// </summary>
internal static class SpatialAudioController
{
    private const string AwinicKey = @"SOFTWARE\AwinicAPO\mode";
    private const string AwinicValue = "mode";
    private const string HonorKey = @"SOFTWARE\HonorAPO";
    private const string HonorValue = "HonorAlgoMode";
    private const int Spatial = 11;
    private const int Stereo = 1;

    /// <summary>Does this laptop have the HONOR/Awinic audio effect at all.</summary>
    internal static bool IsSupported()
    {
        using var awinic = Registry.LocalMachine.OpenSubKey(AwinicKey);
        using var honor = Registry.LocalMachine.OpenSubKey(HonorKey);
        return awinic is not null || honor is not null;
    }

    /// <summary>true = spatial, false = stereo, null = unknown.</summary>
    internal static bool? IsSpatial()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AwinicKey)
                ?? Registry.LocalMachine.OpenSubKey(HonorKey);
            var raw = key?.GetValue(AwinicValue) ?? key?.GetValue(HonorValue);
            return raw is int value ? value == Spatial : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void SetSpatial(bool spatial)
    {
        var value = spatial ? Spatial : Stereo;
        using (var awinic = Registry.LocalMachine.CreateSubKey(AwinicKey, true))
            awinic.SetValue(AwinicValue, value, RegistryValueKind.DWord);
        using (var honor = Registry.LocalMachine.CreateSubKey(HonorKey, true))
            honor.SetValue(HonorValue, value, RegistryValueKind.DWord);
    }
}
