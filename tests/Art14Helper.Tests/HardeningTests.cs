using Xunit;
using Art14Helper.Native;

namespace Art14Helper.Tests;

/// <summary>The checks that keep the elevated app from trusting what a user could plant.</summary>
public class HardeningTests
{
    [Theory]
    [InlineData("Auto", "Auto")]
    [InlineData(" bright ", "Bright")]
    public void ChoicesParseByName(string text, string expected)
    {
        Assert.True(EnumText.TryParse<KeyboardLight>(text, out var value));
        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Glow")]
    [InlineData("Auto, Dim")]
    public void NumbersAndUnknownNamesAreRefused(string text)
        => Assert.False(EnumText.TryParse<KeyboardLight>(text, out _));

    [Fact]
    public void NumbersNeverReachTheBios()
        => Assert.False(Privileged.ParseLightState("7,30", out _));

    [Theory]
    [InlineData("Honor Device Co., Ltd.")]
    [InlineData("HONOR DEVICE CO.,LTD")]
    [InlineData("Huawei Technologies Co., Ltd.")]
    public void HonorSignersAreAccepted(string organization) => Assert.True(Authenticode.IsHonorSigner(organization));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not Honor Device Co., Ltd.")]
    [InlineData("Honor Device Co., Ltd. Fan Club")]
    [InlineData("Microsoft Corporation")]
    public void OtherSignersAreRefused(string? organization) => Assert.False(Authenticode.IsHonorSigner(organization));

    [Fact]
    public void WindowsFilesCountAsSystemOwned()
        => Assert.True(ColorModeService.IsOwnedBySystem(Path.Combine(Environment.SystemDirectory, "kernel32.dll")));

    [Fact]
    public void AUsersFileIsNotTrusted()
    {
        if (Privileged.IsElevated)
            return;   // an administrator's new file is owned by Administrators
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(ColorModeService.IsOwnedBySystem(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void WorkFoldersAreNewEachTime()
    {
        var first = AppFolders.NewWorkFolder("test");
        var second = AppFolders.NewWorkFolder("test");
        try
        {
            Assert.NotEqual(first, second);
            Assert.Empty(Directory.EnumerateFileSystemEntries(first));
        }
        finally
        {
            Directory.Delete(first);
            Directory.Delete(second);
        }
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\x.zip")]
    [InlineData(@"C:\Program Files\Art14Helper\backup.zip")]
    [InlineData(@"C:\Users\Someone\Documents\drivers.sys")]
    public void DriverBackupsGoOnlyToAZipInTheUsersFolders(string path)
        => Assert.NotNull(DriverBackup.UnsafeBackupTarget(path));

    [Fact]
    public void ADriverBackupInDocumentsIsFine()
        => Assert.Null(DriverBackup.UnsafeBackupTarget(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Drivers.zip")));

    [Theory]
    [InlineData(7)]
    [InlineData(65535)]
    public void OnlyOfferedKeyboardTimeoutsReachTheBios(int seconds)
        // Refused before any BIOS call, so this is safe anywhere.
        => Assert.Throws<ArgumentOutOfRangeException>(() => KeyboardLights.SetIdleTimeout((ushort)seconds));
}
