using Xunit;

namespace Art14Helper.Tests;

/// <summary>Log flood guard and line cap, working folder cleanup, the run record and the BIOS retry.</summary>
public class ReliabilityTests
{
    [Fact]
    public void FloodGuardLetsALimitThroughAndReportsTheRest()
    {
        var guard = new Log.FloodGuard(limit: 3, windowMs: 1000);
        Assert.Equal((true, 0), guard.Admit(0));
        Assert.Equal((true, 0), guard.Admit(10));
        Assert.Equal((true, 0), guard.Admit(20));
        Assert.Equal((false, 0), guard.Admit(30));    // over the limit: held back
        Assert.Equal((false, 0), guard.Admit(40));
        Assert.Equal((true, 2), guard.Admit(1000));   // next window: written, with the count of the two held back
        Assert.Equal((true, 0), guard.Admit(1010));   // reported once only
    }

    [Fact]
    public void LongLinesAreCut()
    {
        Assert.Equal("short", Log.Shorten("short"));
        var cut = Log.Shorten(new string('x', Log.MaxLineLength + 500));
        Assert.Equal(Log.MaxLineLength, cut.Length);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public void CleanupRemovesOnlyOldLeftovers()
    {
        var folder = Directory.CreateTempSubdirectory("art14-work-").FullName;
        try
        {
            var now = DateTime.UtcNow;
            var oldFile = Path.Combine(folder, "task-old.xml");
            var newFile = Path.Combine(folder, "task-new.xml");
            var oldDir = Directory.CreateDirectory(Path.Combine(folder, "download-old")).FullName;
            File.WriteAllText(oldFile, "x");
            File.WriteAllText(newFile, "x");
            File.WriteAllText(Path.Combine(oldDir, "driver.exe"), "x");
            File.SetLastWriteTimeUtc(oldFile, now.AddDays(-2));
            Directory.SetLastWriteTimeUtc(oldDir, now.AddDays(-3));

            Assert.Equal(2, WorkCleanup.Clean(folder, now, WorkCleanup.MinimumAge));
            Assert.False(File.Exists(oldFile));
            Assert.False(Directory.Exists(oldDir));
            Assert.True(File.Exists(newFile));   // a job running now is never touched
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CleanupOfAMissingFolderDoesNothing()
        => Assert.Equal(0, WorkCleanup.Clean(Path.Combine(Path.GetTempPath(), "art14-none-" + Guid.NewGuid()), DateTime.UtcNow, WorkCleanup.MinimumAge));

    [Fact]
    public void UncleanEndIsExplained()
    {
        Assert.Contains("(started 2026-11-26 18:21:00)", RunRecord.UncleanEndMessage("2026-11-26 18:21:00\r\n"));
        Assert.Contains("Task Manager", RunRecord.UncleanEndMessage(""));
        Assert.DoesNotContain("(started", RunRecord.UncleanEndMessage(" "));
    }

    [Fact]
    public void MissingBiosIsLookedForAgainAfterAMinute()
    {
        Assert.True(Bios.ShouldSearch(null, 5_000));                                // never missing: always
        Assert.False(Bios.ShouldSearch(10_000, 10_000 + Bios.RetryMissingAfterMs - 1)); // just missed: wait
        Assert.True(Bios.ShouldSearch(10_000, 10_000 + Bios.RetryMissingAfterMs));       // a minute later: again
    }
}
