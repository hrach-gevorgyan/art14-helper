namespace Art14Helper;

/// <summary>
/// The app's log, one line per entry, in <see cref="AppFolders.Logs"/>: in the program folder
/// for the elevated app (only administrators can change it), in %LOCALAPPDATA%\Art14Helper
/// otherwise. When it passes 1 MB it's renamed to .log.1 (replacing the previous one) and a
/// new file starts, so it never grows past about 2 MB. Logging never throws.
/// </summary>
/// <remarks>
/// Kind to the disk even when something goes wrong in a loop: at most
/// <see cref="LinesPerMinute"/> lines a minute (the rest are counted, and the count is written
/// once the minute is over), and no line longer than <see cref="MaxLineLength"/> characters.
/// </remarks>
internal static class Log
{
    private const long RollOverBytes = 1 << 20;
    internal const int LinesPerMinute = 120;
    internal const int MaxLineLength = 4000;
    private static readonly Lock WriteLock = new();
    private static readonly FloodGuard Guard = new(LinesPerMinute, 60_000);

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
        var line = Shorten(Format(DateTime.Now, level, message, exception)) + Environment.NewLine;
        lock (WriteLock)
        {
            var (write, skipped) = Guard.Admit(Environment.TickCount64);
            if (skipped > 0)
                line = Format(DateTime.Now, "info ", $"{skipped} log lines skipped in the last minute (too many at once)", null) + Environment.NewLine + line;
            if (!write)
                return;
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

    /// <summary>A line cut to <see cref="MaxLineLength"/> characters (a long exception chain is mostly repetition).</summary>
    internal static string Shorten(string line)
        => line.Length <= MaxLineLength ? line : line[..(MaxLineLength - 1)] + "…";

    /// <summary>
    /// Lets at most <c>limit</c> lines through per window of <c>windowMs</c>. The first line
    /// of a new window carries how many the previous window held back. Not thread-safe:
    /// called under the write lock.
    /// </summary>
    internal sealed class FloodGuard(int limit, long windowMs)
    {
        private long? _windowStart;   // none before the first line (a sentinel like long.MinValue would overflow)
        private int _count;
        private int _skipped;

        /// <summary>Whether this line may be written, and how many were held back before it (to report once).</summary>
        internal (bool Write, int Skipped) Admit(long now)
        {
            if (_windowStart is not { } start || now - start >= windowMs)
            {
                _windowStart = now;
                _count = 0;
            }
            if (_count >= limit)
            {
                _skipped++;
                return (false, 0);
            }
            _count++;
            var skipped = _skipped;
            _skipped = 0;
            return (true, skipped);
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
