using Xunit;

namespace Art14Helper.Tests;

/// <summary>Driver downloads: only HTTPS, and only real HONOR domains count as official.</summary>
public class DownloadSafetyTests
{
    [Theory]
    [InlineData("honor.com")]
    [InlineData("www.honor.com")]
    [InlineData("selfservice-eu.honor.com")]
    [InlineData("UPDATE.Platform.HiHonorCloud.com")]
    public void RealHonorHostsMatch(string host)
        => Assert.True(DownloadUrls.IsHostIn(host, host.Contains("cloud", StringComparison.OrdinalIgnoreCase) ? "hihonorcloud.com" : "honor.com"));

    [Theory]
    [InlineData("evilhonor.com")]
    [InlineData("honor.com.evil.net")]
    [InlineData("nothonor.com")]
    public void LookAlikeHostsDoNotMatch(string host)
        => Assert.False(DownloadUrls.IsHostIn(host, "honor.com"));

    [Fact]
    public void DownloadUrlIsAlwaysHttps()
    {
        var url = DownloadUrls.ForPackage("http://update.platform.hihonorcloud.com/x/pkg.zip");
        Assert.Equal("https", url.Scheme);
    }
}
