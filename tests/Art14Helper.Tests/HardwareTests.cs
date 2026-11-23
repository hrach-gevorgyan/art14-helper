using Xunit;

namespace Art14Helper.Tests;

/// <summary>BIOS command encoding, reply decoding and the privileged action table.</summary>
public class HardwareTests
{
    [Theory]
    [InlineData(0x02, 0x08, new byte[0], 0x0802UL)]                 // fan speed
    [InlineData(0x04, 0x07, new byte[] { 1 }, 0x010704UL)]           // Fn mode 1
    [InlineData(0x06, 0x35, new byte[] { 1 }, 0x013506UL)]           // brightness down
    [InlineData(0x03, 0x10, new byte[] { 40, 70 }, 0x46281003UL)]    // two arguments
    public void EncodesCommands(byte group, byte command, byte[] arguments, ulong expected)
        => Assert.Equal(expected, Bios.Command(group, command, arguments));

    [Fact]
    public void RejectsTooManyArguments()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Bios.Command(1, 2, [1, 2, 3, 4, 5, 6, 7]));

    [Fact]
    public void ReadsReplyFields()
    {
        byte[] reply = [0, 0x34, 0x08, 0x00, 0x09];
        Assert.Equal(0x0834, Bios.Word(reply, 1));
        Assert.Equal(0x0900, Bios.Word(reply, 3));
        Assert.Null(Bios.Word(reply, 4));
        Assert.Equal(0x09, Bios.Byte(reply, 4));
        Assert.Null(Bios.Byte(reply, 5));
    }

    [Fact]
    public void DecodesFanSpeedAndTarget()
    {
        Assert.Equal((2100, 2200), Sensors.DecodeFan([0, 0x34, 0x08, 0x98, 0x08]));
        // A torn read (EC mid-update) is dropped; the target still counts.
        Assert.Equal((null, 2200), Sensors.DecodeFan([0, 0xCD, 0x2F, 0x98, 0x08]));
        Assert.Equal((null, null), Sensors.DecodeFan([0]));
    }

    [Fact]
    public void NoAdminRightsMeansNoBiosAccess()
    {
        // The test runner isn't elevated: quiet calls report failure without throwing.
        Assert.SkipWhen(Privileged.IsElevated, "Runs only without admin rights.");
        Assert.False(Bios.TryRun(Bios.Command(0x02, 0x08)));
        Assert.Null(Sensors.Read());
        Assert.Throws<InvalidOperationException>(() => Bios.Run(Bios.Command(0x02, 0x08)));
    }
}
