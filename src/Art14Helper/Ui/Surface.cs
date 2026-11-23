namespace Art14Helper.Ui;

/// <summary>
/// Something drawn on a <see cref="Surface"/>: a card, a tile, a button. Parts are not
/// windows; the surface paints them and passes them the mouse. Points are in the
/// surface's content coordinates, the same as <see cref="Bounds"/>.
/// </summary>
internal abstract class Part
{
    internal Rectangle Bounds { get; set; }

    /// <summary>Set by the surface: asks for a repaint.</summary>
    internal Action? Changed { get; set; }

    protected void Redraw() => Changed?.Invoke();

    /// <summary>Height this part wants at <paramref name="width"/>.</summary>
    internal abstract int HeightFor(int width);

    internal abstract void Paint(Canvas canvas);

    /// <summary>The pointer moved to <paramref name="at"/>, or left the part (null).</summary>
    internal virtual void Hover(Point? at) { }

    internal virtual void Press(Point at) { }

    /// <summary>The button went up; <paramref name="click"/> when it's still where it went down.</summary>
    internal virtual void Release(Point at, bool click) { }

    internal virtual bool IsClickable(Point at) => false;

    internal virtual string? Tip(Point at) => null;

    /// <summary>How many things in this part can take the keyboard focus (rows, tiles, a button).</summary>
    internal virtual int FocusStops => 0;

    /// <summary>Where focus stop <paramref name="stop"/> is drawn, in content coordinates.</summary>
    internal virtual Rectangle FocusBounds(int stop) => Bounds;

    /// <summary>Enter or Space on focus stop <paramref name="stop"/>.</summary>
    internal virtual void Activate(int stop) { }
}

/// <summary>
/// A rectangle of a window that draws a list of parts on its own canvas, optionally
/// scrolled vertically (wheel, keys, or dragging the thin bar on the right).
/// </summary>
internal sealed class Surface(Window owner) : IDisposable
{
    private readonly Canvas _canvas = new();
    private readonly List<Part> _parts = [];
    private Part? _hover;
    private Part? _pressed;
    private int _dragOffset = -1;
    // The keyboard focus as a position among the focus stops, so it survives a page being
    // rebuilt (a click reloads the settings); -1: none.
    private int _focus = -1;

    /// <summary>Draws the focus ring; on after keyboard use, off after a click (as in Windows).</summary>
    internal bool ShowFocus { get; set; }

    /// <summary>Where the surface is, in the window's client coordinates.</summary>
    internal Rectangle Area { get; set; }

    internal bool Scrolls { get; init; }

    internal int ScrollY { get; private set; }

    internal int ContentHeight { get; set; }

    internal float Scale { get; set; } = 1f;

    /// <summary>Tooltip text under the pointer changed (null: none).</summary>
    internal Action<string?>? TipChanged { get; set; }

    internal IReadOnlyList<Part> Parts => _parts;

    internal void Add(Part part)
    {
        part.Changed = owner.Invalidate;
        _parts.Add(part);
    }

    internal void Clear()
    {
        foreach (var part in _parts)
            part.Changed = null;
        _parts.Clear();
        _hover = _pressed = null;
        TipChanged?.Invoke(null);
    }

    private int MaxScroll => Math.Max(0, ContentHeight - Area.Height);

    internal void ScrollTo(int y)
    {
        var clamped = Math.Clamp(y, 0, MaxScroll);
        if (clamped == ScrollY)
            return;
        ScrollY = clamped;
        owner.Invalidate();
    }

    internal void ScrollBy(int delta) => ScrollTo(ScrollY + delta);

    internal bool Contains(Point client) => Area.Contains(client);

    private Point ToContent(Point client) => new(client.X - Area.X, client.Y - Area.Y + ScrollY);

    private Part? PartAt(Point content) => _parts.LastOrDefault(part => part.Bounds.Contains(content));

    internal void Paint(IntPtr dc)
    {
        if (Area.Width <= 0 || Area.Height <= 0)
            return;
        _canvas.EnsureSize(Area.Size);
        _canvas.Origin = Point.Empty;
        _canvas.Clear(FluentTheme.Background);   // read each time: the theme can change while open
        _canvas.Origin = new Point(0, -ScrollY);
        var visible = new Rectangle(0, ScrollY, Area.Width, Area.Height);
        foreach (var part in _parts.Where(part => part.Bounds.IntersectsWith(visible)))
            part.Paint(_canvas);
        if (ShowFocus && FocusedStop() is { } focused)
        {
            var ring = Rectangle.Inflate(focused.Item1.FocusBounds(focused.Item2), (int)(2 * Scale), (int)(2 * Scale));
            _canvas.Outline(ring, FluentTheme.Text, 6 * Scale, Math.Max(2f, 2 * Scale));
        }
        _canvas.Origin = Point.Empty;
        if (ScrollBar() is { } bar)
            _canvas.Fill(bar, _dragOffset >= 0 ? FluentTheme.TextSecondary : FluentTheme.TextDisabled, bar.Width / 2f);
        _canvas.CopyTo(dc, Area.Location);
    }

    /// <summary>The scroll thumb, in surface coordinates; null when everything fits.</summary>
    private Rectangle? ScrollBar()
    {
        if (!Scrolls || MaxScroll == 0)
            return null;
        var width = (int)Math.Round(4 * Scale);
        var margin = (int)Math.Round(3 * Scale);
        var track = Area.Height - 2 * margin;
        var length = Math.Max((int)(24 * Scale), track * Area.Height / ContentHeight);
        var top = margin + (track - length) * ScrollY / MaxScroll;
        return new Rectangle(Area.Width - width - margin, top, width, length);
    }

    internal void MouseMove(Point client)
    {
        if (_dragOffset >= 0 && ScrollBar() is { } bar)
        {
            var margin = (int)Math.Round(3 * Scale);
            var room = Area.Height - 2 * margin - bar.Height;
            var top = client.Y - Area.Y - _dragOffset - margin;
            ScrollTo(room <= 0 ? 0 : top * MaxScroll / room);
            return;
        }
        var at = ToContent(client);
        var part = _pressed ?? PartAt(at);
        if (part != _hover)
        {
            _hover?.Hover(null);
            _hover = part;
            TipChanged?.Invoke(null);
        }
        part?.Hover(at);
        TipChanged?.Invoke(part?.Tip(at));
    }

    internal void MouseLeave()
    {
        if (_dragOffset >= 0)
            return;
        _hover?.Hover(null);
        _hover = null;
        TipChanged?.Invoke(null);
    }

    internal void Press(Point client)
    {
        if (ScrollBar() is { } bar)
        {
            var local = new Point(client.X - Area.X, client.Y - Area.Y);
            var grab = Rectangle.Inflate(bar, (int)(4 * Scale), 0);
            if (grab.Contains(local))
            {
                _dragOffset = local.Y - bar.Y;
                owner.Invalidate();
                return;
            }
        }
        var at = ToContent(client);
        _pressed = PartAt(at);
        _pressed?.Press(at);
        TipChanged?.Invoke(null);
    }

    internal void Release(Point client)
    {
        if (_dragOffset >= 0)
        {
            _dragOffset = -1;
            owner.Invalidate();
            return;
        }
        var pressed = _pressed;
        _pressed = null;
        if (pressed is null)
            return;
        var at = ToContent(client);
        pressed.Release(at, pressed.Bounds.Contains(at));
        MouseMove(client);
    }

    /// <summary>Mouse button released elsewhere or capture lost: nothing stays pressed.</summary>
    internal void Cancel()
    {
        _dragOffset = -1;
        if (_pressed is { } pressed)
        {
            _pressed = null;
            pressed.Release(Point.Empty, click: false);
        }
    }

    private List<(Part Part, int Stop)> Stops()
        => _parts.SelectMany(part => Enumerable.Range(0, part.FocusStops).Select(stop => (part, stop))).ToList();

    private (Part, int)? FocusedStop()
    {
        if (_focus < 0)
            return null;
        var stops = Stops();
        return _focus < stops.Count ? stops[_focus] : null;
    }

    internal bool HasFocus => FocusedStop() is not null;

    /// <summary>
    /// Tab: the next (or previous) focus stop, scrolled into view. False when this surface has
    /// none left in that direction, so the window moves on to the next surface.
    /// </summary>
    internal bool MoveFocus(bool backward)
    {
        var stops = Stops();
        var next = _focus < 0 || _focus >= stops.Count ? (backward ? stops.Count - 1 : 0) : _focus + (backward ? -1 : 1);
        if (next < 0 || next >= stops.Count)
        {
            _focus = -1;
            owner.Invalidate();
            return false;
        }
        _focus = next;
        var box = stops[next].Part.FocusBounds(stops[next].Stop);
        if (box.Top < ScrollY)
            ScrollTo(box.Top - (int)(12 * Scale));
        else if (box.Bottom > ScrollY + Area.Height)
            ScrollTo(box.Bottom - Area.Height + (int)(12 * Scale));
        owner.Invalidate();
        return true;
    }

    internal void ClearFocus()
    {
        _focus = -1;
        owner.Invalidate();
    }

    /// <summary>Enter or Space: activates the focused stop; false when nothing has the focus.</summary>
    internal bool ActivateFocus()
    {
        if (FocusedStop() is not { } focused)
            return false;
        focused.Item1.Activate(focused.Item2);
        return true;
    }

    internal void Wheel(int delta) => ScrollBy(-delta * (int)(96 * Scale) / 120);

    internal bool HandAt(Point client)
    {
        if (!Area.Contains(client))
            return false;
        var at = ToContent(client);
        return PartAt(at)?.IsClickable(at) == true;
    }

    public void Dispose()
    {
        Clear();
        _canvas.Dispose();
    }
}
