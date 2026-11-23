using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
namespace Art14Helper;

internal enum ColorModeResult
{
    Applied,
    HdrOn,
    Failed
}

internal enum ColorMode
{
    Native = 0,
    Srgb = 1,
    P3 = 2,
    AdobeRgb = 3
}

/// <summary>
/// Display color modes as in HONOR PC Manager. sRGB and P3 load the panel's own
/// factory calibration (a 17x17x17 3D LUT in the color folder, written at the
/// factory for this exact screen) into the Intel display pipe; Native turns the
/// LUT off. The matching HONOR color profile is also set as the display's default,
/// so color-managed apps know which colors the screen shows.
/// </summary>
internal static class ColorModeService
{
    /// <summary>The recommended mode until the user picks one: P3, the panel's full gamut, calibrated.</summary>
    internal const ColorMode DefaultMode = ColorMode.P3;

    private static readonly object Gate = new();
    // -1 not checked yet, 0 no, 1 yes. An int, so the menu (UI thread) never reads
    // a half-written value while the startup probe sets it.
    private static volatile int _support = -1;

    internal static string ColorDirectory
        => Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color");

    /// <summary>Null until the startup probe has run.</summary>
    internal static bool? IsSupported => _support < 0 ? null : _support == 1;

    internal static ColorMode? Current => AppSettings.ColorMode;

    /// <summary>
    /// Startup: checks support once and applies the saved mode. Never chosen yet (fresh install) means sRGB: most Windows
    /// content is sRGB and most apps aren't color-managed.
    /// </summary>
    internal static void Initialize()
    {
        lock (Gate)
        {
            // HONOR's factory tables are used when present; otherwise the app builds
            // its own from the panel's color points. Without either, no color modes.
            if ((CalibrationFile(ColorMode.Srgb) is null || CalibrationFile(ColorMode.P3) is null)
                && PanelColorimetry.Read() is null)
            {
                Log.Info("Color: no calibration and no panel color data");
                _support = 0;
                return;
            }
            PanelColorimetry.Read(); // logs the panel's color points
            if (!IntelControlLibrary.IsPresent())
            {
                _support = 0;
                return;
            }

            // Not opening now can be passing: the built-in screen is off (lid closed on an external
            // monitor) or the driver is still starting. Then the next display change tries again.
            using var pipeline = IntelColorPipeline.Open();
            if (pipeline is null)
            {
                Log.Info("Color: not available yet, trying again on the next display change");
                return;
            }
            _support = 1;
            if (DisplayPanel.IsHdrOn())
            {
                Log.Info("Color: HDR is on, color mode waits until it is off");
                return;
            }
            var mode = AppSettings.ColorMode ?? DefaultMode;
            if (ApplyCore(pipeline, mode))
                AppSettings.ColorMode = mode;
        }
    }

    private static int _restorePending;
    private static long _lastApplyTick;

    /// <summary>Display change: one restore a moment later, however many events arrive.</summary>
    internal static void RestoreSoon()
    {
        // A change caused by our own LUT load must not start another load.
        if (_support == 0 || Environment.TickCount64 - Interlocked.Read(ref _lastApplyTick) < 4000
            || Interlocked.Exchange(ref _restorePending, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Interlocked.Exchange(ref _restorePending, 0);
            Restore();
        });
    }

    /// <summary>After sleep or a display change: puts the saved mode back.</summary>
    internal static void Restore()
    {
        if (_support == 0 || DisplayPanel.IsHdrOn())
            return;
        // Never applied yet (the first start was in HDR): the sRGB default now.
        var mode = AppSettings.ColorMode ?? DefaultMode;
        lock (Gate)
        {
            using var pipeline = IntelColorPipeline.Open();
            if (pipeline is null)
                return;
            _support = 1;   // also when the startup probe found no screen yet
            if (ApplyCore(pipeline, mode))
                AppSettings.ColorMode = mode;
        }
    }

    internal static ColorModeResult Set(ColorMode mode)
    {
        // Intel's 3D LUT works only in SDR; in HDR Windows manages the colors.
        if (DisplayPanel.IsHdrOn())
            return ColorModeResult.HdrOn;
        lock (Gate)
        {
            using var pipeline = IntelColorPipeline.Open();
            if (pipeline is null || !ApplyCore(pipeline, mode))
                return ColorModeResult.Failed;
            _support = 1;
            AppSettings.ColorMode = mode;
            return ColorModeResult.Applied;
        }
    }

    /// <summary>
    /// Uninstall: back to the panel's native colors and HONOR's own default profile, and
    /// the profiles the app generated are removed from Windows.
    /// </summary>
    internal static void ResetToNative()
    {
        try
        {
            lock (Gate)
            {
                using var pipeline = IntelColorPipeline.Open();
                pipeline?.RestoreDefault();
                RemoveOwnProfiles();
            }
        }
        catch (Exception exception)
        {
            Log.Error("Color: reset failed", exception);
        }
    }

    // The last table built and the last profile made default (both under Gate). A restore
    // after wake or a display change sends the same table again without rebuilding it from
    // the files, and leaves the profile alone.
    private static (ColorMode Mode, bool NativeWhite, int Size, double[] Samples)? _table;
    private static (ColorMode Mode, bool NativeWhite)? _profile;

    private static bool ApplyCore(IntelColorPipeline pipeline, ColorMode mode)
    {
        Interlocked.Exchange(ref _lastApplyTick, Environment.TickCount64);
        var nativeWhite = mode != ColorMode.Native && AppSettings.ColorNativeWhite;
        bool applied;
        if (mode == ColorMode.Native)
        {
            applied = pipeline.RestoreDefault();
        }
        else
        {
            var samples = _table is { } known && known.Mode == mode && known.NativeWhite == nativeWhite && known.Size == pipeline.LutSize
                ? known.Samples
                : null;
            if (samples is null)
            {
                samples = LoadCalibration(mode, pipeline.LutSize);
                if (samples is not null && nativeWhite)
                    samples = ColorMath.KeepNativeWhite(samples);
                if (samples is not null)
                    _table = (mode, nativeWhite, pipeline.LutSize, samples);
            }
            applied = samples is not null && pipeline.SetLut(samples);
        }

        var white = nativeWhite ? ", native white" : mode != ColorMode.Native ? ", D65 white" : "";
        Log.Info($"Color: {mode}{white} " + (applied ? "applied" : "failed"));
        // Remembered only when it worked, so a failure (panel not found yet, say) is retried.
        if (applied && _profile != (mode, nativeWhite) && SetDefaultProfile(ProfileName(mode)))
            _profile = (mode, nativeWhite);
        Interlocked.Exchange(ref _lastApplyTick, Environment.TickCount64);
        return applied;
    }

    private const string SrgbProfile = "sRGB Color Space Profile.icm"; // part of Windows

    /// <summary>
    /// The profile that describes what the screen shows in each mode, for color-
    /// managed apps. sRGB with the standard white uses Windows' own; every other
    /// case is generated by the app (gamma 2.2) and written to the color folder.
    /// </summary>
    private static string ProfileName(ColorMode mode)
    {
        var panel = PanelColorimetry.Read();
        if (mode == ColorMode.Native)
        {
            if (panel is null)
                return SrgbProfile;
            return Generated("Art14 Helper Native", panel);
        }

        var target = Target(mode);
        if (!AppSettings.ColorNativeWhite)
            return mode == ColorMode.Srgb ? SrgbProfile : Generated($"Art14 Helper {target.Source}", target);

        // Native white: the target's colors with the panel's own white.
        var white = panel?.White ?? target.White;
        return Generated($"Art14 Helper {target.Source} native white", target with { White = white });
    }

    private static Chromaticities Target(ColorMode mode) => mode switch
    {
        ColorMode.P3 => Chromaticities.DisplayP3,
        ColorMode.AdobeRgb => Chromaticities.AdobeRgb,
        _ => Chromaticities.Srgb
    };

    private static string Generated(string description, Chromaticities space)
    {
        var name = description + ".icc";
        WriteProfile(name, ColorMath.BuildIccProfile(space, description));
        return name;
    }

    /// <summary>Whether a mode can be offered: Adobe RGB needs the panel's color points.</summary>
    internal static bool IsAvailable(ColorMode mode)
        => mode != ColorMode.AdobeRgb || PanelColorimetry.Read() is not null;

    /// <summary>Turns "keep native white" on or off and re-applies the current mode.</summary>
    /// Kept only when it worked, so the menu never shows a white the screen doesn't have.
    internal static ColorModeResult SetNativeWhite(bool on)
    {
        var before = AppSettings.ColorNativeWhite;
        AppSettings.ColorNativeWhite = on;   // Set reads it
        var mode = AppSettings.ColorMode ?? DefaultMode;
        var result = mode == ColorMode.Native ? ColorModeResult.Applied : Set(mode);
        if (result != ColorModeResult.Applied)
            AppSettings.ColorNativeWhite = before;
        return result;
    }

    private static void WriteProfile(string name, byte[] data)
    {
        try
        {
            var path = Path.Combine(ColorDirectory, name);
            // The header holds the creation time: compare without it (bytes 24-35).
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == data.Length
                    && existing.AsSpan(0, 24).SequenceEqual(data.AsSpan(0, 24))
                    && existing.AsSpan(36).SequenceEqual(data.AsSpan(36)))
                    return;
            }
            // Removed and created anew instead of overwritten: users may add files to this folder,
            // and a link planted under this name must not carry the write to another file.
            File.Delete(path);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(data);
        }
        catch (Exception exception)
        {
            Log.Error($"Color: could not write {name}", exception);
        }
    }

    private static readonly SecurityIdentifier[] TrustedOwners =
    [
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new(WellKnownSidType.LocalSystemSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),   // TrustedInstaller
    ];

    internal static bool IsOwnedBySystem(string path)
    {
        var owner = new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        if (owner is SecurityIdentifier sid && TrustedOwners.Contains(sid))
            return true;
        Log.Info($"Color: ignoring {Path.GetFileName(path)}, which an administrator doesn't own");
        return false;
    }

    private static string? CalibrationFile(ColorMode mode)
    {
        try
        {
            var suffix = mode == ColorMode.P3 ? "P3" : "sRGB";
            // Users may add files to the color folder, so only a file an administrator (or Windows)
            // owns counts as the factory measurement.
            return Directory.GetFiles(ColorDirectory, $"*_display_factory_calibration_{suffix}.xml").FirstOrDefault(IsOwnedBySystem);
        }
        catch (Exception exception)
        {
            Log.Error("Color: could not list the color folder", exception);
            return null;
        }
    }

    /// <summary>
    /// Reads GMP_Feature: size^3 RGB triplets of 12-bit values, red in the outer
    /// loop, then green, then blue (the same order the Intel API expects).
    /// </summary>
    internal static double[]? ParseCalibration(string xml, int size)
    {
        var text = XDocument.Parse(xml).Root?.Element("GMP_Feature")?.Value;
        if (text is null)
            return null;

        var parts = text.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var expected = size * size * size * 3;
        if (parts.Length != expected)
        {
            Log.Info($"Color: calibration has {parts.Length} values, expected {expected}");
            return null;
        }

        var samples = new double[expected];
        for (var i = 0; i < expected; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                || value < 0 || value > 4095)
                return null;
            samples[i] = value / 4095.0;
        }
        return samples;
    }

    /// <summary>
    /// Where a gamma 2.2 signal lands on the sRGB curve: the same light, expressed
    /// as the input an sRGB-target table expects. Equal at 0 and 1, differs mainly
    /// near black.
    /// </summary>
    internal static double Gamma22ToSrgbInput(double x)
    {
        if (x <= 0)
            return 0;
        if (x >= 1)
            return 1;
        var linear = Math.Pow(x, 2.2);
        return linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
    }

    /// <summary>
    /// Re-samples a size^3 table so its input follows gamma 2.2 instead of the sRGB
    /// curve (trilinear interpolation of the original table; colors unchanged).
    /// </summary>
    internal static double[] ToGamma22(double[] table, int size)
    {
        var result = new double[table.Length];
        var max = size - 1;
        double Sample(int r, int g, int b, int channel) => table[((r * size + g) * size + b) * 3 + channel];

        var map = new double[size];
        for (var i = 0; i < size; i++)
            map[i] = Gamma22ToSrgbInput(i / (double)max) * max;

        for (var r = 0; r < size; r++)
        for (var g = 0; g < size; g++)
        for (var b = 0; b < size; b++)
        {
            var (r0, rf) = Split(map[r], max);
            var (g0, gf) = Split(map[g], max);
            var (b0, bf) = Split(map[b], max);
            int r1 = Math.Min(r0 + 1, max), g1 = Math.Min(g0 + 1, max), b1 = Math.Min(b0 + 1, max);
            for (var c = 0; c < 3; c++)
            {
                var c00 = Sample(r0, g0, b0, c) * (1 - bf) + Sample(r0, g0, b1, c) * bf;
                var c01 = Sample(r0, g1, b0, c) * (1 - bf) + Sample(r0, g1, b1, c) * bf;
                var c10 = Sample(r1, g0, b0, c) * (1 - bf) + Sample(r1, g0, b1, c) * bf;
                var c11 = Sample(r1, g1, b0, c) * (1 - bf) + Sample(r1, g1, b1, c) * bf;
                var c0 = c00 * (1 - gf) + c01 * gf;
                var c1 = c10 * (1 - gf) + c11 * gf;
                result[((r * size + g) * size + b) * 3 + c] = c0 * (1 - rf) + c1 * rf;
            }
        }
        return result;

        static (int Index, double Fraction) Split(double position, int max)
        {
            var index = Math.Clamp((int)Math.Floor(position), 0, max);
            return (index, Math.Clamp(position - index, 0, 1));
        }
    }

    /// <summary>
    /// HONOR's factory table for this panel when it's on the laptop (most precise;
    /// sRGB with gamma 2.2 tones), otherwise the app's own, built from the panel's
    /// color points.
    /// </summary>
    private static double[]? LoadCalibration(ColorMode mode, int size)
    {
        var file = mode == ColorMode.AdobeRgb ? null : CalibrationFile(mode);
        if (file is not null)
        {
            try
            {
                var samples = ParseCalibration(File.ReadAllText(file), size);
                if (samples is not null)
                {
                    // HONOR's sRGB table follows the official sRGB curve, which is much
                    // brighter than gamma 2.2 just above black (a 6% gray came out at
                    // 10%): dark scenes looked washed out. Same colors, gamma 2.2 tones.
                    return mode == ColorMode.Srgb ? ToGamma22(samples, size) : samples;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"Color: could not read {file}", exception);
            }
        }

        if (PanelColorimetry.Read() is not { } panel)
            return null;
        Log.Info($"Color: building {mode} from the {panel.Source}");
        return ColorMath.BuildLut(panel, Target(mode), size);
    }

    // ---- Windows color management: default profile of the built-in display ----

    private const string HonorDefaultProfile = "HonorNative.icc";   // installed by HONOR

    /// <summary>Takes the app's generated profiles off the display and out of the color folder.</summary>
    private static void RemoveOwnProfiles()
    {
        var panel = DisplayPanel.FindBuiltIn();
        foreach (var file in Directory.GetFiles(ColorDirectory, "Art14 Helper *.icc"))
        {
            var name = Path.GetFileName(file);
            if (panel is not null)
                foreach (var scope in new[] { ColorProfiles.SystemWide, ColorProfiles.CurrentUser })
                    ColorProfiles.Remove(scope, name, panel.Adapter, panel.SourceId);
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Color: {name} not removed", exception);
            }
        }
        SetDefaultProfile(File.Exists(Path.Combine(ColorDirectory, HonorDefaultProfile)) ? HonorDefaultProfile : SrgbProfile);
        _profile = null;
    }

    /// <summary>Makes a profile the panel's default for the system and the user; false if either failed.</summary>
    private static bool SetDefaultProfile(string profileName)
    {
        if (!File.Exists(Path.Combine(ColorDirectory, profileName)) || DisplayPanel.FindBuiltIn() is not { } panel)
            return false;
        var done = true;
        foreach (var scope in new[] { ColorProfiles.SystemWide, ColorProfiles.CurrentUser })
        {
            var result = ColorProfiles.SetDefault(scope, profileName, panel.Adapter, panel.SourceId);
            if (result != 0)
            {
                done = false;
                Log.Info($"Color: profile {profileName} (scope {scope}) not set as default: 0x{result:X}");
            }
        }
        return done;
    }
}
