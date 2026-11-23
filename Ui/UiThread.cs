using System.Collections.Concurrent;

namespace Art14Helper.Ui;

/// <summary>
/// The one thread that owns every window: its message loop, work handed over from other
/// threads (<see cref="Post"/>, and <c>await</c> continuations started on it), and timers.
/// </summary>
internal static class UiThread
{
    private const uint RunQueued = WindowMessage.App + 0x40;
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static readonly Dictionary<nuint, Action> Timers = [];
    private static nuint _lastTimer;
    private static Inbox? _inbox;
    private static int _threadId;

    /// <summary>Makes the calling thread the UI thread. Call once, before any window exists.</summary>
    internal static void Start()
    {
        _threadId = Environment.CurrentManagedThreadId;
        _inbox = new Inbox();
        SynchronizationContext.SetSynchronizationContext(new Context());
    }

    internal static bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>A window of the UI thread that is always there (the clipboard needs an owner window).</summary>
    internal static IntPtr Window => _inbox?.Handle ?? IntPtr.Zero;

    /// <summary>Runs <paramref name="work"/> on the UI thread soon. Safe from any thread; dropped before <see cref="Start"/>.</summary>
    internal static void Post(Action work)
    {
        if (_inbox is not { IsOpen: true } inbox)
            return;
        Queue.Enqueue(work);
        User32.PostMessageW(inbox.Handle, RunQueued, 0, 0);
    }

    /// <summary>Handles messages until <see cref="Quit"/>.</summary>
    internal static void Run() => Pump(() => false);

    /// <summary>
    /// Handles messages until <paramref name="done"/> says so: a modal window's own loop.
    /// A quit request that arrives meanwhile is passed on to the outer loop.
    /// </summary>
    internal static void RunUntil(Func<bool> done)
    {
        if (!Pump(done))
            User32.PostQuitMessage(0);
    }

    /// <summary>Ends <see cref="Run"/> after the current message. Call on the UI thread.</summary>
    internal static void Quit() => User32.PostQuitMessage(0);

    /// <summary>Returns false when the loop ended on a quit request.</summary>
    private static unsafe bool Pump(Func<bool> done)
    {
        User32.QueuedMessage message;
        while (!done())
        {
            if (User32.GetMessageW(&message, IntPtr.Zero, 0, 0) <= 0)
                return false;
            User32.TranslateMessage(&message);
            User32.DispatchMessageW(&message);
        }
        return true;
    }

    /// <summary>Calls <paramref name="tick"/> every <paramref name="milliseconds"/> until stopped; returns its id.</summary>
    internal static nuint StartTimer(uint milliseconds, Action tick, nuint id = 0)
    {
        if (_inbox is not { IsOpen: true } inbox)
            return 0;
        if (id == 0)
            id = ++_lastTimer;
        Timers[id] = tick;
        User32.SetTimer(inbox.Handle, id, milliseconds, IntPtr.Zero);   // the same id restarts the countdown
        return id;
    }

    internal static void StopTimer(nuint id)
    {
        if (id == 0 || !Timers.Remove(id) || _inbox is not { IsOpen: true } inbox)
            return;
        User32.KillTimer(inbox.Handle, id);
    }

    /// <summary>Calls <paramref name="work"/> once, <paramref name="milliseconds"/> from now.</summary>
    internal static void After(uint milliseconds, Action work)
    {
        nuint id = 0;
        id = StartTimer(milliseconds, () =>
        {
            StopTimer(id);
            work();
        });
    }

    /// <summary>Hidden message-only window that receives the hand-over and timer messages.</summary>
    private sealed class Inbox : Window
    {
        internal Inbox() => Create("Art14Helper.Inbox", null, 0, parent: User32.MessageOnly);

        protected override nint? Receive(uint message, nint wParam, nint lParam)
        {
            if (message == RunQueued)
            {
                // One message may find several items queued (or none): drain what's there.
                while (Queue.TryDequeue(out var work))
                    Guarded(work);
                return 0;
            }
            if (message == WindowMessage.Timer)
            {
                if (Timers.TryGetValue((nuint)wParam, out var tick))
                    Guarded(tick);
                return 0;
            }
            return null;
        }

        private static void Guarded(Action work)
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                Log.Error("UI work failed (the app keeps running)", exception);
            }
        }
    }

    /// <summary>Brings <c>await</c> continuations back to the UI thread.</summary>
    private sealed class Context : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => UiThread.Post(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (IsCurrent)
            {
                callback(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            UiThread.Post(() =>
            {
                try
                {
                    callback(state);
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}

/// <summary>A repeating timer on the UI thread. <see cref="Start"/> again restarts the countdown.</summary>
internal sealed class UiTimer(uint milliseconds, Action tick) : IDisposable
{
    private nuint _id;

    internal bool Running => _id != 0;

    internal void Start() => _id = UiThread.StartTimer(milliseconds, tick, _id);

    internal void Stop()
    {
        UiThread.StopTimer(_id);
        _id = 0;
    }

    public void Dispose() => Stop();
}
