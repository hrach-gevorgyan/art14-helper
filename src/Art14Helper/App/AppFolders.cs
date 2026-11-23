namespace Art14Helper;

/// <summary>
/// Where the app writes its own files. With admin rights that's under the program folder in
/// Program Files, which only administrators can change: a program running without them
/// can't plant a junction or link there to turn an elevated write, rename or delete against
/// a system file. Without admin rights, the user's own local app data is fine.
/// </summary>
internal static class AppFolders
{
    private static readonly string UserFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Art14Helper");

    /// <summary>The app's own folder for this process.</summary>
    internal static string Root => Privileged.IsElevated ? Installer.InstallDirectory : UserFolder;

    /// <summary>The log's folder.</summary>
    internal static string Logs => Privileged.IsElevated ? Path.Combine(Installer.InstallDirectory, "Logs") : UserFolder;

    /// <summary>The per-user folder (logs of runs without admin rights, older versions' log); removed on uninstall.</summary>
    internal static string User => UserFolder;

    /// <summary>A new, empty working folder for one job (a download, a backup); the caller removes it.</summary>
    internal static string NewWorkFolder(string purpose)
        => Directory.CreateDirectory(Path.Combine(Root, "Work", $"{purpose}-{Guid.NewGuid():N}")).FullName;

    /// <summary>A file in the app's working folder, for a result handed between processes.</summary>
    internal static string WorkFile(string name)
        => Path.Combine(Directory.CreateDirectory(Path.Combine(Root, "Work")).FullName, name);
}
