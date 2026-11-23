namespace Art14Helper.Ui;

/// <summary>
/// A top-level window drawn entirely by the app, Windows 11 Settings style: dark title
/// bar, the app icon, and one or more <see cref="Surface"/>s that get the paint and the
/// mouse. Sizes follow the monitor's scale and change when the window moves to another.
/// </summary>
internal abstract class PaintedWindow : Window
{
    private const uint TipDelay = 600;

    private readonly List<Surface> _surfaces = [];
    private readonly UiTimer _tipTimer;
    private Surface? _pressed;
    private bool _leaveTracked;
    private TrackingTooltip? _tooltip;
    private string? _tip;
    private IntPtr _bigIcon, _smallIcon;

    protected PaintedWindow() => _tipTimer = new UiTimer(TipDelay, ShowTip);

    internal new float Scale { get; private set; } = 1f;

    protected int S(float value) => (int)Math.Round(value * Scale);

    /// <summary>Creates the window hidden at <paramref name="bounds"/> (physical pixels) at a known scale.</summary>
    protected void Open(string className, string title, uint style, Rectangle bounds, float scale)
    {
        Scale = scale;
        Create(className, title, style, User32.AppWindow, bounds);
        Scale = base.Scale;
        WindowTheme.TitleBar(Handle, FluentTheme.IsDark, FluentTheme.Background);
        SetIcons();
        _tooltip = new TrackingTooltip(Handle);
        ScaleChanged();
        Layout();
    }

    protected Surface AddSurface(bool scrolls = false)
    {
        var surface = new Surface(this) { Scrolls = scrolls, Scale = Scale, TipChanged = OnTipChanged };
        _surfaces.Add(surface);
        return surface;
    }

    /// <summary>Places the surfaces and their parts for the current client size.</summary>
    protected abstract void Layout();

    /// <summary>The scale changed (also called once at the start): rebuild fonts.</summary>
    protected virtual void ScaleChanged() { }

    /// <summary>A key press; true when handled.</summary>
    protected virtual bool KeyPressed(int key) => false;

    /// <summary>Smallest size the user can drag the window to, in physical pixels; empty for none.</summary>
    protected virtual Size MinimumSize => Size.Empty;

    protected Size ClientSize => User32.ClientArea(Handle).Size;

    internal void ShowAndActivate()
    {
        if (User32.IsIconic(Handle) != 0)
            User32.ShowWindow(Handle, User32.Restore);
        else
            User32.ShowWindow(Handle, User32.Show);
        User32.SetForegroundWindow(Handle);
    }

    internal void Close() => Dispose();

    private Surface? SurfaceAt(Point client) => _surfaces.FirstOrDefault(surface => surface.Contains(client));

    private static Point PointFrom(nint lParam) => new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    protected override unsafe nint? Receive(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessage.Paint:
                User32.PaintInfo paint;
                var dc = User32.BeginPaint(Handle, &paint);
                try
                {
                    foreach (var surface in _surfaces)
                        surface.Paint(dc);
                }
                finally
                {
                    User32.EndPaint(Handle, &paint);   // also when drawing failed (low on GDI): no leaked DC
                }
                return 0;
            case WindowMessage.EraseBackground:
                return 1;   // the surfaces cover everything
            case WindowMessage.Size:
                Layout();
                Invalidate();
                return 0;
            case WindowMessage.MouseMove:
                var point = PointFrom(lParam);
                if (!_leaveTracked)
                {
                    User32.TrackLeave(Handle);
                    _leaveTracked = true;
                }
                var over = _pressed ?? SurfaceAt(point);
                foreach (var surface in _surfaces.Where(surface => surface != over))
                    surface.MouseLeave();
                over?.MouseMove(point);
                return 0;
            case WindowMessage.MouseLeave:
                _leaveTracked = false;
                foreach (var surface in _surfaces)
                    surface.MouseLeave();
                return 0;
            case WindowMessage.LeftDown:
                foreach (var surface in _surfaces)
                {
                    // The mouse took over: no ring, and no hidden focus for a later Space to trigger.
                    surface.ShowFocus = false;
                    surface.ClearFocus();
                }
                _pressed = SurfaceAt(PointFrom(lParam));
                if (_pressed is not null)
                {
                    User32.SetCapture(Handle);
                    _pressed.Press(PointFrom(lParam));
                }
                return 0;
            case WindowMessage.LeftUp:
                var released = _pressed;
                _pressed = null;
                User32.ReleaseCapture();
                released?.Release(PointFrom(lParam));
                return 0;
            case WindowMessage.CaptureChanged:
                _pressed?.Cancel();
                _pressed = null;
                return 0;
            case WindowMessage.MouseWheel:
                var at = User32.ToClient(Handle, PointFrom(lParam));
                (SurfaceAt(at) ?? _surfaces.FirstOrDefault(surface => surface.Scrolls))?.Wheel((short)((wParam >> 16) & 0xFFFF));
                return 0;
            case WindowMessage.SetCursor when (lParam & 0xFFFF) == User32.HitClient:
                var cursor = User32.ToClient(Handle, User32.CursorPosition);
                User32.SetCursor(hand: SurfaceAt(cursor)?.HandAt(cursor) == true);
                return 1;
            case WindowMessage.KeyDown:
                var key = (int)wParam;
                if (key == User32.KeyTab)
                {
                    MoveFocus(backward: User32.IsShiftDown);
                    return 0;
                }
                // Only a visible focus acts, and only on the first press, not on auto-repeat (a held
                // Space would flip a hardware switch over and over).
                var repeat = (lParam & (1 << 30)) != 0;
                if (key is User32.KeyEnter or User32.KeySpace && !repeat
                    && _surfaces.FirstOrDefault(surface => surface is { HasFocus: true, ShowFocus: true })?.ActivateFocus() == true)
                    return 0;
                return KeyPressed(key) ? 0 : null;
            case WindowMessage.GetMinMaxInfo when !MinimumSize.IsEmpty:
                var limits = (User32.SizeLimits*)lParam;
                limits->MinTrackSize = new Point(MinimumSize.Width, MinimumSize.Height);
                return 0;
            case WindowMessage.DpiChanged:
                Scale = ((int)wParam & 0xFFFF) / 96f;
                foreach (var surface in _surfaces)
                    surface.Scale = Scale;
                SetIcons();
                ScaleChanged();
                User32.Move(Handle, ((Rect*)lParam)->ToRectangle());   // Windows' suggested size at the new scale
                Layout();
                Invalidate();
                return 0;
            case WindowMessage.SettingChange or WindowMessage.ThemeChanged:
                // Light or dark mode, or the accent, may have changed: repaint in the new colors.
                FluentTheme.Reload();
                WindowTheme.TitleBar(Handle, FluentTheme.IsDark, FluentTheme.Background);
                Invalidate();
                return null;
            case WindowMessage.Destroy:
                _tipTimer.Dispose();
                _tooltip?.Dispose();
                FreeIcons();
                foreach (var surface in _surfaces)
                    surface.Dispose();
                return null;
        }
        return null;
    }

    /// <summary>Tab and Shift+Tab: through the focus stops of each surface in turn, wrapping around.</summary>
    private void MoveFocus(bool backward)
    {
        if (_surfaces.Count == 0)
            return;
        var current = _surfaces.FindIndex(surface => surface.HasFocus);
        var index = current >= 0 ? current : backward ? _surfaces.Count - 1 : 0;
        for (var tries = 0; tries <= _surfaces.Count; tries++)
        {
            if (_surfaces[index].MoveFocus(backward))
                break;
            index = (index + (backward ? _surfaces.Count - 1 : 1)) % _surfaces.Count;
        }
        foreach (var surface in _surfaces)
            surface.ShowFocus = true;
        Invalidate();
    }

    private void OnTipChanged(string? text)
    {
        if (text == _tip)
            return;
        _tip = text;
        _tooltip?.Hide();
        if (text is null)
            _tipTimer.Stop();
        else
            _tipTimer.Start();
    }

    private void ShowTip()
    {
        _tipTimer.Stop();
        if (_tip is null || !IsOpen)
            return;
        var pointer = User32.CursorPosition;
        _tooltip?.Show(_tip, pointer.X + S(16), pointer.Y + S(20));
    }

    private void SetIcons()
    {
        var (oldBig, oldSmall) = (_bigIcon, _smallIcon);
        _bigIcon = IconApi.AppIcon(S(32));
        _smallIcon = IconApi.AppIcon(S(16));
        const uint SetIcon = 0x0080;   // WM_SETICON
        User32.SendMessageW(Handle, SetIcon, 1, _bigIcon);
        User32.SendMessageW(Handle, SetIcon, 0, _smallIcon);
        if (oldBig != IntPtr.Zero)
            IconApi.Destroy(oldBig);
        if (oldSmall != IntPtr.Zero)
            IconApi.Destroy(oldSmall);
    }

    private void FreeIcons()
    {
        if (_bigIcon != IntPtr.Zero)
            IconApi.Destroy(_bigIcon);
        if (_smallIcon != IntPtr.Zero)
            IconApi.Destroy(_smallIcon);
        _bigIcon = _smallIcon = IntPtr.Zero;
    }
}
