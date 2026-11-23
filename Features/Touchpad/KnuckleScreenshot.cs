using System.Drawing.Imaging;

namespace Art14Helper;

/// <summary>
/// The knuckle double-tap screenshot: the whole desktop (every monitor) as a PNG in
/// Pictures\Screenshots, named Screenshot_yyyyMMdd_HHmmss.png like HONOR's own.
/// </summary>
internal static class KnuckleScreenshot
{
    /// <summary>One knock pattern can arrive as several reports; one screenshot per this window.</summary>
    private const int RepeatWindowMilliseconds = 800;
    private static long _lastTaken = -RepeatWindowMilliseconds;

    internal static void Take()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Exchange(ref _lastTaken, now) < RepeatWindowMilliseconds)
            return;
        // A thread of its own: it switches to real pixels for the capture, and the
        // touchpad reader shouldn't wait for the PNG encoder.
        var worker = new Thread(Capture) { IsBackground = true, Name = "Knuckle screenshot" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private static void Capture()
    {
        try
        {
            ScreenArea.UseRealPixels();
            var area = ScreenArea.AllMonitors();
            using var shot = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (var canvas = Graphics.FromImage(shot))
                canvas.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);

            var path = FreeName(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"), DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            shot.Save(path, ImageFormat.Png);
            Log.Info($"Knuckle screenshot: {path}");
            // Also on the clipboard, as Windows' own screenshot keys do: ready to paste.
            var image = Clipboard.ImageData(shot);
            UiThread.Post(() => Popup.Show(PopupIcon.Screenshot,
                Clipboard.SetImage(image) ? "Screenshot copied" : "Screenshot saved", @"In Pictures\Screenshots"));
        }
        catch (Exception exception)
        {
            Log.Error("Knuckle screenshot not saved", exception);
        }
    }

    /// <summary>Screenshot_yyyyMMdd_HHmmss.png, with _2, _3 … added when that name is taken.</summary>
    internal static string FreeName(string folder, DateTime when, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var stem = "Screenshot_" + when.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(folder, stem + ".png");
        for (var copy = 2; exists(path); copy++)
            path = Path.Combine(folder, $"{stem}_{copy}.png");
        return path;
    }
}
