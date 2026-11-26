namespace Art14Helper;

internal enum RowKind
{
    /// <summary>One option of a group: radio circle on the left.</summary>
    Choice,
    /// <summary>On or off: switch on the right.</summary>
    Toggle,
    /// <summary>Runs or opens something: chevron on the right.</summary>
    Action,
    /// <summary>Just information, maybe with a value on the right.</summary>
    Info
}

internal enum BadgeTone
{
    Neutral,
    Success,
    Attention
}

/// <summary>One line of a settings card.</summary>
internal sealed class RowSpec
{
    internal string Text { get; init; } = "";
    internal string? Secondary { get; init; }
    internal string? Value { get; init; }
    /// <summary>Short status pill on the right ("Up to date", "Update").</summary>
    internal string? Badge { get; init; }
    internal BadgeTone BadgeTone { get; init; }
    internal RowKind Kind { get; init; }
    internal bool Checked { get; init; }
    internal bool Enabled { get; init; } = true;
    internal Action? OnClick { get; init; }
    /// <summary>Icon-font symbol at the start of the row.</summary>
    internal string? Glyph { get; init; }
    internal bool Clickable => Enabled && OnClick is not null;
}

/// <summary>
/// A settings card: an optional title over a rounded box of rows, each with its own
/// hover, press and click. Long descriptions also show as a tooltip.
/// </summary>
internal sealed class CardPart(string? title, IReadOnlyList<RowSpec> rows, float scale) : Part
{
    private IReadOnlyList<RowSpec> _rows = rows;
    /// <summary>A description wraps up to this many lines; anything longer also shows as a tooltip.</summary>
    private const int MaxDescriptionLines = 3;
    private int _width;
    // Lines each description needs at _width: measuring text is GDI work, and heights are asked for on every hover and paint.
    private readonly Dictionary<RowSpec, int> _lines = [];
    private int _hover = -1;
    private int _pressed = -1;

    internal string? Title => title;

    internal IReadOnlyList<RowSpec> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            _lines.Clear();
            Redraw();
        }
    }

    /// <summary>No clicks while true (setup runs).</summary>
    internal bool Locked { get; set; }

    private int S(float value) => (int)Math.Round(value * scale);

    private int TitleHeight => title is null ? 0 : S(30);

    private int RowHeight(RowSpec row) => row.Secondary is null ? S(52) : S(52) + DescriptionLines(row) * S(16);

    /// <summary>How many lines the description needs at the current width, at most <see cref="MaxDescriptionLines"/>.</summary>
    private int DescriptionLines(RowSpec row) => Math.Min(MaxDescriptionLines, NeededLines(row));

    private int NeededLines(RowSpec row)
    {
        if (row.Secondary is null || _width <= 0)
            return 1;
        if (_lines.TryGetValue(row, out var known))
            return known;
        var (left, right) = Columns(null, row, new Rectangle(0, 0, _width - 2, S(52)));
        var small = UiFont.Text(12, scale);
        var line = Math.Max(1, small.Measure("Ag").Height);
        return _lines[row] = Math.Max(1, (small.Measure(row.Secondary, Math.Max(1, right - left)).Height + line / 2) / line);
    }

    internal override int HeightFor(int width)
    {
        if (width != _width)
            _lines.Clear();
        _width = width;
        return TitleHeight + _rows.Sum(RowHeight) + S(2);
    }

    private int RowAt(Point at)
    {
        var y = Bounds.Y + TitleHeight + S(1);
        for (var i = 0; i < _rows.Count; i++)
        {
            var height = RowHeight(_rows[i]);
            if (at.Y >= y && at.Y < y + height)
                return i;
            y += height;
        }
        return -1;
    }

    private bool CanClick(int index) => !Locked && index >= 0 && _rows[index].Clickable;

    internal override bool IsClickable(Point at) => CanClick(RowAt(at));

    private List<int> FocusRows => Enumerable.Range(0, _rows.Count).Where(CanClick).ToList();

    internal override int FocusStops => FocusRows.Count;

    internal override Rectangle FocusBounds(int stop)
    {
        var index = FocusRows[stop];
        var y = Bounds.Y + TitleHeight + S(1);
        for (var i = 0; i < index; i++)
            y += RowHeight(_rows[i]);
        return new Rectangle(Bounds.X + 1, y, Bounds.Width - 3, RowHeight(_rows[index]) - 1);
    }

    internal override void Activate(int stop)
    {
        if (FocusRows is var rows && stop < rows.Count)
            _rows[rows[stop]].OnClick!();
    }

    internal override string? Tip(Point at)
        => RowAt(at) is var index and >= 0 && _rows[index].Secondary is { } text && NeededLines(_rows[index]) > MaxDescriptionLines ? text : null;

    internal override void Hover(Point? at)
    {
        var index = at is { } point ? RowAt(point) : -1;
        if (index == _hover)
            return;
        _hover = index;
        Redraw();
    }

    internal override void Press(Point at)
    {
        _pressed = RowAt(at);
        Redraw();
    }

    internal override void Release(Point at, bool click)
    {
        var pressed = _pressed;
        _pressed = -1;
        Redraw();
        if (click && RowAt(at) == pressed && CanClick(pressed))
            _rows[pressed].OnClick!();
    }

    internal override void Paint(Canvas canvas)
    {
        if (title is not null)
            canvas.Text(title, UiFont.Strong(14, scale), new Rectangle(Bounds.X + S(2), Bounds.Y, Bounds.Width, S(24)), FluentTheme.Text);

        var card = new Rectangle(Bounds.X, Bounds.Y + TitleHeight, Bounds.Width - 1, Bounds.Height - TitleHeight - 1);
        var radius = S(6);
        canvas.Fill(card, FluentTheme.Card, radius);
        var y = card.Y + S(1);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var box = new Rectangle(card.X + 1, y, card.Width - 1, RowHeight(row));
            var fill = !CanClick(i) ? FluentTheme.Card
                : i == _pressed ? FluentTheme.CardPressed
                : i == _hover ? FluentTheme.CardHover
                : FluentTheme.Card;
            if (fill != FluentTheme.Card)
                canvas.FillInside(box, card, radius, fill);
            PaintRow(canvas, row, box);
            if (i < _rows.Count - 1)
                canvas.Line(box.Left, box.Bottom - 1, box.Right, box.Bottom - 1, FluentTheme.Stroke);
            y += box.Height;
        }
        canvas.Outline(card, FluentTheme.Stroke, radius);
    }

    private void PaintRow(Canvas canvas, RowSpec row, Rectangle box)
    {
        var body = UiFont.Text(14, scale);
        var ink = row.Enabled ? FluentTheme.Text : FluentTheme.TextDisabled;
        var (left, right) = Columns(canvas, row, box);

        if (row.Secondary is null)
        {
            canvas.Text(row.Text, body, new Rectangle(left, box.Top, right - left, box.Height), ink, TextLayout.Middle | TextLayout.Ellipsis);
            return;
        }
        // Title and description as one block, centred in the row; the description wraps.
        var lines = DescriptionLines(row);
        var top = box.Top + (box.Height - S(22) - lines * S(16)) / 2;
        canvas.Text(row.Text, body, new Rectangle(left, top, right - left, S(20)), ink, TextLayout.Bottom | TextLayout.Ellipsis);
        canvas.Text(row.Secondary, UiFont.Text(12, scale), new Rectangle(left, top + S(22), right - left, lines * S(16) + S(2)),
            row.Enabled ? FluentTheme.TextTertiary : FluentTheme.TextDisabled,
            lines == 1 ? TextLayout.SingleLine | TextLayout.Ellipsis : TextLayout.Wrap);
    }

    /// <summary>
    /// The glyph, radio, switch, chevron, badge and value around a row's text, drawn when
    /// <paramref name="canvas"/> is given; returns the space left for the text. Also used,
    /// without drawing, to measure how many lines a description needs.
    /// </summary>
    private (int Left, int Right) Columns(Canvas? canvas, RowSpec row, Rectangle box)
    {
        var body = UiFont.Text(14, scale);
        var small = UiFont.Text(12, scale);
        var ink = row.Enabled ? FluentTheme.Text : FluentTheme.TextDisabled;
        var left = box.Left + S(16);
        var right = box.Right - S(16);
        var middle = box.Top + (box.Height - S(20)) / 2;

        if (row.Glyph is not null)
        {
            canvas?.Text(row.Glyph, UiFont.Icons(18, scale), new Rectangle(left, box.Top, S(20), box.Height), ink, TextLayout.Center | TextLayout.Middle);
            left += S(36);
        }

        if (row.Kind == RowKind.Choice)
        {
            if (canvas is not null)
                PaintRadio(canvas, new Rectangle(left, middle, S(20), S(20)), row.Checked, row.Enabled);
            left += S(32);
        }

        if (row.Kind == RowKind.Toggle)
        {
            var toggle = new Rectangle(right - S(40), middle, S(40), S(20));
            // Wide enough for the longer of the two words in the current language (Russian "Выкл.").
            var stateWidth = Math.Max(S(32), Math.Max(body.Measure(T("On")).Width, body.Measure(T("Off")).Width));
            if (canvas is not null)
            {
                PaintSwitch(canvas, toggle, row.Checked, row.Enabled);
                canvas.Text(row.Checked ? T("On") : T("Off"), body, new Rectangle(toggle.Left - stateWidth - S(8), box.Top, stateWidth, box.Height),
                    ink, TextLayout.Right | TextLayout.Middle);
            }
            right = toggle.Left - stateWidth - S(16);
        }
        else if (row.Kind == RowKind.Action)
        {
            canvas?.Text("", UiFont.Icons(12, scale), new Rectangle(right - S(16), box.Top, S(16), box.Height),
                FluentTheme.TextSecondary, TextLayout.Center | TextLayout.Middle);   // chevron
            right -= S(28);
        }

        if (row.Badge is not null)
        {
            var size = small.Measure(row.Badge);
            var pill = new Rectangle(right - size.Width - S(16), box.Top + (box.Height - S(24)) / 2, size.Width + S(16), S(24));
            if (canvas is not null)
            {
                var (fill, text) = row.BadgeTone switch
                {
                    BadgeTone.Success => (FluentTheme.SuccessFill, FluentTheme.Success),
                    BadgeTone.Attention => (FluentTheme.AccentFill, FluentTheme.Accent),
                    _ => (FluentTheme.Divider, FluentTheme.TextSecondary)
                };
                canvas.Fill(pill, fill, S(12));
                canvas.Text(row.Badge, small, pill, text, TextLayout.Center | TextLayout.Middle);
            }
            right = pill.Left - S(12);
        }

        if (row.Value is not null)
        {
            var width = Math.Min(body.Measure(row.Value).Width, (right - left) / 2);
            canvas?.Text(row.Value, body, new Rectangle(right - width, box.Top, width, box.Height), FluentTheme.TextSecondary,
                TextLayout.Right | TextLayout.Middle | TextLayout.Ellipsis);
            right -= width + S(16);
        }
        return (left, right);
    }

    private void PaintRadio(Canvas canvas, Rectangle box, bool on, bool enabled)
    {
        if (on)
        {
            canvas.Circle(box, enabled ? FluentTheme.Accent : FluentTheme.TextDisabled);
            canvas.Circle(Rectangle.Inflate(box, -S(5), -S(5)), FluentTheme.OnAccent);
            return;
        }
        canvas.Circle(box, FluentTheme.ControlFill);
        canvas.Ring(box, enabled ? FluentTheme.ControlStroke : FluentTheme.TextDisabled, Math.Max(1f, scale));
    }

    private void PaintSwitch(Canvas canvas, Rectangle box, bool on, bool enabled)
    {
        var knob = S(12);
        var top = box.Top + (box.Height - knob) / 2;
        if (on)
        {
            canvas.Fill(box, enabled ? FluentTheme.Accent : FluentTheme.TextDisabled, box.Height / 2f);
            canvas.Circle(new Rectangle(box.Right - S(4) - knob, top, knob, knob), FluentTheme.OnAccent);
            return;
        }
        canvas.Outline(box, enabled ? FluentTheme.ControlStroke : FluentTheme.TextDisabled, box.Height / 2f, Math.Max(1f, scale));
        canvas.Circle(new Rectangle(box.Left + S(4), top, knob, knob), enabled ? FluentTheme.TextSecondary : FluentTheme.TextDisabled);
    }
}
