using Xunit;

namespace Art14Helper.Tests;

/// <summary>Keep awake's menu and wording, and what the diagnostics report may contain.</summary>
public class AwakeAndDiagnosticsTests
{
    [Fact]
    public void KeepAwakeIsOffUntilStarted()
    {
        Assert.False(KeepAwake.IsOn);
        Assert.Equal("off", KeepAwake.Describe());
    }

    [Fact]
    public void KeepAwakeMenuOffersDurationsAndOptions()
    {
        var menu = new MenuModel();
        KeepAwakeMenu.Build(menu);
        var items = menu.Nodes.Where(node => node.Kind == MenuNodeKind.Item).Select(node => node.Text).ToList();
        Assert.Equal(["Stay awake", "Off", "30 minutes", "1 hour", "2 hours", "4 hours", "Until turned off",
            "While awake", "Keep the screen on", "Stop apps showing you as away",
            "Away from the laptop", "Remote mode",
            "When the lid closes", "Keep running on the charger", "Keep running on battery"], items);
        Assert.True(menu.Nodes.First(node => node.Text == "Off").Checked);
        // Off: what only matters while awake is greyed out. (The stored choices are the user's own: not checked here.)
        Assert.False(menu.Nodes.First(node => node.Text == "Keep the screen on").Enabled);
        Assert.False(menu.Nodes.First(node => node.Text == "Stop apps showing you as away").Enabled);
    }

    [Fact]
    public void ProgramPathsShowOnlyTheirName()
    {
        Assert.Equal("obs64.exe", Diagnostics.Setting("Keys.F10Command", @"C:\Users\someone\Apps\obs64.exe"));
        Assert.Equal("Home", Diagnostics.Setting("Charge.Chosen", "Home"));
        Assert.Equal("30", Diagnostics.Setting("Light.IdleSeconds", 30));
    }

    [Fact]
    public void TheUserFolderIsHidden()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(@"%USERPROFILE%\AppData\Local\Art14Helper", Diagnostics.Private(profile + @"\AppData\Local\Art14Helper"));
    }

    [Fact]
    public void TheReportCarriesNoAccountName()
    {
        var report = Diagnostics.Build();
        Assert.Contains("diagnostics", report);
        Assert.Contains("Settings:", report);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), report, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Remote mode's page and what a settings backup leaves out.</summary>
public class RemoteModeTests
{
    [Fact]
    public void RemoteModeIsASwitch()
    {
        var menu = new MenuModel();
        KeepAwakeMenu.Build(menu.AddSubMenu("Keep awake", "Keeps the laptop from sleeping."));
        var pages = new SettingsPages(_ => { }, _ => { }, () => true, () => { },
            new DriversPage(() => Task.FromResult<IReadOnlyList<DriverComponent>>([])));
        var page = pages.From(menu).Single(page => page.Title == "Keep awake");
        var rows = page.Build().SelectMany(section => section.Rows).ToList();
        Assert.Equal(RowKind.Toggle, rows.Single(row => row.Text == "Remote mode").Kind);
        Assert.Equal(RowKind.Choice, rows.Single(row => row.Text == "1 hour").Kind);
    }

    [Fact]
    public void BackupsLeaveOutLidAndRemoteState()
    {
        var text = SettingsBackup.Write(new Dictionary<string, object>
        {
            ["Awake.Remote"] = 1,
            ["Awake.RemoteLidCharger"] = 1,
            ["Lid.BeforeCharger"] = 1,
            ["Awake.Screen"] = 0
        });
        Assert.Equal(["Awake.Screen"], SettingsBackup.Read(text)!.Keys);
    }
}

/// <summary>Keep awake goes on after a restart of the app (an update, a crash) with the time that's left.</summary>
public class KeepAwakeResumeTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UntilTurnedOffResumes() => Assert.Equal(DateTime.MaxValue, KeepAwake.ResumeUntil("until turned off", Now));

    [Fact]
    public void AnEndStillAheadResumesAtThatTime()
    {
        var end = Now.AddHours(3);
        Assert.Equal(end.ToLocalTime(), KeepAwake.ResumeUntil(end.ToString("o"), Now));
    }

    [Theory]
    [InlineData("2026-11-23T12:59:00.0000000Z")]   // ran out while the app wasn't running
    [InlineData(null)]                             // wasn't on
    [InlineData("tomorrow")]                       // unreadable
    public void NothingToResume(string? stored) => Assert.Null(KeepAwake.ResumeUntil(stored, Now));
}
