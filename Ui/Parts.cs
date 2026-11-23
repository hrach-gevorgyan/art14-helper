namespace Art14Helper.Ui;

/// <summary>Plain text: one line, or wrapped to the width. A fixed height keeps the layout steady while the text changes.</summary>
internal sealed class TextPart(string text, UiFont font, Color color, bool wrap = false, int fixedHeight = 0) : Part
{
    internal string Text { get; set; } = text;

    internal Color Color { get; set; } = color;

    internal override int HeightFor(int width)
        => fixedHeight > 0 ? fixedHeight : Text.Length == 0 ? 0 : font.Measure(Text, wrap ? width : 0).Height;

    internal override void Paint(Canvas canvas)
        => canvas.Text(Text, font, Bounds, Color, wrap ? TextLayout.Wrap : TextLayout.Middle | TextLayout.Ellipsis);
}

/// <summary>An icon followed by a title, both vertically centred: the app header in a window.</summary>
internal sealed class HeadingPart(string title, UiFont font, int iconSize, int gap, int height) : Part
{
    private IntPtr _icon = IconApi.AppIcon(iconSize);

    internal string Title { get; set; } = title;

    internal override int HeightFor(int width) => height;

    internal override void Paint(Canvas canvas)
    {
        var top = Bounds.Y + (Bounds.Height - iconSize) / 2;
        canvas.Icon(_icon, new Rectangle(Bounds.X, top, iconSize, iconSize));
        var left = Bounds.X + iconSize + gap;
        canvas.Text(Title, font, new Rectangle(left, Bounds.Y, Bounds.Right - left, Bounds.Height), FluentTheme.Text);
    }

    /// <summary>Frees the icon; call when the part is thrown away.</summary>
    internal void Free()
    {
        if (_icon != IntPtr.Zero)
            IconApi.Destroy(_icon);
        _icon = IntPtr.Zero;
    }
}

/// <summary>A Windows 11 button: accent-filled (primary) or gray.</summary>
internal sealed class ButtonPart(string text, bool primary, float scale, Action onClick) : Part
{
    private bool _hover;
    private bool _pressed;

    internal string Text { get; set; } = text;

    internal bool Enabled { get; set; } = true;

    internal bool Visible { get; set; } = true;

    internal override int HeightFor(int width) => (int)Math.Round(36 * scale);

    internal override bool IsClickable(Point at) => Visible && Enabled;

    internal override int FocusStops => Visible && Enabled ? 1 : 0;

    internal override void Activate(int stop) => onClick();

    internal override void Hover(Point? at)
    {
        if (_hover == at.HasValue)
            return;
        _hover = at.HasValue;
        Redraw();
    }

    internal override void Press(Point at)
    {
        _pressed = true;
        Redraw();
    }

    internal override void Release(Point at, bool click)
    {
        _pressed = false;
        Redraw();
        if (click && Visible && Enabled)
            onClick();
    }

    internal override void Paint(Canvas canvas)
    {
        if (!Visible)
            return;
        Color fill, ink;
        if (!Enabled)
            (fill, ink) = (FluentTheme.ButtonDisabled, FluentTheme.TextDisabled);
        else if (primary)
            (fill, ink) = (FluentTheme.Shade(FluentTheme.Accent, _pressed ? -0.1f : _hover ? 0.1f : 0f), FluentTheme.OnAccent);
        else
            (fill, ink) = (_pressed ? FluentTheme.ButtonPressed : _hover ? FluentTheme.ButtonHover : FluentTheme.Button, FluentTheme.Text);
        var box = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width - 1, Bounds.Height - 1);
        canvas.Fill(box, fill, 4 * scale);
        canvas.Text(Text, UiFont.Text(14, scale), box, ink, TextLayout.Center | TextLayout.Middle);
    }
}

/// <summary>A thin bar with a sliding segment: work in progress of unknown length.</summary>
internal sealed class ProgressPart : Part, IDisposable
{
    private readonly float _scale;
    private readonly UiTimer _frames;
    private float _position;

    internal ProgressPart(float scale)
    {
        _scale = scale;
        _frames = new UiTimer(16, () =>
        {
            _position = (_position + 0.012f) % 1.4f;
            Redraw();
        });
    }

    internal bool Running
    {
        get => _frames.Running;
        set
        {
            if (value == _frames.Running)
                return;
            if (value)
                _frames.Start();
            else
                _frames.Stop();
            Redraw();
        }
    }

    internal override int HeightFor(int width) => (int)Math.Round(4 * _scale);

    internal override void Paint(Canvas canvas)
    {
        if (!Running)
            return;
        var middle = Bounds.Y + Bounds.Height / 2f;
        canvas.Fill(new RectangleF(Bounds.X, middle - 1, Bounds.Width, 2), FluentTheme.Divider);
        var segment = Bounds.Width * 0.3f;
        var left = Math.Max(Bounds.X, Bounds.X + (_position - 0.3f) * Bounds.Width);
        var right = Math.Min(Bounds.Right, Bounds.X + (_position - 0.3f) * Bounds.Width + segment);
        if (right > left)
            canvas.Fill(new RectangleF(left, Bounds.Y, right - left, Bounds.Height), FluentTheme.Accent, Bounds.Height / 2f);
    }

    public void Dispose() => _frames.Dispose();
}

/// <summary>An entry of the navigation pane: icon and name, accent bar when selected.</summary>
internal sealed class NavPart(string text, string glyph, float scale, Action onClick) : Part
{
    private bool _hover;

    internal string Text => text;

    internal bool Selected { get; set; }

    private int S(float value) => (int)Math.Round(value * scale);

    internal override int HeightFor(int width) => S(38);

    internal override bool IsClickable(Point at) => true;

    internal override int FocusStops => 1;

    internal override Rectangle FocusBounds(int stop) => new(Bounds.X, Bounds.Y + S(2), Bounds.Width - 1, Bounds.Height - S(4));

    internal override void Activate(int stop) => onClick();

    internal override void Hover(Point? at)
    {
        if (_hover == at.HasValue)
            return;
        _hover = at.HasValue;
        Redraw();
    }

    internal override void Release(Point at, bool click)
    {
        if (click)
            onClick();
    }

    internal override void Paint(Canvas canvas)
    {
        var box = new Rectangle(Bounds.X, Bounds.Y + S(2), Bounds.Width - 1, Bounds.Height - S(4));
        if (Selected || _hover)
            canvas.Fill(box, Selected ? FluentTheme.NavSelected : FluentTheme.NavHover, S(4));
        if (Selected)
            canvas.Fill(new RectangleF(box.X, box.Y + (box.Height - S(16)) / 2f, S(3), S(16)), FluentTheme.Accent, 1.5f * scale);
        canvas.Text(glyph, UiFont.Icons(16, scale), new Rectangle(box.X + S(14), box.Y, S(20), box.Height), FluentTheme.Text,
            TextLayout.Center | TextLayout.Middle);
        canvas.Text(text, UiFont.Text(14, scale), new Rectangle(box.X + S(48), box.Y, box.Width - S(52), box.Height), FluentTheme.Text,
            TextLayout.Middle | TextLayout.Ellipsis);
    }
}
