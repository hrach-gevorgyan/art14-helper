using System.Globalization;

namespace Art14Helper;

/// <summary>
/// Says in the log how each run of the tray app ended. A clean exit writes its reason. A run
/// that didn't get to (ended from Task Manager, a crash Windows ended at once, the laptop
/// losing power) leaves its marker file behind, and the next start says so: without it, the
/// log just stops and nobody can tell why.
/// </summary>
/// <remarks>
/// The marker is one small file in the app's own folder, written once at start and deleted
/// at exit. Copies with and without admin rights have different folders, so each keeps its own.
/// </remarks>
internal static class RunRecord
{
    private static string Marker => Path.Combine(AppFolders.Root, "running");

    /// <summary>At the tray app's start: reports an unclean end of the previous run, then marks this one.</summary>
    internal static void Started()
    {
        try
        {
            if (File.Exists(Marker))
                Log.Info(UncleanEndMessage(File.ReadAllText(Marker)));
            Directory.CreateDirectory(AppFolders.Root);
            File.WriteAllText(Marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Run record not written", exception);
        }
    }

    /// <summary>At a clean exit: the reason goes in the log and the marker away.</summary>
    internal static void Closed(string reason)
    {
        Log.Info($"Closing: {reason}");
        try
        {
            File.Delete(Marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Run record not removed", exception);
        }
    }

    internal static string UncleanEndMessage(string markerText)
    {
        var started = markerText.Trim() is { Length: > 0 } text ? $" (started {text})" : "";
        return $"The previous run{started} didn't close normally: ended from Task Manager, crashed, or the laptop lost power";
    }
}

/// <summary>
/// Removes what jobs left in the app's working folder: a driver download or backup cut short
/// by a crash or a restart can leave hundreds of megabytes there, and nothing else ever
/// would. Only entries older than a day go, so a job running right now is never touched.
/// </summary>
internal static class WorkCleanup
{
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

    /// <summary>Cleans the working folder of this process. Returns how many entries went.</summary>
    internal static int Run() => Clean(Path.Combine(AppFolders.Root, "Work"), DateTime.UtcNow, MinimumAge);

    internal static int Clean(string folder, DateTime nowUtc, TimeSpan minimumAge)
    {
        if (!Directory.Exists(folder))
            return 0;
        var removed = 0;
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (nowUtc - entry.LastWriteTimeUtc < minimumAge)
                continue;
            try
            {
                if (entry is DirectoryInfo directory)
                    directory.Delete(recursive: true);
                else
                    entry.Delete();
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use or locked: next start tries again.
            }
        }
        if (removed > 0)
            Log.Info($"Cleaned {removed} leftover item(s) from the working folder");
        return removed;
    }
}
