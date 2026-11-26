using Xunit;

namespace Art14Helper.Tests;

/// <summary>The optional update notice: GitHub's answer, version comparison and the weekly rhythm.</summary>
public class UpdateCheckTests
{
    private const string Page = "https://github.com/hrach-gevorgyan/art14-helper/releases/tag/v1.2.0";

    [Fact]
    public void ReadsTheLatestRelease()
    {
        var release = UpdateCheck.Parse($$"""{"tag_name":"v1.2.0","html_url":"{{Page}}","draft":false,"prerelease":false}""");
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal(Page, release.Page);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.2.0","draft":true}""")]
    [InlineData("""{"tag_name":"v1.2.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void IgnoresAnythingButAPublishedVersion(string json) => Assert.Null(UpdateCheck.Parse(json));

    [Fact]
    public void OpensOnlyThisRepositorysPages()
    {
        var release = UpdateCheck.Parse("""{"tag_name":"v1.2.0","html_url":"https://example.com/evil"}""");
        Assert.Equal(UpdateCheck.ReleasesPage, release!.Page);
    }

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.1.1", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("2.0", "1.9.9", true)]       // a tag without the third number
    [InlineData("1.1.0", "not a version", false)]
    public void ComparesVersions(string available, string running, bool newer)
        => Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(available), running));

    [Fact]
    public void ChecksAtMostWeekly()
    {
        var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateCheck.IsDue(null, now));                    // never checked
        Assert.False(UpdateCheck.IsDue(now.AddDays(-6), now));
        Assert.True(UpdateCheck.IsDue(now.AddDays(-7), now));
        Assert.True(UpdateCheck.IsDue(now.AddDays(3), now));          // clock went backwards: don't wait for ever
    }
}
