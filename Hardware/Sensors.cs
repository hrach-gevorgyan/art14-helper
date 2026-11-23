namespace Art14Helper;

/// <summary>One reading of the laptop's sensors and of the state the firmware reports.</summary>
internal sealed record SensorReading(
    DateTime TakenAt,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? Fan1TargetRpm,
    int? Fan2TargetRpm,
    int? CpuTemperature,
    int? BatteryTemperature,
    KeyboardLight? Light,
    int? ChargeStart,
    int? ChargeEnd)
{
    /// <summary>Recent enough to show instead of the stored settings.</summary>
    internal bool IsFresh => DateTime.UtcNow - TakenAt < TimeSpan.FromSeconds(30);
}

/// <summary>
/// Reads fans, temperatures, keyboard light mode and charge limits from the BIOS.
/// Needs admin rights; without them there is no reading (hovering the tray icon must
/// never bring up a UAC prompt).
/// </summary>
internal static class Sensors
{
    private static readonly ulong FanCommand = Bios.Command(0x02, 0x08);
    private static readonly ulong TemperatureCommand = Bios.Command(0x02, 0x02);
    private const byte CpuZone = 0x00, BatteryZone = 0x0E;

    /// <summary>
    /// Highest believable fan speed. A read can catch the EC halfway through writing
    /// the two bytes and produce a huge number; such readings are treated as unknown.
    /// </summary>
    internal const int MaxPlausibleRpm = 8000;

    /// <summary>The last reading, or null before the first one.</summary>
    internal static SensorReading? Latest { get; private set; }

    /// <summary>
    /// Takes a reading, keeps it as <see cref="Latest"/> and copies the firmware's
    /// light mode and charge limits into the stored settings (the firmware can change
    /// them by itself, e.g. Fn+Space or a reset). Null without admin rights.
    /// </summary>
    internal static SensorReading? Read()
    {
        if (!Privileged.IsElevated)
            return null;

        var (fan1, target1) = Fan(0);
        var (fan2, target2) = Fan(1);
        var (chargeStart, chargeEnd) = ChargeLimits.Read();
        var reading = new SensorReading(DateTime.UtcNow, fan1, fan2, target1, target2,
            Temperature(CpuZone), Temperature(BatteryZone), KeyboardLights.Effective(KeyboardLights.ReadMode()), chargeStart, chargeEnd);
        Latest = reading;

        if (reading.Light is { } light)
            AppSettings.CurrentLight = light;
        if (ChargeLimits.FromRange(chargeStart, chargeEnd) is { } limit)
            AppSettings.CurrentCharge = limit;
        return reading;
    }

    /// <summary>Fan reply: actual speed in bytes 1-2, the firmware's target in bytes 3-4.</summary>
    internal static (int? Actual, int? Target) DecodeFan(byte[] reply)
        => (Bios.Word(reply, 1) is { } rpm && rpm <= MaxPlausibleRpm ? rpm : null, Bios.Word(reply, 3));

    private static (int? Actual, int? Target) Fan(byte index)
        => Bios.TryRun(FanCommand | (ulong)index << 16, out var reply) ? DecodeFan(reply) : (null, null);

    /// <summary>Temperature reply: degrees Celsius in byte 2.</summary>
    private static int? Temperature(byte zone)
        => Bios.TryRun(TemperatureCommand | (ulong)zone << 16, out var reply) ? Bios.Byte(reply, 2) : null;
}
