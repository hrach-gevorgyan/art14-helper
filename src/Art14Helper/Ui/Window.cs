using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Art14Helper.Ui;

/// <summary>
/// Base for the app's windows: registers the class, routes messages to <see cref="Receive"/>
/// and forgets the window once Windows destroys it. An exception in a handler is logged,
/// never let through to Windows, so one broken handler doesn't end the app.
/// </summary>
internal abstract unsafe class Window : IDisposable
{
    private static readonly Dictionary<IntPtr, Window> Open = [];
    private static readonly HashSet<string> Registered = [];
    [ThreadStatic] private static Window? _creating;

    internal IntPtr Handle { get; private set; }

    internal bool IsOpen => Handle != IntPtr.Zero;

    /// <summary>Raised once Windows has destroyed the window (closed by the user or disposed).</summary>
    internal event Action? Destroyed;

    protected void Create(string className, string? title, uint style, uint exStyle = 0, Rectangle bounds = default, IntPtr parent = default)
    {
        Register(className);
        _creating = this;
        try
        {
            var size = bounds.IsEmpty ? new Rectangle(User32.UseDefault, User32.UseDefault, User32.UseDefault, User32.UseDefault) : bounds;
            var handle = User32.CreateWindowExW(exStyle, className, title, style, size.X, size.Y, size.Width, size.Height,
                parent, IntPtr.Zero, User32.Module, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Window {className} wasn't created.");
        }
        finally
        {
            _creating = null;
        }
    }

    /// <summary>Handles a message; null leaves it to Windows' default handling.</summary>
    protected virtual nint? Receive(uint message, nint wParam, nint lParam) => null;

    internal float Scale => IsOpen && User32.GetDpiForWindow(Handle) is var dpi and > 0 ? dpi / 96f : 1f;

    internal void Invalidate()
    {
        if (IsOpen)
            User32.Invalidate(Handle);
    }

    public virtual void Dispose()
    {
        if (IsOpen)
            User32.DestroyWindow(Handle);
        GC.SuppressFinalize(this);
    }

    private static void Register(string className)
    {
        if (Registered.Contains(className))
            return;
        fixed (char* name = className)
        {
            var windowClass = new User32.WindowClass
            {
                Size = (uint)sizeof(User32.WindowClass),
                Procedure = &Route,
                Instance = User32.Module,
                Cursor = User32.ArrowCursor,
                ClassName = name
            };
            // Remembered only when it worked, so a failure is retried instead of turning every
            // later window into a confusing "class not found".
            if (User32.RegisterClassExW(&windowClass) != 0)
                Registered.Add(className);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Route(IntPtr window, uint message, nint wParam, nint lParam)
    {
        if (!Open.TryGetValue(window, out var owner) && _creating is { } creating)
        {
            // The first messages arrive inside CreateWindowEx, before it returns the handle.
            creating.Handle = window;
            Open[window] = owner = creating;
        }
        if (owner is null)
            return User32.DefWindowProcW(window, message, wParam, lParam);

        try
        {
            if (message == WindowMessage.NcDestroy)
            {
                Open.Remove(window);
                owner.Handle = IntPtr.Zero;
                owner.Destroyed?.Invoke();
            }
            else if (owner.Receive(message, wParam, lParam) is { } handled)
                return handled;
        }
        catch (Exception exception)
        {
            Log.Error($"{owner.GetType().Name}: message 0x{message:X4} failed (the app keeps running)", exception);
        }
        return User32.DefWindowProcW(window, message, wParam, lParam);
    }
}
