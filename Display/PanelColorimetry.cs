namespace Art14Helper;

/// <summary>
/// Where the built-in panel's color points come from, best first:
/// 1. HONOR's factory measurement of this exact panel, if it's on the laptop
///    (only the numbers are read; the file itself is never copied);
/// 2. the panel's own description (EDID, through WMI), which every display has,
///    also right after a clean Windows install.
/// </summary>
internal static class PanelColorimetry
{
    // Read once (the panel doesn't change), and safely when startup and a menu ask at the same time.
    private static readonly Lazy<Chromaticities?> Points = new(Find);

    internal static Chromaticities? Read() => Points.Value;

    private static Chromaticities? Find()
    {
        var factory = FromFactoryFile();
        var edid = FromEdid();
        if (factory is not null)
            Log.Info("Color: " + factory);
        if (edid is not null)
            Log.Info("Color: " + edid);
        var points = factory ?? edid;
        if (points is null)
            Log.Info("Color: no panel color data found");
        return points;
    }

    private static Chromaticities? FromFactoryFile()
    {
        try
        {
            foreach (var file in Directory.GetFiles(ColorModeService.ColorDirectory, "*_display_factory_calibration_*.xml"))
                if (Chromaticities.FromFactoryCalibration(File.ReadAllText(file)) is { } measured)
                    return measured;
        }
        catch (Exception exception)
        {
            Log.Error("Color: could not read the factory measurement", exception);
        }
        return null;
    }

    /// <summary>
    /// Red, green, blue and white x/y as WMI gives them. EDID stores 10-bit fractions of
    /// 1024, but some drivers report only the top 8 bits (fractions of 256); real primaries
    /// in 10 bits always have a coordinate above 255 (green y, red x), so that tells them apart.
    /// </summary>
    internal static Chromaticities FromEdidValues(IReadOnlyList<double> raw)
    {
        var unit = raw.Max() > 255 ? 1024.0 : 256.0;
        (double, double) At(int i) => (raw[i] / unit, raw[i + 1] / unit);
        return new Chromaticities(At(0), At(2), At(4), At(6), "display data (EDID)");
    }

    /// <summary>Primaries from the panel's EDID (WmiMonitorColorCharacteristics).</summary>
    private static Chromaticities? FromEdid()
    {
        try
        {
            // \\?\DISPLAY#EDO4179#5&...#{guid} → DISPLAY\EDO4179\5&... (WMI instance names add "_0").
            var devicePath = DisplayPanel.FindBuiltIn()?.DevicePath;
            string? instancePrefix = null;
            if (devicePath is not null)
            {
                var parts = devicePath.TrimStart('\\', '?').Split('#');
                if (parts.Length >= 3)
                    instancePrefix = $"{parts[0]}\\{parts[1]}\\{parts[2]}";
            }

            // Every monitor's primaries, then the panel's by instance name. Without a name
            // the first monitor is taken, and a lone monitor is taken either way.
            var monitors = new List<(string Name, Chromaticities Values)>();
            Wmi.ForEach("SELECT InstanceName, Red, Green, Blue, DefaultWhite FROM WmiMonitorColorCharacteristics", row =>
            {
                var raw = new List<double>(8);
                foreach (var property in (string[])["Red", "Green", "Blue", "DefaultWhite"])
                {
                    var point = row.Inner(property);
                    raw.Add(point.Number("X"));
                    raw.Add(point.Number("Y"));
                }
                monitors.Add((row.Text("InstanceName"), FromEdidValues(raw)));
            }, @"root\wmi");

            var result = instancePrefix is null
                ? monitors.Select(m => m.Values).FirstOrDefault()
                : monitors.Where(m => m.Name.StartsWith(instancePrefix, StringComparison.OrdinalIgnoreCase)).Select(m => m.Values).FirstOrDefault();
            if (result is null && monitors.Count == 1)
                result = monitors[0].Values;
            if (result is null)
            {
                Log.Info($"Color: EDID: no matching display ({monitors.Count} in WMI, looking for {instancePrefix ?? "?"})");
                return null;
            }
            if (!result.IsValid())
                Log.Info("Color: EDID values not usable: " + result);
            return result.IsValid() ? result : null;
        }
        catch (Exception exception)
        {
            Log.Error("Color: could not read the display's color data", exception);
            return null;
        }
    }
}
