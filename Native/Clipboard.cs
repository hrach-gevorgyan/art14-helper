using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Art14Helper.Native;

/// <summary>
/// Text and pictures onto the Windows clipboard. Call on the UI thread: Windows wants an
/// owner window, and the UI thread's own one serves (the active window when there is one).
/// </summary>
internal static unsafe partial class Clipboard
{
    private const uint UnicodeText = 13, Dib = 8;   // CF_UNICODETEXT, CF_DIB
    private const uint Moveable = 0x0002;           // GMEM_MOVEABLE

    internal static bool SetText(string text) => Set((UnicodeText, Encoding.Unicode.GetBytes(text + '\0')));

    /// <summary>
    /// A picture as a classic bitmap (every app can paste it) and as PNG (what browsers,
    /// chat apps and Office prefer). The data can be made on any thread with <see cref="ImageData"/>.
    /// </summary>
    internal static bool SetImage((byte[] Dib, byte[] Png) image) => Set((Dib, image.Dib), (RegisterClipboardFormatW("PNG"), image.Png));

    internal static (byte[] Dib, byte[] Png) ImageData(Bitmap image)
    {
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        return (DibOf(image), png.ToArray());
    }

    /// <summary>BITMAPINFOHEADER plus 32-bit rows, bottom row first, as CF_DIB expects.</summary>
    private static byte[] DibOf(Bitmap image)
    {
        const int HeaderSize = 40;
        var (width, height) = (image.Width, image.Height);
        var stride = width * 4;
        var data = new byte[HeaderSize + stride * height];
        fixed (byte* start = data)
        {
            var header = (int*)start;
            header[0] = HeaderSize;
            header[1] = width;
            header[2] = height;                          // positive: bottom-up
            ((short*)start)[6] = 1;                      // planes
            ((short*)start)[7] = 32;                     // bits per pixel; compression 0 = BI_RGB
            header[5] = stride * height;                 // image size
        }
        var bits = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(bits.Scan0 + row * bits.Stride, data, HeaderSize + (height - 1 - row) * stride, stride);
        }
        finally
        {
            image.UnlockBits(bits);
        }
        return data;
    }

    /// <summary>
    /// All memory is prepared before the clipboard is touched, so a failure leaves the user's
    /// clipboard as it was. True when at least one format went in.
    /// </summary>
    private static bool Set(params (uint Format, byte[] Data)[] items)
    {
        var blocks = new List<(uint Format, IntPtr Memory)>();
        foreach (var (format, data) in items)
        {
            if (format == 0 || Copy(data) is not { } memory)
                continue;
            blocks.Add((format, memory));
        }
        if (blocks.Count == 0)
            return false;

        var owner = User32.GetActiveWindow() is var active && active != IntPtr.Zero ? active : UiThread.Window;
        if (OpenClipboard(owner) == 0)
        {
            blocks.ForEach(block => GlobalFree(block.Memory));
            return false;
        }
        var placed = 0;
        try
        {
            EmptyClipboard();
            foreach (var (format, memory) in blocks)
            {
                if (SetClipboardData(format, memory) != IntPtr.Zero)
                    placed++;
                else
                    GlobalFree(memory);   // still ours when the clipboard didn't take it
            }
        }
        finally
        {
            CloseClipboard();
        }
        return placed > 0;
    }

    private static IntPtr? Copy(byte[] data)
    {
        var memory = GlobalAlloc(Moveable, (nuint)data.Length);
        if (memory == IntPtr.Zero)
            return null;
        var target = GlobalLock(memory);
        if (target == null)
        {
            GlobalFree(memory);
            return null;
        }
        Marshal.Copy(data, 0, (IntPtr)target, data.Length);
        GlobalUnlock(memory);
        return memory;
    }

    [LibraryImport(Libraries.User32)]
    private static partial int OpenClipboard(IntPtr owner);

    [LibraryImport(Libraries.User32)]
    private static partial int EmptyClipboard();

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr data);

    [LibraryImport(Libraries.User32)]
    private static partial int CloseClipboard();

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport(Libraries.Kernel32)]
    private static partial void* GlobalLock(IntPtr memory);

    [LibraryImport(Libraries.Kernel32)]
    private static partial int GlobalUnlock(IntPtr memory);

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
