using System.Security;
using System.Security.Principal;
using System.Xml.Linq;

namespace Art14Helper;

/// <summary>
/// Start at sign-in through a Task Scheduler task. The hardware settings need admin
/// rights, and only a task set to the highest run level can provide them at sign-in with
/// no UAC question (a Run key entry can't). The task is managed with schtasks.exe and a task XML
/// (creating or changing it needs admin rights; reading doesn't).
/// </summary>
internal static class Autostart
{
    internal const string TaskName = "Art14 Helper";
    private static readonly XNamespace TaskSchema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>True when the sign-in task exists.</summary>
    internal static bool IsEnabled => ReadTask() is not null;

    /// <summary>On or off. "On" points the task at the installed copy in Program Files. Needs admin rights.</summary>
    internal static void SetEnabled(bool on)
    {
        if (on)
            RegisterFor(Installer.InstalledExe);
        else
            Remove();
    }

    /// <summary>
    /// Creates the task for <paramref name="exe"/>, or replaces it. Needs admin rights. Only the
    /// installed copy: an elevated sign-in task pointing at a folder the user can write to would
    /// let any program there run with admin rights at every sign-in.
    /// </summary>
    internal static void RegisterFor(string exe)
    {
        if (!string.Equals(Path.GetFullPath(exe), Installer.InstalledExe, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
            throw new InvalidOperationException("Start with Windows works for the installed app only. Install it first.");

        // schtasks reads the definition from a file (UTF-16). It goes in the program folder, where
        // only administrators can write, and stays open read-only, without write or delete sharing,
        // until schtasks has read it, so nothing can swap it in between. (schtasks refuses a file
        // that is open for writing, so the writing handle is closed first.)
        var xmlFile = AppFolders.WorkFile($"task-{Guid.NewGuid():N}.xml");
        try
        {
            using (var writer = new FileStream(xmlFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                writer.Write(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(TaskXml(exe, WindowsIdentity.GetCurrent().Name))).ToArray());
            using (new FileStream(xmlFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var created = SystemTool.Run("schtasks.exe", "/Create", "/TN", TaskName, "/XML", xmlFile, "/F");
                if (created.ExitCode != 0)
                    throw new InvalidOperationException($"Task Scheduler refused the sign-in task: {created.Tail()}");
            }
        }
        finally
        {
            File.Delete(xmlFile);
        }
    }

    /// <summary>Deletes the task; nothing to do when there is none.</summary>
    internal static void Remove()
    {
        if (!IsEnabled)
            return;
        var deleted = SystemTool.Run("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
        if (deleted.ExitCode != 0)
            throw new InvalidOperationException($"Task Scheduler didn't delete the sign-in task: {deleted.Tail()}");
    }

    /// <summary>
    /// Starts the app through the task now (elevated, no UAC prompt), but only if the task
    /// launches <paramref name="exe"/>. False when there's no such task or it didn't start.
    /// </summary>
    internal static bool TryStartNow(string exe)
    {
        if (ReadTask() is not { } task || !SamePath(LaunchedProgram(task), exe))
            return false;
        return SystemTool.Run("schtasks.exe", "/Run", "/TN", TaskName).ExitCode == 0;
    }

    /// <summary>
    /// The task definition: at sign-in of <paramref name="user"/>, run <paramref name="exe"/>
    /// with the highest rights, also on battery, without a time limit (the default stops a
    /// task after three days), at most one copy, restarted up to three times if it crashes.
    /// </summary>
    internal static string TaskXml(string exe, string user)
    {
        XElement Node(string name, params object[] content) => new(TaskSchema + name, content);
        var task = Node("Task", new XAttribute("version", "1.2"),
            Node("RegistrationInfo", Node("Description", $"Starts {AppInfo.Name} at sign-in")),
            Node("Triggers", Node("LogonTrigger", Node("Enabled", "true"), Node("UserId", user))),
            Node("Principals", Node("Principal", new XAttribute("id", "Author"),
                Node("UserId", user), Node("LogonType", "InteractiveToken"), Node("RunLevel", "HighestAvailable"))),
            Node("Settings",
                Node("MultipleInstancesPolicy", "IgnoreNew"),
                Node("DisallowStartIfOnBatteries", "false"),
                Node("StopIfGoingOnBatteries", "false"),
                Node("ExecutionTimeLimit", "PT0S"),
                Node("RestartOnFailure", Node("Interval", "PT1M"), Node("Count", "3")),
                Node("Enabled", "true")),
            Node("Actions", new XAttribute("Context", "Author"),
                Node("Exec", Node("Command", exe), Node("WorkingDirectory", Path.GetDirectoryName(exe) ?? ""))));
        // XDocument.ToString leaves the declaration out; schtasks wants it.
        return new XDeclaration("1.0", "UTF-16", null) + Environment.NewLine + task;
    }

    /// <summary>The program a task definition runs, or null.</summary>
    internal static string? LaunchedProgram(XDocument task)
        => task.Descendants(TaskSchema + "Exec").Elements(TaskSchema + "Command").FirstOrDefault()?.Value.Trim('"', ' ');

    /// <summary>
    /// The registered task's definition, or null when there's none. Elevated, the stored
    /// definition is read straight from the Tasks folder; otherwise schtasks prints it.
    /// </summary>
    private static XDocument? ReadTask()
    {
        try
        {
            var stored = Path.Combine(Environment.SystemDirectory, "Tasks", TaskName);
            if (Privileged.IsElevated)
                return File.Exists(stored) ? XDocument.Load(stored) : null;
            var query = SystemTool.Run("schtasks.exe", "/Query", "/TN", TaskName, "/XML");
            return query.ExitCode == 0 ? XDocument.Parse(query.Output) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or System.Xml.XmlException)
        {
            Log.Error("Sign-in task unreadable", exception);
            return null;
        }
    }

    private static bool SamePath(string? a, string b)
        => a is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
