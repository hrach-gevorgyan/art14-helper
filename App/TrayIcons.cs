using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Art14Helper;

/// <summary>
/// The tray icon: the app's mark (a Trebuchet MS Bold "A" whose crossbar is cut free from
/// the right leg) cut out of a one-color tile, white on a dark taskbar and near-black on a
/// light one, like Windows' own tray icons. Same geometry as the app icon
/// (Assets/Art14Helper.ico), on a 64-unit grid.
/// </summary>
internal static class TrayIcons
{
    // Theme changes come in bursts of UserPreferenceChanged; each size and theme is
    // drawn once and the icon (with its GDI handle) reused afterwards.
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<(int Size, bool Dark), Icon> Drawn = [];
    private static Mark? _mark;

    /// <summary>True when the taskbar uses the dark theme.</summary>
    internal static bool TaskbarIsDark => ReadThemeFlag() is 0;

    /// <summary>The taskbar's light-theme flag: the system one, or the apps one on builds without it.</summary>
    private static object? ReadThemeFlag()
    {
        using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return personalize?.GetValue("SystemUsesLightTheme") ?? personalize?.GetValue("AppsUseLightTheme");
    }

    /// <summary>The icon for the current tray size and theme. Shared: callers must not dispose it.</summary>
    internal static Icon Create()
    {
        var size = Math.Max(16, IconApi.SmallIconSize);
        var dark = TaskbarIsDark;
        lock (CacheLock)
        {
            if (!Drawn.TryGetValue((size, dark), out var icon))
            {
                _mark ??= Mark.Measure();
                Drawn[(size, dark)] = icon = ToIcon(Draw(_mark, size, dark ? Color.White : Color.FromArgb(30, 30, 30)));
            }
            return icon;
        }
    }

    /// <summary>The letter outline and the crossbar cut, in 64-unit coordinates.</summary>
    private sealed class Mark(GraphicsPath letter, PointF cutTop, PointF cutBottom)
    {
        internal GraphicsPath Letter { get; } = letter;
        internal PointF CutTop { get; } = cutTop;
        internal PointF CutBottom { get; } = cutBottom;

        /// <summary>
        /// Places the letter in the middle of the tile and finds the cut from its outline:
        /// down the centre line to the crossbar, then along the right leg for its angle.
        /// </summary>
        internal static Mark Measure()
        {
            using var family = TrebuchetOrFallback();
            var letter = new GraphicsPath();
            letter.AddString("A", family, (int)FontStyle.Bold, 44, PointF.Empty, StringFormat.GenericTypographic);
            var box = letter.GetBounds();
            using (var center = new Matrix())
            {
                center.Translate(32 - box.Width / 2 - box.X, 33 - box.Height / 2 - box.Y);
                letter.Transform(center);
            }
            box = letter.GetBounds();

            const float Step = 0.05f;
            var mid = box.X + box.Width / 2;
            var y = box.Y;
            while (!letter.IsVisible(mid, y)) y += Step;   // into the apex
            while (letter.IsVisible(mid, y)) y += Step;    // through it, to the hole
            while (!letter.IsVisible(mid, y)) y += Step;   // down to the crossbar
            var barTop = y;
            while (letter.IsVisible(mid, y)) y += Step;
            var barBottom = y;

            float RightEdge(float atY)
            {
                var x = box.Right + 1;
                while (!letter.IsVisible(x, atY)) x -= Step;
                return x;
            }
            float upper = box.Y + box.Height * 0.3f, lower = box.Bottom - 1.5f;
            var slope = (RightEdge(lower) - RightEdge(upper)) / (lower - upper);   // x change per unit down

            var probe = barTop - 0.6f;
            var inner = mid;
            while (!letter.IsVisible(inner, probe)) inner += Step;   // inside edge of the right leg
            float top = barTop - 0.6f, bottom = barBottom + 0.6f;
            return new Mark(letter,
                new PointF(inner + slope * (top - probe), top),
                new PointF(inner + slope * (bottom - probe), bottom));
        }

        private static FontFamily TrebuchetOrFallback()
        {
            try
            {
                return new FontFamily("Trebuchet MS");
            }
            catch (ArgumentException)
            {
                return new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif);   // a Windows install without the font
            }
        }
    }

    /// <summary>
    /// Renders the mark at 4x with smoothing, then shrinks it to the target size, so the
    /// edges stay smooth at 16-32 px.
    /// </summary>
    private static Bitmap Draw(Mark mark, int size, Color color)
    {
        const int Oversample = 4;
        using var large = new Bitmap(size * Oversample, size * Oversample, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(large.Width / 64f, large.Height / 64f);

            using var paint = new SolidBrush(color);
            using (var tile = Squircle(4, 4, 56, 16))
                g.FillPath(paint, tile);

            // Cut the letter out (transparent paint replaces pixels), then fill the
            // crossbar cut back in with the tile color. About a pixel wide at tray sizes.
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var hole = new SolidBrush(Color.Transparent))
                g.FillPath(hole, mark.Letter);
            g.CompositingMode = CompositingMode.SourceOver;
            var width = Math.Max(2.4f, 64f / size);
            using var cut = new Pen(color, width);
            g.DrawLine(cut, Shift(mark.CutTop, -width / 2), Shift(mark.CutBottom, -width / 2));
        }

        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(large, new Rectangle(0, 0, size, size));
        }
        return result;
    }

    /// <summary>The cut sits on the crossbar's side of the leg edge, so it shortens the bar, not the leg.</summary>
    private static PointF Shift(PointF point, float dx) => new(point.X + dx, point.Y);

    /// <summary>A square with rounded corners.</summary>
    private static GraphicsPath Squircle(float left, float top, float side, float radius)
    {
        var arc = radius * 2;
        var far = left + side - arc;
        var bottom = top + side - arc;
        var path = new GraphicsPath();
        path.AddArc(left, top, arc, arc, 180, 90);
        path.AddArc(far, top, arc, arc, 270, 90);
        path.AddArc(far, bottom, arc, arc, 0, 90);
        path.AddArc(left, bottom, arc, arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// An Icon with its own copy of the image: Icon.FromHandle doesn't take ownership of
    /// the HICON, so the clone is kept and the temporary handle freed straight away.
    /// </summary>
    private static Icon ToIcon(Bitmap bitmap)
    {
        var temporary = bitmap.GetHicon();
        bitmap.Dispose();
        using var view = Icon.FromHandle(temporary);
        var owned = (Icon)view.Clone();
        IconApi.Destroy(temporary);
        return owned;
    }
}
