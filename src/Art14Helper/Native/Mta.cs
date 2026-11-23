using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// The multithreaded COM apartment, where the app keeps its WMI and sensor objects. Work
/// started on an STA thread (the UI thread) is carried out on a pool thread and waited for,
/// so those objects are only ever touched from the apartment they belong to.
/// </summary>
internal static partial class Mta
{
    internal static T Run<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            var task = Task.Run(() => Run(work));
            // A managed wait on an STA thread pumps messages, which would let queued UI work
            // run in the middle of this call (another BIOS command, say). A plain kernel
            // wait doesn't; the calls made this way are short.
            WaitForSingleObject(((IAsyncResult)task).AsyncWaitHandle.SafeWaitHandle.DangerousGetHandle(), uint.MaxValue);
            return task.GetAwaiter().GetResult();
        }
        WbemCom.CoInitializeEx(IntPtr.Zero, 0);   // COINIT_MULTITHREADED; "already done" is fine
        return work();
    }

    internal static void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    [LibraryImport(Libraries.Kernel32)]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}
