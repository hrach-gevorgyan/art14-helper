namespace Art14Helper;

/// <summary>One installed driver of a present device (what Win32_PnPSignedDriver would list).</summary>
internal sealed record InstalledDriver(
    string DeviceName,
    string HardwareId,
    string Version,
    string Provider,
    string DeviceClass)
{
    /// <summary>
    /// Whether the device name mentions <paramref name="keyword"/>. Short plain
    /// keywords must stand as a whole word: as a substring, "npu" is inside "Microsoft
    /// Input Configuration Device" and "nfc" or "gna" inside other names.
    /// </summary>
    internal bool NameMentions(string keyword)
    {
        var wholeWord = keyword.Length <= 4 && keyword.All(char.IsLetterOrDigit);
        var from = 0;
        while (from <= DeviceName.Length)
        {
            var at = DeviceName.IndexOf(keyword, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            if (!wholeWord)
                return true;
            var after = at + keyword.Length;
            var startsWord = at == 0 || !char.IsLetterOrDigit(DeviceName[at - 1]);
            var endsWord = after == DeviceName.Length || !char.IsLetterOrDigit(DeviceName[after]);
            if (startsWord && endsWord)
                return true;
            from = at + 1;
        }
        return false;
    }

    internal bool NameMentionsAny(params string[] keywords) => keywords.Any(NameMentions);

    internal bool IdContains(string text) => HardwareId.Contains(text, StringComparison.OrdinalIgnoreCase);

    internal bool IdStartsWith(string prefix) => HardwareId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    internal bool IsClass(string name) => DeviceClass.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A Windows inbox ("Microsoft", "Standard ...") or unnamed driver: present on any
    /// PC, so it says nothing about HONOR's package.
    /// </summary>
    internal bool IsInbox
        => string.IsNullOrWhiteSpace(Provider)
           || Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
           || Provider.Contains("Standard", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Works out which HONOR components are installed and in which version, from the
/// driver list Windows keeps. Also lists devices that have no driver at all.
/// </summary>
internal static class DeviceInventory
{
    internal const int BiosId = 23;

    /// <summary>Everything HONOR's catalogs know for this laptop family, by component id.</summary>
    internal static readonly IReadOnlyList<(int Id, string Name, string DisplayName, string? AppName, string Group)> Catalog =
    [
        (1, "Chipset", "Chipset", null, "Chipset"),
        (2, "ME", "Intel Management Engine", null, "Chipset"),
        (3, "Graphics", "Graphics", null, "Graphics"),
        (4, "SerialIO", "Serial IO", null, "Chipset"),
        (6, "DPTF", "Intel thermal framework", null, "Chipset"),
        (12, "WDT", "Watchdog", null, "Chipset"),
        (14, "Audio", "Audio", null, "Audio"),
        (15, "WiFi", "Wi-Fi", null, "Networking"),
        (16, "BT", "Bluetooth", null, "Bluetooth"),
        (18, "EgisFingerPrint", "Fingerprint", null, "Fingerprint"),
        (BiosId, "BIOS", "BIOS", null, "BIOS"),
        (41, "Monitor", "Monitor", null, "Monitor"),
        (52, "NFC", "NFC", null, "NFC"),
        (55, "PPM", "Intel PPM", null, "Chipset"),
        (56, "TXT", "Intel TXT", null, "Chipset"),
        (65, "ISST", "Intel Smart Sound", null, "Chipset"),
        (73, "iPMT", "Intel PMT", null, "Chipset"),
        (74, "NPU", "Intel NPU", null, "Chipset"),
        (76, "LuxvisionsCameraDrv", "Camera", "HonorCamera", "Camera"),
        (78, "MEP", "Windows Studio Effects", null, "Camera"),   // processes the camera picture
        (87, "VDisplay", "Virtual Display", null, "Graphics"),
        (88, "VHID", "Virtual HID", null, "Software")
    ];

    /// <summary>
    /// How each component is recognized. A rule has one or more tiers; the first tier
    /// that matches any driver decides, so a precise test can come before a broad one.
    /// </summary>
    private sealed record Rule(int Id, Func<InstalledDriver, bool>[] Tiers, Func<InstalledDriver, string>? PackageType = null);

    private static readonly Rule[] Rules =
    [
        new(1, [d => d.NameMentionsAny("lpc", "espi", "smbus", "chipset")]),
        new(2, [d => d.NameMentions("management engine interface")]),
        // Graphics: match the PCI vendor id and the display class; adapter names are
        // translated and vary by model. The Intel iGPU is tried first because HONOR's
        // catalog carries its driver.
        new(3, [d => IsGpu(d, "VEN_8086"), d => IsGpu(d, "VEN_1002"), d => IsGpu(d, "VEN_10DE")]),
        new(4, [d => d.NameMentions("serial io")]),
        new(6, [d => d.NameMentionsAny("innovation platform framework manager", "dynamic platform and thermal framework")]),
        new(12, [d => d.NameMentions("wdtdevice") || d.IdContains("WDT0001")]),
        new(14, [d => d.IdContains("FUNC_01") && d.NameMentions("audio")]),
        new(15, [d => d.IsClass("NET") && (d.IdStartsWith("PCI\\") || d.IdStartsWith("USB\\"))
                      && d.NameMentionsAny("wi-fi", "wifi", "wireless", "wlan")
                      && !d.NameMentions("virtual") && !d.IsInbox]),
        new(16, [d => (d.IsClass("BLUETOOTH") || d.NameMentions("bluetooth")) && !d.IsInbox]),
        new(18, [d => d.IsClass("BIOMETRIC") || d.NameMentions("fingerprint")], VidPid),
        new(41, [d => (d.IsClass("MONITOR") || d.IdStartsWith("DISPLAY\\") || d.IdStartsWith("MONITOR\\")) && !d.IsInbox]),
        new(52, [d => d.NameMentions("nfc") || d.IdContains("NTAG")]),
        new(55, [d => d.NameMentions("ppm provisioning")]),
        new(56, [d => d.NameMentions("txt authenticated")]),
        // Smart Sound endpoints have localized names; this part stays English.
        new(65, [d => d.NameMentions("smart sound")]),
        new(73, [d => d.NameMentions("platform monitoring technology")]),
        // Intel GNA (on older CPUs) sits in the same device class with a different
        // version line; it must not stand in for the NPU.
        new(74, [d => !d.NameMentionsAny("gaussian", "gna")
                      && (d.IsClass("COMPUTEACCELERATOR") || d.IsClass("NEURALPROCESSOR")
                          || d.NameMentionsAny("npu", "neural processor", "ai boost"))]),
        new(76, [d => (d.IsClass("CAMERA") || d.IsClass("IMAGE") || d.NameMentions("camera")) && !d.IsInbox]),
        new(78, [d => d.IdContains("MEP_") && d.NameMentionsAny("windows camera effects", "windows studio effects")]),
        new(87, [d => d.NameMentions("virtual display")]),
        new(88, [d => d.NameMentions("virtual hid")])
    ];

    private static bool IsGpu(InstalledDriver d, string vendor)
        => (d.IsClass("DISPLAY") || d.NameMentionsAny("graphics", "arc(tm)")) && d.IdContains(vendor) && !d.IsInbox;

    private static string VidPid(InstalledDriver d)
    {
        // "...VID_1C7A&PID_0587..." → "VID_1C7A&PID_0587"
        var at = d.HardwareId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + 17 > d.HardwareId.Length)
            return "";
        var candidate = d.HardwareId.Substring(at, 17).ToUpperInvariant();
        return candidate[8..13] == "&PID_" && candidate[4..8].All(char.IsAsciiHexDigit)
               && candidate[13..].All(char.IsAsciiHexDigit)
            ? candidate
            : "";
    }

    /// <summary>Catalog entry for an id, if HONOR's catalog has one.</summary>
    internal static (int Id, string Name, string DisplayName, string? AppName, string Group)? CatalogEntry(int id)
    {
        foreach (var entry in Catalog)
            if (entry.Id == id)
                return entry;
        return null;
    }

    /// <summary>The components recognized in a driver list (pure; no system access).</summary>
    internal static List<DriverComponent> Recognize(IReadOnlyCollection<InstalledDriver> drivers)
    {
        var found = new List<DriverComponent>();
        foreach (var rule in Rules)
        {
            var entry = CatalogEntry(rule.Id)!.Value;
            foreach (var tier in rule.Tiers)
            {
                var representative = Representative(drivers.Where(tier).ToList());
                if (representative is null)
                    continue;
                found.Add(new DriverComponent
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    DisplayName = entry.DisplayName,
                    AppName = entry.AppName,
                    CurrentVersion = representative.Version,
                    DeviceName = representative.DeviceName,
                    PackageType = rule.PackageType?.Invoke(representative) ?? ""
                });
                break;
            }
        }
        return found;
    }

    /// <summary>
    /// Picks the driver whose version represents the component. A package installs
    /// one version across all its devices, so the most frequent version is chosen;
    /// taking the maximum lets a stray device that happens to match a keyword win.
    /// Vendor drivers are preferred over Windows inbox ones.
    /// </summary>
    internal static InstalledDriver? Representative(IReadOnlyList<InstalledDriver> matches)
    {
        if (matches.Count == 0)
            return null;
        var pool = matches.Any(d => !d.IsInbox) ? matches.Where(d => !d.IsInbox).ToList() : matches;
        var votes = new Dictionary<string, List<InstalledDriver>>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in pool)
        {
            if (!votes.TryGetValue(driver.Version, out var list))
                votes[driver.Version] = list = [];
            list.Add(driver);
        }
        var winner = votes.Values
            .OrderByDescending(list => list.Count)
            .ThenByDescending(list => list[0].Version, DriverVersions.ByNumbers)
            .First();
        return winner.MinBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds a not-installed entry for every catalog component missing from the list.</summary>
    internal static List<DriverComponent> WithCatalogPlaceholders(IEnumerable<DriverComponent> components)
    {
        var list = components.ToList();
        var present = list.Select(c => c.Id).ToHashSet();
        list.AddRange(Catalog.Where(entry => !present.Contains(entry.Id)).Select(entry => new DriverComponent
        {
            Id = entry.Id,
            Name = entry.Name,
            DisplayName = entry.DisplayName,
            AppName = entry.AppName
        }));
        return list;
    }

    // ---- System access (WMI and registry), kept in this one place. ----

    /// <summary>Recognized components, the BIOS, and devices that have no driver.</summary>
    internal static List<DriverComponent> ReadFromSystem()
    {
        var devices = Devices.ListPresent();
        var components = Recognize(ReadSignedDrivers(devices));

        var bios = Wmi.FirstText("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
        if (!string.IsNullOrWhiteSpace(bios))
            components.Add(new DriverComponent
            {
                Id = BiosId,
                Name = "BIOS",
                DisplayName = "BIOS",
                CurrentVersion = bios,
                PackageType = Convert.ToString(Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", "")) ?? ""
            });

        components.AddRange(ReadDevicesWithoutDriver(devices));
        return components;
    }

    /// <summary>
    /// Present devices with an installed driver: the same rows as WMI's Win32_PnPSignedDriver
    /// (device description, instance id, version, provider, class), read from Configuration
    /// Manager in a few milliseconds instead of seconds.
    /// </summary>
    private static List<InstalledDriver> ReadSignedDrivers(List<DeviceNode> devices)
        => devices.Where(device => !string.IsNullOrWhiteSpace(device.DriverVersion))
            .Select(device => new InstalledDriver(device.Description ?? device.FriendlyName ?? "", device.InstanceId,
                device.DriverVersion!, device.DriverProvider ?? "", device.Class ?? ""))
            .ToList();

    /// <summary>Devices in the "driver not installed" state (problem code 28), ids -1, -2, ...</summary>
    private static IEnumerable<DriverComponent> ReadDevicesWithoutDriver(List<DeviceNode> devices)
    {
        const int DriverNotInstalled = 28;   // CM_PROB_FAILED_INSTALL
        return devices
            .Where(device => device.Problem == DriverNotInstalled)
            .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select((device, index) => new DriverComponent
            {
                Id = -1 - index,
                Name = "MissingDevice",
                DisplayName = string.IsNullOrWhiteSpace(device.DisplayName) ? "Unknown device" : device.DisplayName,
                PackageType = device.Class ?? "",
                DeviceName = device.InstanceId
            });
    }
}
