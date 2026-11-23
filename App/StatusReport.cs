using System.Globalization;
using System.Text;

namespace Art14Helper;

/// <summary>What the Home page of the settings window shows: light, limit, power, temperatures, fans.</summary>
internal sealed record StatusInfo(
    string KeyboardLight,
    string ChargeLimit,
    double? BatteryWatts,
    int? CpuTemperature,
    int? BatteryTemperature,
    int? Fan1Rpm,
    int? Fan2Rpm);

/// <summary>
/// The current state in words, for the tray tooltip and the Home page. Uses the last
/// sensor reading while it's fresh and the stored settings otherwise.
/// </summary>
internal static class StatusReport
{
    /// <summary>Windows cuts a tray tooltip at 127 characters.</summary>
    internal const int TooltipLimit = 127;

    // Battery power is read at most every 2 s: hovering the icon refreshes the tooltip often.
    private static (double? Watts, long At) _battery = (null, long.MinValue / 2);
    private static int _batteryReading;

    internal static StatusInfo ForHomePage()
    {
        var reading = Fresh();
        return new StatusInfo(
            LightWord(reading) is { } light ? char.ToUpperInvariant(light[0]) + light[1..] : "—",
            ChargeWord(reading) is { } charge ? char.ToUpperInvariant(charge[0]) + charge[1..] : "—",
            BatteryWatts(), reading?.CpuTemperature, reading?.BatteryTemperature, reading?.Fan1Rpm, reading?.Fan2Rpm);
    }

    internal static string ForTrayTooltip()
    {
        var reading = Fresh();
        var text = Tooltip(LightWord(reading), ChargeWord(reading), BatteryWatts(), reading);
        if (!KeepAwake.IsOn)
            return text;
        text = (RemoteMode.IsOn ? "Remote mode: awake, lid can close" : $"Awake {KeepAwake.Describe()}") + "\n" + text;
        return text.Length > TooltipLimit ? text[..TooltipLimit] : text;
    }

    /// <summary>
    /// The tooltip text: light, charge limit, battery power, temperatures and fans, one per
    /// line (single \n: every character counts), never longer than <see cref="TooltipLimit"/>.
    /// </summary>
    internal static string Tooltip(string? light, string? charge, double? watts, SensorReading? reading)
    {
        var text = new StringBuilder(TooltipLimit + 40);
        text.Append("Keyboard light: ").Append(light ?? "?");
        text.Append("\nCharge limit: ").Append(charge ?? "?");
        if (watts is { } power)
            text.Append("\nBattery: ").Append(PowerText(power));   // the battery's own flow, not the laptop's draw
        if (reading is not null)
        {
            if (reading.CpuTemperature is not null || reading.BatteryTemperature is not null)
                text.Append("\nCPU ").Append(Degrees(reading.CpuTemperature)).Append(", battery ").Append(Degrees(reading.BatteryTemperature));
            if (reading.Fan1Rpm is not null || reading.Fan2Rpm is not null)
                AppendFans(text, reading);
        }
        if (text.Length > TooltipLimit)
            text.Length = TooltipLimit;
        return text.ToString();
    }

    /// <summary>"+12.3 W" charging, "-7.0 W" draining, or "idle" when the charge limit holds it at 0.</summary>
    internal static string PowerText(double watts)
        => Math.Abs(watts) < 0.05 ? "0 W, idle" : (watts > 0 ? "+" : "") + watts.ToString("0.0", CultureInfo.CurrentCulture) + " W";

    private static string Degrees(int? celsius) => celsius is { } value ? $"{value}°C" : "?";

    /// <summary>
    /// Fans with the speed the firmware is steering them to ("2100→2400"). If that line
    /// doesn't fit, the targets go; if the speeds alone don't fit either, the line goes
    /// (a number cut in half would be a wrong number). A target of 0 means none reported.
    /// </summary>
    private static void AppendFans(StringBuilder text, SensorReading reading)
    {
        static string Fan(int? rpm, int? target) => (rpm?.ToString(CultureInfo.InvariantCulture) ?? "?") + (target is > 0 ? $"→{target}" : "");
        var withTargets = $"\nFans: {Fan(reading.Fan1Rpm, reading.Fan1TargetRpm)} / {Fan(reading.Fan2Rpm, reading.Fan2TargetRpm)} RPM";
        var plain = $"\nFans: {Fan(reading.Fan1Rpm, null)} / {Fan(reading.Fan2Rpm, null)} RPM";
        var hasTargets = reading.Fan1TargetRpm is > 0 || reading.Fan2TargetRpm is > 0;
        foreach (var line in hasTargets ? new[] { withTargets, plain } : [plain])
        {
            if (text.Length + line.Length <= TooltipLimit)
            {
                text.Append(line);
                return;
            }
        }
    }

    private static SensorReading? Fresh() => Sensors.Latest is { IsFresh: true } reading ? reading : null;

    private static string? LightWord(SensorReading? reading) => (reading?.Light ?? AppSettings.CurrentLight) switch
    {
        KeyboardLight.Off => "off",
        KeyboardLight.Dim => "dim",
        KeyboardLight.Bright => "bright",
        KeyboardLight.Auto => "auto",
        _ => null
    };

    /// <summary>The window the EC reports when a reading has it, else the stored limit's window.</summary>
    internal static string? ChargeWord(SensorReading? reading)
    {
        if (AppSettings.ChargeOnce)
            return "100% once";
        if (reading is { ChargeStart: { } start, ChargeEnd: { } end })
            return ChargeLimits.FromRange(start, end) == ChargeLimit.Off ? "off" : $"{start}–{end}%";
        if (AppSettings.CurrentCharge is not { } limit)
            return null;
        if (limit == ChargeLimit.Off)
            return "off";
        var (low, high) = ChargeLimits.Range(limit);
        return $"{low}–{high}%";
    }

    /// <summary>
    /// Battery charge (+) or drain (-) in watts from WMI's BatteryStatus (no admin rights
    /// needed). Returns the last value at once; when it's older than two seconds a fresh
    /// read starts in the background, so the tooltip and the settings window never wait
    /// for WMI. Null without a battery (or before the first read finishes).
    /// </summary>
    private static double? BatteryWatts()
    {
        var now = Environment.TickCount64;
        var last = _battery;
        if (now - last.At >= 2000 && Interlocked.Exchange(ref _batteryReading, 1) == 0)
            _ = Task.Run(() =>
            {
                try
                {
                    _battery = (ReadBatteryWatts(), Environment.TickCount64);
                }
                finally
                {
                    Interlocked.Exchange(ref _batteryReading, 0);
                }
            });
        return last.Watts;
    }

    private static double? ReadBatteryWatts()
    {
        double? watts = null;
        try
        {
            Wmi.ForEach("SELECT ChargeRate, DischargeRate FROM BatteryStatus", row =>
            {
                if (watts is null && long.TryParse(row.Text("ChargeRate"), out var charging) && long.TryParse(row.Text("DischargeRate"), out var draining))
                    watts = (charging - draining) / 1000.0;   // milliwatts
            }, @"root\wmi");
        }
        catch (WmiException exception)
        {
            Log.Error("Battery power unreadable", exception);
        }
        return watts;
    }
}
