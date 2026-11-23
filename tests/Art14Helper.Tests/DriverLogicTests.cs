using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Version math, device recognition, HONOR answers and package handling.</summary>
public class DriverLogicTests
{
    private static DriverComponent Component(int id, string version = "0", string name = "X")
        => new() { Id = id, Name = name, DisplayName = name, CurrentVersion = version };

    // ---- Versions ----

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("31.0.101.5590", "31.0.101.6000", -1)]
    [InlineData("V1.2", "1.3", -1)]
    [InlineData("abc", "ABC", 0)]
    public void ComparesByDigitGroups(string left, string right, int expected)
        => Assert.Equal(expected, Math.Sign(DriverVersions.Compare(left, right)));

    [Theory]
    [InlineData("10.1.20.5", "10.1.19.1", true)]
    [InlineData("10.1.19.1", "10.1.20.5", false)]
    [InlineData("20260619", "10.18.26100.3", false)] // build date vs driver version
    [InlineData("2.5", "10.1.19627.8492", false)]     // different numbering schemes
    [InlineData("1.2.3", "0", false)]                 // not installed
    [InlineData("20260701", "20260619", true)]
    public void OnlyComparableNewerVersionsAreUpdates(string offered, string installed, bool expected)
        => Assert.Equal(expected, DriverVersions.IsNewer(offered, installed));

    [Theory]
    [InlineData("Chipset 10.1.56.27 for Win11 23.2", "10.1.56.27")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData("build 20260619", "20260619")]
    [InlineData("  beta ", "beta")]
    [InlineData("Driver 1.2.", "1.2")] // a trailing dot isn't part of it
    public void FindsVersionInSupportText(string text, string expected)
        => Assert.Equal(expected, DriverVersions.FromSupportText(text));

    [Fact]
    public void NormalizesDates()
    {
        Assert.Equal("2026-06-19", DriverVersions.IsoDate("2026-06-19 10:22:33"));
        Assert.Null(DriverVersions.IsoDate("soon"));
        Assert.Null(DriverVersions.IsoDate(null));
    }

    // ---- Device recognition ----

    private static InstalledDriver Driver(string name, string id = "PCI\\X", string version = "1.0",
        string provider = "Intel", string deviceClass = "SYSTEM")
        => new(name, id, version, provider, deviceClass);

    [Fact]
    public void ShortKeywordsMatchWholeWordsOnly()
    {
        Assert.False(Driver("Microsoft Input Configuration Device").NameMentions("npu"));
        Assert.True(Driver("Intel(R) AI Boost NPU").NameMentions("npu"));
        Assert.True(Driver("Intel(R) Wi-Fi 6E").NameMentions("wi-fi"));
    }

    [Fact]
    public void RepresentativeIsTheMostCommonVendorVersion()
    {
        var chosen = DeviceInventory.Representative(
        [
            Driver("Serial IO I2C", version: "30.100.1"),
            Driver("Serial IO GPIO", version: "30.100.1"),
            Driver("Serial IO UART", version: "31.0.0"),
            Driver("Serial IO SPI", version: "99.0", provider: "Microsoft")
        ]);
        Assert.Equal("30.100.1", chosen!.Version);
        Assert.Equal("Serial IO GPIO", chosen.DeviceName); // alphabetical among equals
    }

    [Fact]
    public void RecognizesComponentsFromDriverList()
    {
        var found = DeviceInventory.Recognize(
        [
            Driver("Intel(R) Arc(TM) Graphics", "PCI\\VEN_8086&DEV_7D51", "32.0.101.6000", deviceClass: "DISPLAY"),
            Driver("NVIDIA GeForce", "PCI\\VEN_10DE&DEV_1", "560.0", "NVIDIA", "DISPLAY"),
            Driver("Intel(R) Wi-Fi 6E AX211 160MHz", "PCI\\VEN_8086&DEV_51F1", "23.60.0.5", deviceClass: "NET"),
            Driver("Microsoft Input Configuration Device", "HID\\X", "10.0", "Microsoft"),
            Driver("Intel(R) Gaussian & Neural Accelerator", "PCI\\GNA", "3.0", deviceClass: "COMPUTEACCELERATOR"),
            Driver("Fingerprint Sensor", "USB\\VID_1c7a&PID_0587\\5", "4.0", "Egis", "BIOMETRIC")
        ]);
        Assert.Equal("32.0.101.6000", found.Single(c => c.Id == 3).CurrentVersion);   // Intel GPU first
        Assert.Equal("23.60.0.5", found.Single(c => c.Id == 15).CurrentVersion);
        Assert.DoesNotContain(found, c => c.Id == 74);                                // no NPU from GNA or "Input"
        Assert.Equal("VID_1C7A&PID_0587", found.Single(c => c.Id == 18).PackageType);
    }

    [Fact]
    public void CatalogPlaceholdersFillTheGaps()
    {
        var list = DeviceInventory.WithCatalogPlaceholders([Component(15, "1.0")]);
        Assert.Equal(DeviceInventory.Catalog.Count, list.Count);
        Assert.Equal("1.0", list.Single(c => c.Id == 15).CurrentVersion);
        Assert.False(list.Single(c => c.Id == 23).IsInstalled);
        Assert.Equal("HonorCamera", list.Single(c => c.Id == 76).AppName);
    }

    // ---- Product matching ----

    private static readonly MachineProfile ZhuqueC = new(
        "ZQC-P", "C233", "Intel(R) Core(TM) Ultra X7 358H", 32, ["ZQC-P", "HONOR MagicBook Pro 14"]);

    [Fact]
    public void ProcessorTokensAreModelNumbers()
    {
        Assert.Equal(["358H"], SupportProductMatcher.ProcessorTokens("Intel(R) Core(TM) Ultra X7 358H"));
        Assert.Equal(["13TH", "I71360P"], SupportProductMatcher.ProcessorTokens("13th Gen Intel(R) Core(TM) i7-1360P"));
        Assert.Empty(SupportProductMatcher.ProcessorTokens("Ultra X7 Gen12"));
    }

    [Fact]
    public void PicksTheProductThatNamesThisMachine()
    {
        var picked = SupportProductMatcher.Pick(
        [
            new("OFFE1", "MagicBook Pro 14 ZQC-P Ultra X7 358H 32GB"),
            new("OFFE2", "MagicBook Pro 14 ZQC-P Ultra 5 325H 16GB"),
            new("OFFE3", "MagicBook 16 JGC-N")
        ], ZhuqueC);
        Assert.Equal(["OFFE1"], picked);
    }

    [Fact]
    public void AmbiguousWeakMatchesPickNothing()
    {
        var machine = ZhuqueC with { Identifiers = ["MagicBook Pro"] };
        Assert.Empty(SupportProductMatcher.Pick([new("A", "MagicBook Pro 14"), new("B", "MagicBook Pro 16")], machine));
    }

    [Fact]
    public void KnownOfferingNeedsModelAndProcessor()
    {
        Assert.Equal(["OFFE00461151"], SupportProductMatcher.Known(ZhuqueC));
        Assert.Empty(SupportProductMatcher.Known(ZhuqueC with { Processor = "Intel(R) Core(TM) Ultra 5 325H" }));
        Assert.Empty(SupportProductMatcher.Known(ZhuqueC with { DeviceName = "JGC-N", Identifiers = ["JGC-N", "C233"] }));
    }

    // ---- Update platform ----

    [Fact]
    public void RequestCarriesComponentsAndRules()
    {
        var body = HonorUpdatePlatform.BuildRequest([Component(15, "23.60", "WiFi"), Component(23, "1.05", "BIOS")], ZhuqueC, "2.0.0");
        using var json = JsonDocument.Parse(body);
        var first = json.RootElement.GetProperty("components")[0];
        Assert.Equal("WiFi", first.GetProperty("AppName").GetString());
        Assert.Equal("15", first.GetProperty("componentID").GetString());
        Assert.Equal("23.60", first.GetProperty("PackageVersionCode").GetString());
        Assert.Equal("WiFi:23.60;BIOS:1.05;", json.RootElement.GetProperty("rules").GetProperty("Dashboard").GetString());
        Assert.Equal("C233", json.RootElement.GetProperty("rules").GetProperty("C_version").GetString());
    }

    [Fact]
    public void ParsesPlatformAnswer()
    {
        const string answer = """
            {"status":"0","components":[
              {"componentID":"15","version":"23.70.1","url":"http://update.platform.hihonorcloud.com/a/b","sha256":"AB","releaseDate":"2026-10-01"},
              {"componentID":"99","version":"1.0","url":"https://x"},
              {"componentID":"23","version":"1.10"}
            ]}
            """;
        using var json = JsonDocument.Parse(answer);
        var packages = HonorUpdatePlatform.ParseAnswer(json.RootElement, [Component(15, "23.60.0"), Component(23, "1.05")]);
        var wifi = Assert.Single(packages);
        Assert.True(wifi.IsUpdate);
        Assert.Equal("AB", wifi.Sha256);
        Assert.Equal("2026-10-01", wifi.ReleaseDate);
    }

    [Fact]
    public void FailedPlatformStatusHasNoPackages()
    {
        using var json = JsonDocument.Parse("""{"status":"1","components":[{"componentID":"15","version":"1","url":"u"}]}""");
        Assert.Empty(HonorUpdatePlatform.ParseAnswer(json.RootElement, [Component(15, "0.1")]));
    }

    // ---- Support site ----

    [Theory]
    [InlineData("HONOR Camera Driver", "", "", 76)]
    [InlineData("Intel ISST", "", "", 65)]
    [InlineData("Some chipset package", "631", "Chipset", 1)]
    [InlineData("BIOS 1.10", "6310", "BIOS", 23)]
    [InlineData("Unknown tool", "699", "Tools", 0)]
    public void MapsSupportPackagesToComponents(string title, string category, string categoryName, int expected)
        => Assert.Equal(expected, HonorSupportSite.ComponentIdFor(title, category, categoryName));

    [Fact]
    public void SyntheticIdsAreStableAndOutsideHonorRange()
    {
        var id = HonorSupportSite.SyntheticId("699|Unknown tool");
        Assert.Equal(id, HonorSupportSite.SyntheticId("699|UNKNOWN TOOL"));
        Assert.InRange(id, 1000, 1_000_999);
    }

    [Theory]
    [InlineData("Touchpad_1.2.3_Firmware", "1.2.3", "Touchpad")]
    [InlineData("Intel Audio 10.30", "10.30", "Intel Audio")]
    [InlineData("Bluetooth", "22.1", "Bluetooth")]
    public void CleansSupportTitles(string title, string version, string expected)
        => Assert.Equal(expected, HonorSupportSite.CleanTitle(title, version));

    [Fact]
    public void UnwrapsJsonp()
    {
        Assert.Equal("""{"a":(1)}""", HonorSupportSite.UnwrapJsonp("""callback({"a":(1)});"""));
        Assert.Throws<InvalidDataException>(() => HonorSupportSite.UnwrapJsonp("<html>"));
    }

    // ---- Merging and downloads ----

    [Fact]
    public void ComparablePackageBeatsBuildDate()
    {
        var installed = Component(14, "6.0.9500.1");
        DriverUpdate Offer(string version) => new()
        {
            Component = installed, Version = version, DownloadBaseUrl = "https://honor.com/" + version
        };
        Assert.Equal("6.0.9600.2", DriverUpdateService.BestOf([Offer("20260619"), Offer("6.0.9600.2"), Offer("6.0.9550.0")]).Version);
    }

    [Fact]
    public void CloudFolderUrlsPointAtTheFullPackage()
    {
        Assert.Equal("https://update.platform.hihonorcloud.com/x/y/full/update.zip",
            DownloadUrls.ForPackage("http://update.platform.hihonorcloud.com:80/x/y/").ToString());
        Assert.Equal("https://example.com/a", DownloadUrls.ForPackage("http://example.com/a").ToString());
    }

    [Fact]
    public void ReadsFileNameFromSupportDescriptor()
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes("""{"fileName":"dir/Chipset_10.1.exe"}"""));
        var token = Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("Chipset_10.1.exe", DownloadUrls.OriginalFileName(new Uri($"https://honor.com/dl/{token}.zip")));
        Assert.Equal("setup.exe", DownloadUrls.OriginalFileName(new Uri("https://honor.com/dl/setup.exe")));
        Assert.Null(DownloadUrls.OriginalFileName(new Uri("https://honor.com/dl/folder")));
    }

    [Theory]
    [InlineData("https://h/a/pkg.zip", ".zip", ".exe")]
    [InlineData("https://h/a/pkg", ".download", ".exe")]
    [InlineData("https://h/a/fw.bin", ".bin", ".bin")]
    public void ChoosesExtensions(string url, string download, string save)
    {
        Assert.Equal(download, DriverPackage.DownloadExtension(new Uri(url)));
        Assert.Equal(save, DriverPackage.SaveExtension(new Uri(url)));
    }

    [Fact]
    public void SafeFileNames()
    {
        Assert.Equal("a_b_c", FileNames.Safe("a/b:c"));
        Assert.Equal("HONOR-driver", FileNames.Safe("  "));
    }

    [Fact]
    public void UnpackingRefusesPathsOutsideTheFolder()
    {
        var folder = Directory.CreateTempSubdirectory("art14-zip-");
        try
        {
            var archive = Path.Combine(folder.FullName, "evil.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                zip.CreateEntry("../escaped.txt");
            Assert.Throws<InvalidDataException>(() => DriverPackage.UnpackInside(archive, Path.Combine(folder.FullName, "out")));
            Assert.False(File.Exists(Path.Combine(folder.FullName, "escaped.txt")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnpacksNestedArchives()
    {
        var folder = Directory.CreateTempSubdirectory("art14-zip-");
        try
        {
            var inner = Path.Combine(folder.FullName, "inner.zip");
            using (var zip = ZipFile.Open(inner, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("setup.exe").Open()))
                    writer.Write("MZ");
            var outer = Path.Combine(folder.FullName, "outer.zip");
            using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(inner, "drivers/inner.zip");

            var output = Path.Combine(folder.FullName, "out");
            DriverPackage.UnpackAll(outer, output);
            Assert.True(File.Exists(Path.Combine(output, "drivers", "inner", "setup.exe")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
