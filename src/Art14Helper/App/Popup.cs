using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Art14Helper;

/// <summary>What a popup is about: each state has its own icon, so on and off read at a glance.</summary>
internal enum PopupIcon
{
    Info,
    Warning,
    Done,
    Copied,
    KeyboardLight,
    KeyboardLightOff,
    Microphone,
    MicrophoneOff,
    Camera,
    CameraOff,
    Touchpad,
    TouchpadOff,
    Screenshot,
    Battery,
    BatteryCharging,
    AwakeEnded
}

/// <summary>
/// The app's on-screen popup, only for what the user didn't just do and can't see otherwise:
/// a hardware key or gesture, something that changed by itself (the charger, Auto's refresh
/// rate, keep awake running out), a choice that didn't apply, or an unseen result (copied).
/// Choices made in the menu or the settings window get none: they show there. Drawn in the style of Windows 11's
/// own volume and brightness flyouts, with Windows' own theme values: the Acrylic default fill,
/// the surface stroke, the flyout shadow, Body Strong and Caption type, text-colored icons
/// (gray with a slash for "off", amber for a problem), and an accent level bar on the
/// control-strong track. A short title, then a level bar or a short detail. It fades in and
/// out like Windows' flyouts. One
/// popup at a time: a new one replaces the one showing. It sits above the zone where
/// Windows draws its own flyouts, so the two never cover each other.
/// It never takes focus or clicks. Safe to call from any thread.
/// </summary>
internal static class Popup
{
    private const uint VisibleMilliseconds = 2200;

    // Layout at 100% scale, in pixels.
    private const float Shadow = 20, ShadowDrop = 4, Height = 48, Pad = 18, IconSize = 16, IconBox = 20, Gap = 12, Between = 28,
        TitleSize = 14, DetailSize = 12, BarWidth = 96, BarHeight = 4, MinWidth = 220, MaxWidth = 440;
    // Bottom edge of the pill above the work area's bottom, clear of Windows' own flyouts.
    private const float AboveTaskbar = 112;
    // How far the pill rises into place as it fades in, like Windows' own flyouts.
    private const float Rise = 16;

    private static Action<Action>? _onUiThread;
    private static LayeredWindow? _window;

    /// <summary>Connects the popup to the UI thread; until then (and after <see cref="Detach"/>) popups are dropped.</summary>
    internal static void Attach(Action<Action> runOnUiThread) => _onUiThread = runOnUiThread;

    internal static void Detach()
    {
        _onUiThread = null;
        _window?.Dispose();
        _window = null;
    }

    /// <summary>Something the user asked for didn't work; the reason is in the log.</summary>
    internal static void Failed(string title) => Show(PopupIcon.Warning, title, "See the log");

    /// <param name="detail">A few words on the right ("24 min to 70%"); keep it short.</param>
    /// <param name="level">0 to 1: a level bar on the right instead of the detail.</param>
    /// <param name="battery">Battery popups: the charge, so the icon shows the level (and a bolt while charging).</param>
    internal static void Show(PopupIcon icon, string title, string? detail = null, float? level = null, int? battery = null)
        => _onUiThread?.Invoke(() => ShowHere(icon, title, detail, level, battery));

    private static void ShowHere(PopupIcon icon, string title, string? detail, float? level, int? battery)
    {
        try
        {
            _window ??= new LayeredWindow();
            // The primary display's scale now (the system DPI stays at its sign-in value).
            var (area, scale) = User32.MonitorAt(Point.Empty);
            using var pill = Render(icon, title, detail, scale, level, battery);
            var corner = new Point(area.Left + (area.Width - pill.Width) / 2,
                area.Bottom - pill.Height + (int)(Shadow * scale) - (int)(AboveTaskbar * scale));
            _window.Present(pill, corner, rise: (int)(Rise * scale));
            _window.HideAfter(VisibleMilliseconds);
        }
        catch (Exception exception)
        {
            Log.Error($"Popup \"{title}\" not shown", exception);
        }
    }

    /// <summary>Draws the pill, shadow margin included, at <paramref name="scale"/> (1.0 = 96 dpi).</summary>
    internal static Bitmap Render(PopupIcon icon, string title, string? detail, float scale, float? level = null, int? battery = null)
    {
        var dark = FluentTheme.IsDark;
        using var titleFont = FluentTheme.CreateFont(FluentTheme.BodyStrongFamily, TitleSize, scale);
        using var detailFont = FluentTheme.CreateFont(FluentTheme.TextFamily, DetailSize, scale);
        using var glyphFont = new Font(FluentTheme.IconFamily, IconSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        // Width follows the content; a long detail is cut with an ellipsis at the maximum.
        float titleWidth, detailWidth = 0;
        using (var measure = Graphics.FromHwnd(IntPtr.Zero))
        {
            titleWidth = measure.MeasureString(title, titleFont).Width / scale;
            if (level is null && !string.IsNullOrEmpty(detail))
                detailWidth = measure.MeasureString(detail, detailFont).Width / scale;
        }
        var right = level is not null ? BarWidth : detailWidth;
        var pillWidth = Math.Clamp(Pad + IconBox + Gap + titleWidth + (right > 0 ? Between + right : 0) + Pad, MinWidth, MaxWidth);
        var s = Shadow * scale;
        var pill = new RectangleF(s, s, pillWidth * scale, Height * scale);
        var image = new Bitmap((int)Math.Ceiling(pill.Width + 2 * s), (int)Math.Ceiling(pill.Height + 2 * s),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // grayscale: ClearType fringes on a see-through window
        g.Clear(Color.Transparent);
        var radius = pill.Height / 2;

        // The flyout shadow: soft, a little below, about 26% dark and 14% light at the edge, fading
        // out smoothly. Nested rings each add the drop in opacity between their edge and the next.
        var peak = dark ? 0.26f : 0.14f;
        float Opacity(float distance) => distance >= s ? 0 : peak * (1 - distance / s) * (1 - distance / s);
        var step = Math.Max(1f, scale);
        for (var i = s; i > 0; i -= step)
        {
            var alpha = (Opacity(i - step) - Opacity(i)) * 255;
            using var ring = FluentTheme.RoundedRect(RectangleF.Inflate(new RectangleF(pill.X, pill.Y + ShadowDrop * scale, pill.Width, pill.Height), i, i), radius + i);
            using var shade = new SolidBrush(Color.FromArgb((int)Math.Clamp(alpha, 0, 255), 0, 0, 0));
            g.FillPath(shade, ring);
        }
        using (var shape = FluentTheme.RoundedRect(pill, radius))
        using (var fill = new SolidBrush(dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xFC, 0xFC, 0xFC)))   // AcrylicBackgroundFillColorDefault
        using (var edge = new Pen(Color.FromArgb(0x66, 0x75, 0x75, 0x75), Math.Max(1f, scale)))                                // SurfaceStrokeColorDefault
        {
            g.FillPath(fill, shape);
            g.DrawPath(edge, shape);
        }

        // Text colors as Windows draws them on this fill (TextFillColorPrimary and Secondary).
        var primaryInk = dark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
        var secondaryInk = dark ? Color.FromArgb(0xCE, 0xCE, 0xCE) : Color.FromArgb(0x60, 0x60, 0x60);
        // The icon in text color like Windows' flyouts; "off" dimmer (and slashed when the font has no off symbol), problems amber.
        var ink = Tone(icon) switch
        {
            IconTone.Warning => dark ? Color.FromArgb(0xFC, 0xE1, 0x00) : Color.FromArgb(0x9D, 0x5D, 0x00),   // SystemFillColorCaution
            IconTone.Off => secondaryInk,
            _ => primaryInk
        };
        var iconBox = new RectangleF(pill.X + Pad * scale, pill.Y + 1 * scale, IconBox * scale, pill.Height);   // optically centred on the text
        using (var symbol = new SolidBrush(ink))
        using (var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(battery is { } charge ? BatteryGlyph(charge, icon == PopupIcon.BatteryCharging) : Glyph(icon), glyphFont, symbol, iconBox, centred);
        if (IsSlashed(icon))
        {
            var half = IconSize * scale * 0.42f;
            var (cx, cy) = (iconBox.X + iconBox.Width / 2, iconBox.Y + iconBox.Height / 2);
            using var slash = new Pen(ink, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(slash, cx - half, cy - half, cx + half, cy + half);
        }

        using var primary = new SolidBrush(primaryInk);
        using var secondary = new SolidBrush(secondaryInk);
        var textLeft = iconBox.Right + Gap * scale;
        var end = pill.Right - Pad * scale;
        var rightWidth = right > 0 ? Math.Min(right * scale, (end - textLeft) * 0.55f) : 0;
        var titleBox = new RectangleF(textLeft, pill.Y, end - textLeft - (rightWidth > 0 ? rightWidth + Between * scale : 0), pill.Height);
        using (var line = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(title, titleFont, primary, titleBox, line);

        if (level is { } amount)
        {
            // A level bar like Windows' volume flyout: ControlStrongFillColorDefault track, accent fill.
            var bar = new RectangleF(end - BarWidth * scale, pill.Y + (pill.Height - BarHeight * scale) / 2, BarWidth * scale, BarHeight * scale);
            using (var track = FluentTheme.RoundedRect(bar, bar.Height / 2))
            using (var trackFill = new SolidBrush(dark ? Color.FromArgb(0x8B, 255, 255, 255) : Color.FromArgb(0x72, 0, 0, 0)))
                g.FillPath(trackFill, track);
            var filled = Math.Clamp(amount, 0f, 1f) * bar.Width;
            if (filled > 0)
            {
                using var part = FluentTheme.RoundedRect(new RectangleF(bar.X, bar.Y, Math.Max(filled, bar.Height), bar.Height), bar.Height / 2);
                using var partFill = new SolidBrush(FluentTheme.Accent);
                g.FillPath(partFill, part);
            }
        }
        else if (rightWidth > 0)
        {
            using var far = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(detail, detailFont, secondary, new RectangleF(end - rightWidth, pill.Y, rightWidth, pill.Height), far);
        }
        return image;
    }

    private enum IconTone
    {
        On,
        Off,
        Warning
    }

    /// <summary>On, active or done in full text color; off and ended dimmer; problems in amber.</summary>
    private static IconTone Tone(PopupIcon icon) => icon switch
    {
        PopupIcon.Warning => IconTone.Warning,
        PopupIcon.KeyboardLightOff or PopupIcon.MicrophoneOff or PopupIcon.CameraOff or PopupIcon.TouchpadOff
            or PopupIcon.AwakeEnded or PopupIcon.Info => IconTone.Off,
        _ => IconTone.On
    };

    internal static bool IsSlashed(PopupIcon icon) => icon is PopupIcon.KeyboardLightOff or PopupIcon.CameraOff or PopupIcon.TouchpadOff;

    /// <summary>The battery symbol for a charge, in tenths, like the one in the taskbar: Battery0–10, BatteryCharging0–10.</summary>
    internal static string BatteryGlyph(int percent, bool charging)
    {
        var tenths = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        return (charging, tenths) switch
        {
            (false, 10) => "",
            (false, _) => char.ToString((char)(0xE850 + tenths)),
            (true, 10) => "",
            (true, 9) => "",
            (true, _) => char.ToString((char)(0xE85A + tenths))
        };
    }

    /// <summary>Segoe Fluent Icons code points.</summary>
    internal static string Glyph(PopupIcon icon) => icon switch
    {
        PopupIcon.Warning => "",
        PopupIcon.Done => "",
        PopupIcon.Copied => "",
        PopupIcon.KeyboardLight or PopupIcon.KeyboardLightOff => "",
        PopupIcon.Microphone => "",
        PopupIcon.MicrophoneOff => "",
        PopupIcon.Camera or PopupIcon.CameraOff => "",
        PopupIcon.Touchpad or PopupIcon.TouchpadOff => "",
        PopupIcon.Screenshot => "",
        PopupIcon.Battery => "",
        PopupIcon.BatteryCharging => "",
        PopupIcon.AwakeEnded => "",
        _ => ""
    };
}
