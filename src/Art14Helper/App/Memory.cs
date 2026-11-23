namespace Art14Helper;

/// <summary>Keeps the resident app small between the moments it's busy.</summary>
internal static class Memory
{
    /// <summary>
    /// One full, compacting collection that also hands free memory back to Windows. Worth it
    /// after a burst (startup, a closed settings window with its driver list); the GC alone
    /// keeps that memory reserved for the next burst, which for a tray app may never come.
    /// </summary>
    internal static void Trim()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        catch (Exception exception)
        {
            Log.Error("Memory trim failed", exception);
        }
    }
}
