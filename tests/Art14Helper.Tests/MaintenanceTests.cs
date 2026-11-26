using System.Xml.Linq;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Sign-in task, driver backup report, status text and log lines.</summary>
public class MaintenanceTests
{
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void TaskXmlRunsTheExeElevatedAtSignIn()
    {
        var text = Autostart.TaskXml(@"C:\Program Files\Art14Helper\Art14Helper.exe", @"LAPTOP\user");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", text);
        var task = XDocument.Parse(text);
        string Value(string name) => task.Descendants(Task + name).Single().Value;

        Assert.Equal(@"C:\Program Files\Art14Helper\Art14Helper.exe", Autostart.LaunchedProgram(task));
        Assert.Equal(@"C:\Program Files\Art14Helper", Value("WorkingDirectory"));
        Assert.Equal("HighestAvailable", Value("RunLevel"));
        Assert.Equal("InteractiveToken", Value("LogonType"));
        Assert.Equal("false", Value("DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value("StopIfGoingOnBatteries"));
        Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
        Assert.Equal("IgnoreNew", Value("MultipleInstancesPolicy"));
        Assert.All(task.Descendants(Task + "UserId"), user => Assert.Equal(@"LAPTOP\user", user.Value));
    }

    [Fact]
    public void AdministratorAccountIsSeenEvenWhenFiltered()
    {
        // whoami lists Administrators both when enabled (elevated) and as "deny only" (filtered by UAC).
        var whoami = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "whoami.exe"), "/groups /fo csv")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(whoami)!;
        var groups = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(groups.Contains("S-1-5-32-544", StringComparison.Ordinal), Installer.IsAdministratorAccount());
    }

    [Fact]
    public void FindsTheLauncherThatWaitsForTheElevatedCopy()
    {
        Assert.Equal(4242, Installer.WaitingLauncher([@"C:\x\Art14Helper.exe", "--install", "--silent", "--waiting-pid=4242"]));
        Assert.Null(Installer.WaitingLauncher([@"C:\x\Art14Helper.exe", "--install", "--silent"]));
        Assert.Null(Installer.WaitingLauncher(["x", "--waiting-pid=abc"]));
        Assert.Null(Installer.WaitingLauncher(["x", "--waiting-pid=-5"]));
    }

    [Fact]
    public void ReadsTheProgramOfAQuotedCommand()
    {
        var task = XDocument.Parse($"<Task xmlns=\"{Task}\"><Actions><Exec><Command>\"C:\\A b\\x.exe\"</Command></Exec></Actions></Task>");
        Assert.Equal(@"C:\A b\x.exe", Autostart.LaunchedProgram(task));
    }

    [Theory]
    [InlineData(true, 42, true, null)]
    [InlineData(false, 0, false, "There are no driver files (.inf) in it.")]
    public void BackupReportRoundTrips(bool done, int packages, bool restart, string? problem)
    {
        var result = new DriverBackupResult(done, packages, restart, problem);
        Assert.Equal(result, DriverBackup.FromJson(DriverBackup.ToJson(result)));
    }

    [Fact]
    public void CopiesOnlyTheFactoryCalibration()
    {
        var root = Directory.CreateTempSubdirectory("art14-color-");
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root.FullName, "from")).FullName;
            var to = Directory.CreateDirectory(Path.Combine(root.FullName, "to")).FullName;
            File.WriteAllText(Path.Combine(from, "EDO4179_display_factory_calibration_srgb.xml"), "<x/>");
            File.WriteAllText(Path.Combine(from, "Art14 Helper sRGB.icc"), "profile");
            Assert.Equal(1, DriverBackup.CopyColorCalibration(from, to, fromColorFolder: false));
            Assert.Equal(["EDO4179_display_factory_calibration_srgb.xml"], Directory.GetFiles(to).Select(Path.GetFileName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(12.34, "+12.3 W")]
    [InlineData(-7.0, "-7.0 W")]
    [InlineData(0.01, "0 W, idle")]
    public void DescribesBatteryPower(double watts, string expected)
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(expected, StatusReport.PowerText(watts));
    }

    [Fact]
    public void TooltipStaysWithinWindowsLimit()
    {
        var busy = new SensorReading(DateTime.UtcNow, 7999, 7999, 7999, 7999, 105, 55, KeyboardLight.Bright, 95, 100);
        var text = StatusReport.Tooltip("bright", "95–100%", -123.4, busy);
        Assert.True(text.Length <= StatusReport.TooltipLimit, $"{text.Length} characters");
        Assert.StartsWith("Keyboard light: bright\nCharge limit: 95–100%", text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void TooltipDropsFanTargetsBeforeCuttingNumbers()
    {
        var reading = new SensorReading(DateTime.UtcNow, 2100, 2200, 2400, 2500, 48, 31, null, null, null);
        Assert.EndsWith("Fans: 2100→2400 / 2200→2500 RPM", StatusReport.Tooltip("auto", "40–70%", 1.5, reading));
        var noTargets = reading with { Fan1TargetRpm = 0, Fan2TargetRpm = 0 };
        Assert.EndsWith("Fans: 2100 / 2200 RPM", StatusReport.Tooltip("auto", "40–70%", 1.5, noTargets));
    }

    [Fact]
    public void LogLinesHaveTimeLevelAndMessage()
    {
        var line = Log.Format(new DateTime(2026, 11, 23, 9, 5, 7, 42), "info ", "Started", null);
        Assert.Equal("2026-11-23 09:05:07.042 info  Started", line);
        Assert.Contains(" — System.InvalidOperationException: broken",
            Log.Format(DateTime.Now, "error", "Failed", new InvalidOperationException("broken")));
    }

    [Fact]
    public void ToolOutputTailKeepsTheLastLines()
    {
        var outcome = new SystemTool.Outcome(5, "first\r\nsecond\n\nthird\nfourth\n");
        Assert.Equal("second third fourth", outcome.Tail());
    }

    [Fact]
    public void EveryCatalogEntryHasAGroup()
        => Assert.All(DeviceInventory.Catalog, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Group)));

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _before = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _before;
    }
}
