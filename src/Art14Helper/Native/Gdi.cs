using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>GDI: off-screen bitmaps, fonts, text and icons (gdi32, user32).</summary>
internal static unsafe partial class Gdi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    /// <summary>A memory DC with a 32-bit top-down bitmap selected into it.</summary>
    internal static (IntPtr Dc, IntPtr Bitmap, IntPtr Previous) CreateBuffer(int width, int height)
    {
        var header = new BitmapHeader
        {
            Size = (uint)sizeof(BitmapHeader),
            Width = width,
            Height = -height,   // negative: rows from the top
            Planes = 1,
            BitCount = 32
        };
        void* bits;
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var bitmap = dc == IntPtr.Zero ? IntPtr.Zero : CreateDIBSection(dc, &header, 0, &bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero)
        {
            if (dc != IntPtr.Zero)
                DeleteDC(dc);
            throw new OutOfMemoryException($"No {width}x{height} drawing buffer (GDI out of resources).");
        }
        return (dc, bitmap, SelectObject(dc, bitmap));
    }

    internal static void FreeBuffer((IntPtr Dc, IntPtr Bitmap, IntPtr Previous) buffer)
    {
        SelectObject(buffer.Dc, buffer.Previous);
        DeleteObject(buffer.Bitmap);
        DeleteDC(buffer.Dc);
    }

    internal static void Copy(IntPtr source, IntPtr target, Rectangle to)
        => BitBlt(target, to.X, to.Y, to.Width, to.Height, source, 0, 0, 0x00CC0020);   // SRCCOPY

    /// <summary>A ClearType font <paramref name="pixels"/> tall (em size, not cell height).</summary>
    internal static IntPtr CreateFont(string family, int pixels, bool semibold)
        => CreateFontW(-pixels, 0, 0, 0, semibold ? 600 : 400, 0, 0, 0, 1, 0, 0, 5, 0, family);   // DEFAULT_CHARSET, CLEARTYPE_QUALITY

    internal static int DrawText(IntPtr dc, string text, ref Rect box, uint format, Color color)
    {
        SetTextColor(dc, (uint)(color.R | color.G << 8 | color.B << 16));
        SetBkMode(dc, 1);   // TRANSPARENT
        fixed (char* chars = text)
        fixed (Rect* area = &box)
            return DrawTextW(dc, chars, text.Length, area, format);
    }

    internal static void DrawIcon(IntPtr dc, IntPtr icon, Rectangle box)
        => DrawIconEx(dc, box.X, box.Y, icon, box.Width, box.Height, 0, IntPtr.Zero, 3);   // DI_NORMAL

    [LibraryImport(Libraries.Gdi32)]
    internal static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport(Libraries.Gdi32)]
    private static partial IntPtr CreateDIBSection(IntPtr dc, BitmapHeader* header, uint usage, void** bits, IntPtr section, uint offset);

    [LibraryImport(Libraries.Gdi32)]
    internal static partial IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [LibraryImport(Libraries.Gdi32)]
    internal static partial int DeleteObject(IntPtr gdiObject);

    [LibraryImport(Libraries.Gdi32)]
    internal static partial int DeleteDC(IntPtr dc);

    [LibraryImport(Libraries.Gdi32)]
    private static partial int BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    [LibraryImport(Libraries.Gdi32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport(Libraries.Gdi32)]
    private static partial uint SetTextColor(IntPtr dc, uint color);

    [LibraryImport(Libraries.Gdi32)]
    private static partial int SetBkMode(IntPtr dc, int mode);

    [LibraryImport(Libraries.User32)]
    private static partial int DrawTextW(IntPtr dc, char* text, int length, Rect* box, uint format);

    [LibraryImport(Libraries.User32)]
    private static partial int DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint frame, IntPtr brush, uint flags);
}
