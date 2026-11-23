namespace Art14Helper;

/// <param name="Detail">Shown as a tooltip over the tile.</param>
internal sealed record StatusTile(string Glyph, string Label, string Value, string? Detail = null);

/// <param name="Switches">A click flips between two options (⇄); otherwise it opens the page (›).</param>
internal sealed record QuickSetting(string Glyph, string Title, string Value, bool Switches, Action OnClick, string Hint);

internal sealed record DeviceState(string Model, string Details, int? BatteryPercent, string BatteryLine, string LimitLine, bool Charging);

/// <summary>
/// The laptop at the top of Home: model and BIOS on the left; on the right the battery
/// as a ring with the charging state and the charge limit in words.
/// </summary>
internal sealed class DeviceCardPart(float scale) : Part
{
    private DeviceState? _state;

    private int S(float value) => (int)Math.Round(value * scale);

    internal DeviceState? State
    {
        get => _state;
        set
        {
            if (value == _state)
                return;
            _state = value;
            Redraw();
        }
    }

    internal override int HeightFor(int width) => S(128);

    internal override void Paint(Canvas canvas)
    {
        if (_state is not { } state)
            return;
        var box = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width - 1, Bounds.Height - 1);
        canvas.Fill(box, FluentTheme.Card, S(8));
        canvas.Outline(box, FluentTheme.Stroke, S(8));

        canvas.Text("", UiFont.Icons(44, scale), new Rectangle(box.X + S(24), box.Y, S(56), box.Height), FluentTheme.Accent,
            TextLayout.Center | TextLayout.Middle);
        var ringSize = S(76);
        var ring = new Rectangle(box.Right - S(28) - ringSize, box.Y + (box.Height - ringSize) / 2, ringSize, ringSize);
        var textRight = ring.Left - S(24);
        var nameLeft = box.X + S(100);
        // The name takes what it needs (at most half), the battery lines the rest.
        var nameWidth = Math.Min((textRight - nameLeft) / 2,
            Math.Max(UiFont.Strong(20, scale).Measure(state.Model).Width, UiFont.Text(13, scale).Measure(state.Details).Width));
        canvas.Text(state.Model, UiFont.Strong(20, scale), new Rectangle(nameLeft, box.Y + S(34), nameWidth, S(30)), FluentTheme.Text,
            TextLayout.Middle | TextLayout.Ellipsis);
        canvas.Text(state.Details, UiFont.Text(13, scale), new Rectangle(nameLeft, box.Y + S(66), nameWidth, S(22)), FluentTheme.TextTertiary,
            TextLayout.Middle | TextLayout.Ellipsis);

        var linesLeft = nameLeft + nameWidth + S(16);
        var linesWidth = Math.Max(0, textRight - linesLeft);
        canvas.Text(state.BatteryLine, UiFont.Text(14, scale), new Rectangle(linesLeft, box.Y + S(36), linesWidth, S(24)), FluentTheme.Text,
            TextLayout.Right | TextLayout.Middle | TextLayout.Ellipsis);
        canvas.Text(state.LimitLine, UiFont.Text(13, scale), new Rectangle(linesLeft, box.Y + S(64), linesWidth, S(22)), FluentTheme.TextTertiary,
            TextLayout.Right | TextLayout.Middle | TextLayout.Ellipsis);

        var thickness = S(7);
        var track = Rectangle.Inflate(ring, -thickness / 2, -thickness / 2);
        canvas.Ring(track, FluentTheme.Divider, thickness);
        if (state.BatteryPercent is > 0 and var percent)
        {
            var low = percent <= 15 && !state.Charging;
            canvas.Arc(track, low ? FluentTheme.Danger : FluentTheme.Accent, thickness, 3.6f * Math.Min(percent, 100));
        }
        canvas.Text(state.BatteryPercent is { } shown ? $"{shown}%" : "—", UiFont.Strong(18, scale), ring, FluentTheme.Text,
            TextLayout.Center | TextLayout.Middle);
    }
}

/// <summary>Live hardware values side by side in one card: read-only, so not tiles that look clickable.</summary>
internal sealed class StatusTilesPart(float scale) : Part
{
    private IReadOnlyList<StatusTile> _tiles = [];

    private int S(float value) => (int)Math.Round(value * scale);

    internal IReadOnlyList<StatusTile> Tiles
    {
        get => _tiles;
        set
        {
            if (value.SequenceEqual(_tiles))
                return;
            _tiles = value;
            Redraw();
        }
    }

    internal override int HeightFor(int width) => _tiles.Count == 0 ? 0 : S(84);

    internal override string? Tip(Point at)
    {
        if (_tiles.Count == 0 || !Bounds.Contains(at))
            return null;
        var index = Math.Clamp((at.X - Bounds.X) * _tiles.Count / Math.Max(1, Bounds.Width), 0, _tiles.Count - 1);
        return _tiles[index].Detail;
    }

    internal override void Paint(Canvas canvas)
    {
        if (_tiles.Count == 0)
            return;
        var box = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width - 1, Bounds.Height - 1);
        canvas.Fill(box, FluentTheme.Card, S(8));
        canvas.Outline(box, FluentTheme.Stroke, S(8));
        var width = box.Width / (float)_tiles.Count;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var left = box.X + (int)(i * width);
            if (i > 0)
                canvas.Line(left, box.Y + S(16), left, box.Bottom - S(16), FluentTheme.Divider);
            var iconLeft = left + S(20);
            canvas.Text(tile.Glyph, UiFont.Icons(18, scale), new Rectangle(iconLeft, box.Y, S(20), box.Height), FluentTheme.TextSecondary,
                TextLayout.Center | TextLayout.Middle);
            var textLeft = iconLeft + S(32);
            var textWidth = left + (int)width - S(8) - textLeft;
            canvas.Text(tile.Label, UiFont.Text(13, scale), new Rectangle(textLeft, box.Y + S(16), textWidth, S(20)), FluentTheme.TextSecondary,
                TextLayout.Middle | TextLayout.Ellipsis);
            canvas.Text(tile.Value, UiFont.Strong(16, scale), new Rectangle(textLeft, box.Y + S(40), textWidth, S(28)), FluentTheme.Text,
                TextLayout.Middle | TextLayout.Ellipsis);
        }
    }
}

/// <summary>Quick settings on Home, like Windows' own panel: one tile per setting with its value.</summary>
internal sealed class QuickTilesPart(float scale) : Part
{
    private IReadOnlyList<QuickSetting> _items = [];
    private int _hover = -1;
    private int _pressed = -1;

    private int S(float value) => (int)Math.Round(value * scale);

    private int TileHeight => S(76);

    private int Gap => S(10);

    private int ColumnsFor(int width) => Math.Clamp((width + Gap) / S(220), 1, 3);

    internal IReadOnlyList<QuickSetting> Items
    {
        get => _items;
        set
        {
            _items = value;
            Redraw();
        }
    }

    internal override int HeightFor(int width)
    {
        if (_items.Count == 0)
            return 0;
        var rows = (_items.Count + ColumnsFor(width) - 1) / ColumnsFor(width);
        return rows * TileHeight + (rows - 1) * Gap;
    }

    private Rectangle TileBox(int index)
    {
        var columns = ColumnsFor(Bounds.Width);
        var width = (Bounds.Width - Gap * (columns - 1) - 1) / columns;
        return new Rectangle(Bounds.X + index % columns * (width + Gap), Bounds.Y + index / columns * (TileHeight + Gap), width, TileHeight - 1);
    }

    private int TileAt(Point at)
    {
        for (var i = 0; i < _items.Count; i++)
            if (TileBox(i).Contains(at))
                return i;
        return -1;
    }

    internal override bool IsClickable(Point at) => TileAt(at) >= 0;

    internal override int FocusStops => _items.Count;

    internal override Rectangle FocusBounds(int stop) => TileBox(stop);

    internal override void Activate(int stop) => _items[stop].OnClick();

    internal override string? Tip(Point at) => TileAt(at) is var index and >= 0 ? _items[index].Hint : null;

    internal override void Hover(Point? at)
    {
        var index = at is { } point ? TileAt(point) : -1;
        if (index == _hover)
            return;
        _hover = index;
        Redraw();
    }

    internal override void Press(Point at)
    {
        _pressed = TileAt(at);
        Redraw();
    }

    internal override void Release(Point at, bool click)
    {
        var pressed = _pressed;
        _pressed = -1;
        Redraw();
        if (click && pressed >= 0 && TileAt(at) == pressed)
            _items[pressed].OnClick();
    }

    internal override void Paint(Canvas canvas)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var box = TileBox(i);
            var fill = i == _pressed ? FluentTheme.CardPressed : i == _hover ? FluentTheme.CardHover : FluentTheme.Card;
            canvas.Fill(box, fill, S(8));
            canvas.Outline(box, FluentTheme.Stroke, S(8));
            canvas.Text(item.Glyph, UiFont.Icons(20, scale), new Rectangle(box.X + S(18), box.Y, S(24), box.Height), FluentTheme.Accent,
                TextLayout.Center | TextLayout.Middle);
            var sign = new Rectangle(box.Right - S(40), box.Y, S(24), box.Height);
            canvas.Text(item.Switches ? "" : "", UiFont.Icons(13, scale), sign, FluentTheme.TextTertiary,
                TextLayout.Center | TextLayout.Middle);
            var textLeft = box.X + S(58);
            var textWidth = sign.Left - textLeft - S(8);
            canvas.Text(item.Title, UiFont.Text(13, scale), new Rectangle(textLeft, box.Y + S(14), textWidth, S(20)), FluentTheme.TextSecondary,
                TextLayout.Middle | TextLayout.Ellipsis);
            canvas.Text(item.Value, UiFont.Strong(15, scale), new Rectangle(textLeft, box.Y + S(36), textWidth, S(26)), FluentTheme.Text,
                TextLayout.Middle | TextLayout.Ellipsis);
        }
    }
}
