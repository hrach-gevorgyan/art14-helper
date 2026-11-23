namespace Art14Helper.Ui;

/// <summary>
/// A borderless, see-through, always-on-top window that never takes focus or clicks: it
/// shows a ready-made 32-bit image (per-pixel alpha), fades in when it appears, and can
/// fade out and hide itself after a delay, as Windows' own flyouts do.
/// </summary>
internal sealed class LayeredWindow : Window
{
    private const uint HideTimer = 1, FadeTimer = 2;
    private const uint FadeTick = 16;                       // about one frame
    private const int FadeInMilliseconds = 120, FadeOutMilliseconds = 160;
    private const uint Topmost = 0x8, ClickThrough = 0x20, Layered = 0x80000, NoActivate = 0x08000000;   // WS_EX_*

    private bool _visible;
    private int _opacity;
    private int _fadeStep;   // per tick: positive fading in, negative fading out, 0 still

    internal LayeredWindow()
        => Create("Art14Helper.Layered", null, User32.Popup, Topmost | User32.ToolWindow | Layered | NoActivate | ClickThrough);

    /// <summary>
    /// Shows <paramref name="image"/> with its top-left corner at a screen point (physical
    /// pixels). Fades in when it wasn't showing; replaces the picture at once when it was.
    /// </summary>
    internal void Present(Bitmap image, Point topLeft)
    {
        var appearing = !_visible;
        _opacity = appearing ? 0 : 255;
        var screen = User32.GetDC(IntPtr.Zero);
        var memory = Gdi.CreateCompatibleDC(screen);
        var bitmap = image.GetHbitmap(Color.FromArgb(0));   // keeps the alpha channel
        var previous = Gdi.SelectObject(memory, bitmap);
        try
        {
            User32.PresentLayered(Handle, screen, topLeft, image.Size, memory, (byte)_opacity);
            User32.ShowWindow(Handle, User32.ShowNoActivate);
            _visible = true;
        }
        finally
        {
            Gdi.SelectObject(memory, previous);
            Gdi.DeleteObject(bitmap);
            Gdi.DeleteDC(memory);
            User32.ReleaseDC(IntPtr.Zero, screen);
        }
        Fade(appearing ? 255 * (int)FadeTick / FadeInMilliseconds + 1 : 0);
    }

    /// <summary>Fades out and hides <paramref name="milliseconds"/> from now (restarting any earlier countdown).</summary>
    internal void HideAfter(uint milliseconds) => User32.SetTimer(Handle, HideTimer, milliseconds, IntPtr.Zero);

    /// <summary>Gone at once, no fade.</summary>
    internal void Hide()
    {
        User32.KillTimer(Handle, HideTimer);
        Fade(0);
        User32.ShowWindow(Handle, User32.Hide);
        _visible = false;
    }

    private void Fade(int step)
    {
        _fadeStep = step;
        if (step == 0)
            User32.KillTimer(Handle, FadeTimer);
        else
            User32.SetTimer(Handle, FadeTimer, FadeTick, IntPtr.Zero);
    }

    protected override nint? Receive(uint message, nint wParam, nint lParam)
    {
        if (message != WindowMessage.Timer)
            return null;
        if ((uint)wParam == HideTimer)
        {
            User32.KillTimer(Handle, HideTimer);
            Fade(-(255 * (int)FadeTick / FadeOutMilliseconds + 1));
            return 0;
        }
        _opacity = Math.Clamp(_opacity + _fadeStep, 0, 255);
        User32.SetLayeredOpacity(Handle, (byte)_opacity);
        if (_fadeStep > 0 && _opacity == 255)
            Fade(0);
        else if (_fadeStep < 0 && _opacity == 0)
            Hide();
        return 0;
    }
}
