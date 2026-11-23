using System.Drawing.Drawing2D;

namespace Art14Helper.Ui;

/// <summary>Where text sits in its box (DT_* flags).</summary>
[Flags]
internal enum TextLayout : uint
{
    Left = 0,
    Center = 0x1,
    Right = 0x2,
    Middle = 0x4 | SingleLine,
    Bottom = 0x8 | SingleLine,
    Wrap = 0x10,
    SingleLine = 0x20,
    Ellipsis = 0x8000,
    NoPrefix = 0x800
}

/// <summary>A GDI font in pixels, made once and reused for every paint.</summary>
internal sealed class UiFont : IDisposable
{
    private static readonly Lazy<IntPtr> MeasuringDc = new(() => Gdi.CreateCompatibleDC(IntPtr.Zero));
    private static readonly Dictionary<(string, int, bool), UiFont> Made = [];

    internal IntPtr Handle { get; private set; }

    private UiFont(string family, int pixels, bool semibold) => Handle = Gdi.CreateFont(family, pixels, semibold);

    /// <summary>
    /// The font for a family and a size at 100% (<paramref name="pixels"/>), enlarged by
    /// <paramref name="scale"/>. Shared and kept for the app's lifetime: there are only a
    /// handful of sizes per monitor scale.
    /// </summary>
    internal static UiFont Get(string family, float pixels, float scale)
    {
        var size = (int)Math.Round(pixels * scale);
        if (!Made.TryGetValue((family, size, false), out var font))
            Made[(family, size, false)] = font = new UiFont(family, size, false);
        return font;
    }

    internal static UiFont Text(float pixels, float scale) => Get(FluentTheme.TextFamily, pixels, scale);

    internal static UiFont Strong(float pixels, float scale) => Get(FluentTheme.StrongFamily, pixels, scale);

    internal static UiFont Icons(float pixels, float scale) => Get(FluentTheme.IconFamily, pixels, scale);

    /// <summary>The size <paramref name="text"/> needs; with a width, wrapped to it.</summary>
    internal Size Measure(string text, int wrapWidth = 0)
    {
        var dc = MeasuringDc.Value;
        var previous = Gdi.SelectObject(dc, Handle);
        var box = new Rect { Right = wrapWidth > 0 ? wrapWidth : 100_000 };
        const uint CalculateOnly = 0x400;   // DT_CALCRECT
        var layout = (uint)TextLayout.NoPrefix | CalculateOnly | (wrapWidth > 0 ? (uint)TextLayout.Wrap : (uint)TextLayout.SingleLine);
        Gdi.DrawText(dc, text, ref box, layout, Color.Black);
        Gdi.SelectObject(dc, previous);
        return new Size(box.Right - box.Left, box.Bottom - box.Top);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
            Gdi.DeleteObject(Handle);
        Handle = IntPtr.Zero;
    }
}

/// <summary>
/// An off-screen picture of (part of) a window: shapes are drawn anti-aliased with GDI+,
/// text with GDI (ClearType, like the rest of Windows), then the whole picture is copied
/// to the screen at once, so nothing flickers. <see cref="Origin"/> shifts everything
/// drawn (scrolling).
/// </summary>
internal sealed class Canvas : IDisposable
{
    private (IntPtr Dc, IntPtr Bitmap, IntPtr Previous) _buffer;
    private Graphics? _graphics;
    // A page uses a handful of colors; brushes and pens are made once per canvas, not per shape.
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Dictionary<(int, float), Pen> _pens = [];

    private SolidBrush BrushFor(Color color)
    {
        if (!_brushes.TryGetValue(color.ToArgb(), out var brush))
            _brushes[color.ToArgb()] = brush = new SolidBrush(color);
        return brush;
    }

    private Pen PenFor(Color color, float width)
    {
        if (!_pens.TryGetValue((color.ToArgb(), width), out var pen))
            _pens[(color.ToArgb(), width)] = pen = new Pen(color, width);
        return pen;
    }

    internal Size Size { get; private set; }

    internal Point Origin { get; set; }

    private Graphics G => _graphics ?? throw new InvalidOperationException("The canvas has no size yet.");

    /// <summary>Makes sure the picture is <paramref name="size"/>; contents are kept only when it already was.</summary>
    internal void EnsureSize(Size size)
    {
        if (size == Size && _graphics is not null)
            return;
        Release();
        Size = size;
        _buffer = Gdi.CreateBuffer(Math.Max(1, size.Width), Math.Max(1, size.Height));
        _graphics = Graphics.FromHdc(_buffer.Dc);
        _graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    internal void Clear(Color color) => G.Clear(color);

    private RectangleF Shift(RectangleF box) => new(box.X + Origin.X, box.Y + Origin.Y, box.Width, box.Height);

    private Rectangle Shift(Rectangle box) => new(box.X + Origin.X, box.Y + Origin.Y, box.Width, box.Height);

    internal void Fill(RectangleF box, Color color, float radius = 0)
    {
        var brush = BrushFor(color);
        if (radius <= 0)
        {
            G.FillRectangle(brush, Shift(box));
            return;
        }
        using var shape = FluentTheme.RoundedRect(Shift(box), radius);
        G.FillPath(brush, shape);
    }

    /// <summary>Fills only the part of <paramref name="box"/> inside a rounded <paramref name="within"/>.</summary>
    internal void FillInside(RectangleF box, RectangleF within, float radius, Color color)
    {
        using var shape = FluentTheme.RoundedRect(Shift(within), radius);
        G.SetClip(shape);
        Fill(box, color);
        G.ResetClip();
    }

    internal void Outline(RectangleF box, Color color, float radius = 0, float width = 1)
    {
        var pen = PenFor(color, width);
        using var shape = FluentTheme.RoundedRect(Shift(box), radius);
        G.DrawPath(pen, shape);
    }

    internal void Line(float x1, float y1, float x2, float y2, Color color, float width = 1)
    {
        var pen = PenFor(color, width);
        G.DrawLine(pen, x1 + Origin.X, y1 + Origin.Y, x2 + Origin.X, y2 + Origin.Y);
    }

    internal void Circle(RectangleF box, Color color) => Fill(box, color, Math.Min(box.Width, box.Height) / 2);

    internal void Ring(RectangleF box, Color color, float width)
    {
        var pen = PenFor(color, width);
        G.DrawEllipse(pen, Shift(box));
    }

    /// <summary>An arc with round ends, clockwise from the top, <paramref name="degrees"/> long.</summary>
    internal void Arc(RectangleF box, Color color, float width, float degrees)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        G.DrawArc(pen, Shift(box), -90, degrees);
    }

    internal void Text(string text, UiFont font, Rectangle box, Color color, TextLayout layout = TextLayout.Left | TextLayout.Middle)
    {
        if (text.Length == 0)
            return;
        G.Flush(FlushIntention.Sync);   // shapes first, then GDI text over them
        var area = Rect.From(Shift(box));
        var previous = Gdi.SelectObject(_buffer.Dc, font.Handle);
        Gdi.DrawText(_buffer.Dc, text, ref area, (uint)(layout | TextLayout.NoPrefix), color);
        Gdi.SelectObject(_buffer.Dc, previous);
    }

    internal void Icon(IntPtr icon, Rectangle box)
    {
        if (icon == IntPtr.Zero)
            return;
        G.Flush(FlushIntention.Sync);
        Gdi.DrawIcon(_buffer.Dc, icon, Shift(box));
    }

    /// <summary>Copies the picture to a window's paint DC at <paramref name="at"/>.</summary>
    internal void CopyTo(IntPtr dc, Point at)
    {
        G.Flush(FlushIntention.Sync);
        Gdi.Copy(_buffer.Dc, dc, new Rectangle(at, Size));
    }

    private void Release()
    {
        _graphics?.Dispose();
        _graphics = null;
        if (_buffer.Dc != IntPtr.Zero)
            Gdi.FreeBuffer(_buffer);
        _buffer = default;
    }

    public void Dispose()
    {
        Release();
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        foreach (var pen in _pens.Values)
            pen.Dispose();
        _brushes.Clear();
        _pens.Clear();
    }
}
