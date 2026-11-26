using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Art14Helper.Ui;

/// <summary>
/// The Windows 11 Settings look: colors for the dark and the light app mode (whichever
/// Windows uses), font families and rounded shapes. <see cref="Reload"/> picks up a change
/// of mode or accent.
/// </summary>
internal static class FluentTheme
{
    private static bool? _dark;
    private static Color? _accent;

    /// <summary>True when Windows apps use the dark mode (the default if it can't be read).</summary>
    internal static bool IsDark => _dark ??= ReadDark();

    /// <summary>Forget the cached mode and accent, after Windows' personalization changed.</summary>
    internal static void Reload()
    {
        _dark = null;
        _accent = null;
    }

    /// <summary>A fixed mode for previews and tests; null follows Windows.</summary>
    internal static bool? Forced { get; set; }

    private static bool ReadDark()
    {
        if (Forced is { } forced)
            return forced;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not 1;
    }

    private static Color Pick(int dark, int light) => Color.FromArgb(unchecked((int)0xFF000000) | (IsDark ? dark : light));

    internal static Color Background => Pick(0x202020, 0xF3F3F3);
    internal static Color NavSelected => Pick(0x2D2D2D, 0xEAEAEA);
    internal static Color NavHover => Pick(0x282828, 0xEDEDED);
    internal static Color Card => Pick(0x2B2B2B, 0xFBFBFB);
    internal static Color CardHover => Pick(0x323232, 0xF6F6F6);
    internal static Color CardPressed => Pick(0x292929, 0xF3F3F3);
    internal static Color Stroke => Pick(0x1D1D1D, 0xE5E5E5);
    internal static Color Divider => Pick(0x3A3A3A, 0xE0E0E0);
    internal static Color Text => Pick(0xFFFFFF, 0x1B1B1B);
    internal static Color TextSecondary => Pick(0xC5C5C5, 0x5D5D5D);
    internal static Color TextTertiary => Pick(0x9A9A9A, 0x767676);
    internal static Color TextDisabled => Pick(0x6E6E6E, 0xA0A0A0);
    internal static Color ControlStroke => Pick(0x9F9F9F, 0x878787);
    internal static Color ControlFill => Pick(0x1E1E1E, 0xFFFFFF);
    internal static Color Button => Pick(0x373737, 0xFDFDFD);
    internal static Color ButtonHover => Pick(0x3C3C3C, 0xF6F6F6);
    internal static Color ButtonPressed => Pick(0x2E2E2E, 0xF0F0F0);
    internal static Color ButtonDisabled => Pick(0x2E2E2E, 0xF0F0F0);
    internal static Color OnAccent => IsDark ? Color.Black : Color.White;
    internal static Color Danger => Pick(0xFF99A4, 0xC42B1C);
    internal static Color Success => Pick(0x6CCB5F, 0x0F7B0F);
    internal static Color SuccessFill => Pick(0x1F3A22, 0xDFF6DD);

    /// <summary>A soft fill behind accent-colored text (badges).</summary>
    internal static Color AccentFill => IsDark ? Shade(Accent, -0.72f) : Shade(Accent, 0.85f);

    /// <summary>
    /// The user's accent in the shade Windows uses for controls: lighter on dark backgrounds
    /// (accent palette entry 1), darker on light ones (entry 4). Windows blue if unreadable.
    /// </summary>
    internal static Color Accent => _accent ??= ReadAccent();

    private static Color ReadAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        var offset = IsDark ? 4 : 16;
        return key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= offset + 3
            ? Color.FromArgb(palette[offset], palette[offset + 1], palette[offset + 2])
            : IsDark ? Color.FromArgb(0x4C, 0xC2, 0xFF) : Color.FromArgb(0x00, 0x5F, 0xB8);
    }

    /// <summary><paramref name="color"/> moved toward white (positive) or black (negative) by a fraction.</summary>
    internal static Color Shade(Color color, float amount)
    {
        var target = amount >= 0 ? 255 : 0;
        var t = Math.Abs(amount);
        int Mix(int channel) => (int)Math.Round(channel + (target - channel) * t);
        return Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
    }

    private static string? _iconFamily, _textFamily, _strongFamily, _bodyStrongFamily;

    /// <summary>Segoe Fluent Icons; Windows 10 has the same symbols in Segoe MDL2 Assets.</summary>
    internal static string IconFamily => _iconFamily ??= Installed("Segoe Fluent Icons", "Segoe MDL2 Assets");

    internal static string TextFamily => _textFamily ??= Installed("Segoe UI Variable Text", "Segoe UI");

    internal static string StrongFamily => _strongFamily ??= Installed("Segoe UI Semibold", "Segoe UI");

    /// <summary>Windows 11's "Body Strong" (flyout and dialog labels); Segoe UI Semibold on Windows 10.</summary>
    internal static string BodyStrongFamily => _bodyStrongFamily ??= Installed("Segoe UI Variable Text Semibold", StrongFamily);

    // GDI (the windows) links Segoe UI to Windows' Chinese font by itself, so Latin keeps its
    // shapes and Chinese is consistent. GDI+ (the popup) has no such link and fills missing
    // characters from different fonts, so one line mixes weights: there, Chinese text uses
    // Windows' Chinese interface font outright.
    private const string ChineseFamily = "Microsoft YaHei UI";
    private static string? _chineseFamily;
    private static bool Chinese => L10n.Current == AppLanguage.ChineseSimplified;

    /// <summary>The family for GDI+ body text (the popup) in the current language.</summary>
    internal static string DrawingTextFamily => Chinese ? _chineseFamily ??= Installed(ChineseFamily, TextFamily) : TextFamily;

    /// <summary>The family for GDI+ strong text; with the Chinese font, <see cref="CreateFont"/> makes it bold.</summary>
    internal static string DrawingStrongFamily => Chinese ? DrawingTextFamily : BodyStrongFamily;

    /// <summary>After the language changed (nothing cached depends on it now; kept for clarity).</summary>
    internal static void ReloadFonts() => _chineseFamily = null;

    private static string Installed(string wanted, string otherwise)
    {
        using var probe = new Font(wanted, 10f);
        return probe.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ? wanted : otherwise;
    }

    /// <summary>A GDI+ font sized in pixels at 100%, for drawing that needs alpha (the popup).</summary>
    internal static Font CreateFont(string family, float pixels, float scale, bool strong = false)
        => new(family, pixels * scale, strong && family == ChineseFamily ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);   // it has no semibold face of its own

    internal static GraphicsPath RoundedRect(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(box);
            return path;
        }
        path.AddArc(box.X, box.Y, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Y, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.X, box.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
