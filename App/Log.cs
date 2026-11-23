namespace Art14Helper;

/// <summary>
/// The app's log, one line per entry, in <see cref="AppFolders.Logs"/>: in the program folder
/// for the elevated app (only administrators can change it), in %LOCALAPPDATA%\Art14Helper
/// otherwise. When it passes 1 MB it's renamed to .log.1 (replacing the previous one) and a
/// new file starts, so it never grows past about 2 MB. Logging never throws.
/// </summary>
internal static class Log
{
    private const long RollOverBytes = 1 << 20;
    private static readonly Lock WriteLock = new();

    /// <summary>Nothing is written while true: the unit tests set it, so their deliberate failures stay out of the real log.</summary>
    internal static bool Muted { get; set; }

    /// <summary>Where the current log file is.</summary>
    internal static string Location { get; } = Path.Combine(AppFolders.Logs, "Art14Helper.log");

    internal static void Info(string message) => Append("info ", message, null);

    internal static void Error(string message, Exception? exception = null) => Append("error", message, exception);

    /// <summary>One entry: local time to the millisecond, level, message, and the exception if any.</summary>
    internal static string Format(DateTime when, string level, string message, Exception? exception)
        => $"{when:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{(exception is null ? "" : " — " + exception)}";

    private static void Append(string level, string message, Exception? exception)
    {
        if (Muted)
            return;
        var line = Format(DateTime.Now, level, message, exception) + Environment.NewLine;
        lock (WriteLock)
        {
            try
            {
                Write(line);
            }
            catch (DirectoryNotFoundException)
            {
                // The folder went away (cleaned up, uninstalled and reinstalled): make it again.
                Try(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
                    Write(line);
                });
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Nowhere left to report it; the app must keep running without a log.
            }
        }
    }

    private static void Write(string line)
    {
        if (File.Exists(Location) && new FileInfo(Location).Length > RollOverBytes)
            File.Move(Location, Location + ".1", overwrite: true);
        File.AppendAllText(Location, line);
    }

    private static void Try(Action write)
    {
        try
        {
            write();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}
