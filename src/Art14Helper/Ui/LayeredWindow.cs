using System.Diagnostics;

namespace Art14Helper.Ui;

/// <summary>
/// A borderless, see-through, always-on-top window that never takes focus or clicks: it
/// shows a ready-made 32-bit image (per-pixel alpha), rises into place while fading in when
/// it appears, and can sink and fade out after a delay, as Windows' own flyouts do.
/// </summary>
/// <remarks>
/// The animation follows the clock, not the timer: Windows timers often fire late, so each
/// frame works out where the window should be at this moment. Position and opacity go to
/// the screen in one update.
/// </remarks>
internal sealed class LayeredWindow : Window
{
    private const uint HideTimer = 1, FrameTimer = 2;
    private const uint FrameTick = 10;   // the shortest timer Windows gives; frames land on its clock

    /// <summary>Appearing: fully visible early, then gliding the last pixels into place.</summary>
    internal const double FadeInMs = 150, RiseMs = 330;

    /// <summary>Leaving: fading while sinking back by part of the rise.</summary>
    internal const double FadeOutMs = 170;
    private const double SinkShare = 0.5;

    private const uint Topmost = 0x8, ClickThrough = 0x20, Layered = 0x80000, NoActivate = 0x08000000;   // WS_EX_*

    private enum Motion { Still, In, Out }

    private bool _visible;
    private Motion _motion;
    private long _motionStart;   // Stopwatch timestamp
    private Point _place;        // where the picture rests
    private int _rise;           // pixels it starts below that place when appearing

    internal LayeredWindow()
        => Create("Art14Helper.Layered", null, User32.Popup, Topmost | User32.ToolWindow | Layered | NoActivate | ClickThrough);

    /// <summary>
    /// Shows <paramref name="image"/> with its top-left corner at a screen point (physical
    /// pixels). When it wasn't showing, it rises <paramref name="rise"/> pixels into place
    /// while fading in; when it was (even while leaving), the picture is replaced in place.
    /// </summary>
    internal void Present(Bitmap image, Point topLeft, int rise = 0)
    {
        var appearing = !_visible;
        _place = topLeft;
        _rise = Math.Max(rise, 0);
        var start = appearing ? new Point(topLeft.X, topLeft.Y + _rise) : topLeft;
        var screen = User32.GetDC(IntPtr.Zero);
        var memory = Gdi.CreateCompatibleDC(screen);
        var bitmap = image.GetHbitmap(Color.FromArgb(0));   // keeps the alpha channel
        var previous = Gdi.SelectObject(memory, bitmap);
        try
        {
            User32.PresentLayered(Handle, screen, start, image.Size, memory, appearing ? (byte)0 : (byte)255);
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
        Animate(appearing ? Motion.In : Motion.Still);
    }

    /// <summary>Sinks, fades out and hides <paramref name="milliseconds"/> from now (restarting any earlier countdown).</summary>
    internal void HideAfter(uint milliseconds) => User32.SetTimer(Handle, HideTimer, milliseconds, IntPtr.Zero);

    /// <summary>Gone at once, no animation.</summary>
    internal void Hide()
    {
        User32.KillTimer(Handle, HideTimer);
        Animate(Motion.Still);
        User32.ShowWindow(Handle, User32.Hide);
        _visible = false;
    }

    /// <summary>
    /// Where the window is <paramref name="elapsedMs"/> into appearing: pixels still below
    /// its place, and opacity (0–255). The fade is quick and soft; the rise decelerates
    /// strongly, like Fluent's own "decelerate" motion, so it settles without a visible stop.
    /// </summary>
    internal static (int Below, byte Opacity) AppearFrame(int rise, double elapsedMs)
    {
        var fade = EaseOutCubic(elapsedMs / FadeInMs);
        var glide = EaseOutQuint(elapsedMs / RiseMs);
        return ((int)Math.Round(rise * (1 - glide)), (byte)Math.Round(255 * fade));
    }

    /// <summary>The same for leaving: pixels sunk below its place, and opacity. Gentle at first, then quicker.</summary>
    internal static (int Below, byte Opacity) LeaveFrame(int rise, double elapsedMs)
    {
        var t = EaseInCubic(elapsedMs / FadeOutMs);
        return ((int)Math.Round(rise * SinkShare * t), (byte)Math.Round(255 * (1 - t)));
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    private static double EaseOutQuint(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 5);
    private static double EaseInCubic(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);

    private void Animate(Motion motion)
    {
        _motion = motion;
        _motionStart = Stopwatch.GetTimestamp();
        if (motion == Motion.Still)
            User32.KillTimer(Handle, FrameTimer);
        else
            User32.SetTimer(Handle, FrameTimer, FrameTick, IntPtr.Zero);
    }

    protected override nint? Receive(uint message, nint wParam, nint lParam)
    {
        if (message != WindowMessage.Timer)
            return null;
        if ((uint)wParam == HideTimer)
        {
            User32.KillTimer(Handle, HideTimer);
            Animate(Motion.Out);
            return 0;
        }
        if (_motion == Motion.Still)
            return 0;

        var elapsed = Stopwatch.GetElapsedTime(_motionStart).TotalMilliseconds;
        var (below, opacity) = _motion == Motion.In ? AppearFrame(_rise, elapsed) : LeaveFrame(_rise, elapsed);
        User32.SetLayeredFrame(Handle, new Point(_place.X, _place.Y + below), opacity);

        if (_motion == Motion.In && elapsed >= Math.Max(FadeInMs, RiseMs))
            Animate(Motion.Still);
        else if (_motion == Motion.Out && elapsed >= FadeOutMs)
            Hide();
        return 0;
    }
}
