using System.Diagnostics;

namespace Art14Helper;

internal sealed record SettingsSection(string? Title, List<RowSpec> Rows);

internal sealed record SettingsPage(string Title, string Glyph, string? Description, Func<List<SettingsSection>> Build);

/// <summary>
/// What the settings window shows, built from the same menu as the tray (so the two never
/// disagree): one page per submenu, plus Home, Drivers and System. Holds no window state;
/// <paramref name="run"/> performs a click and <paramref name="open"/> switches pages.
/// </summary>
internal sealed class SettingsPages(Action<Action> run, Action<string> open, Func<bool> startsWithWindows, Action toggleStartup, DriversPage drivers)
{
    internal const string Home = "Home";
    internal const string Drivers = "Drivers";
    private const string System = "System";

    private MenuModel _menu = new();

    internal List<SettingsPage> From(MenuModel menu)
    {
        _menu = menu;
        var pages = new List<SettingsPage> { new(Home, "", null, () => [new SettingsSection(null, [Info(Tip(), "")])]) };
        foreach (var node in menu.Nodes.Where(node => node is { Kind: MenuNodeKind.SubMenu, Children: not null }))
        {
            var children = node.Children!.Nodes;
            pages.Add(new SettingsPage(node.Text, GlyphFor(node.Text), node.Tooltip,
                node.Text == "Charge limit" ? () => [.. Sections(children), BatterySection()] : () => Sections(children)));
        }
        pages.Add(new SettingsPage(Drivers, "", "BIOS and driver updates from HONOR, and a driver backup.",
            () => drivers.Sections().Select(section => new SettingsSection(section.Title, section.Rows)).ToList()));
        pages.Add(new SettingsPage(System, "", "Touchscreen, startup and app info.", SystemSections));
        return pages;
    }

    private static string GlyphFor(string page) => page switch
    {
        "Charge limit" => "",
        "Keyboard" => "",
        "Fn lock" => "",
        "F10 / F11 keys" => "",
        "Touchpad" => "",
        "Sound" => "",
        "Display" => "",
        "Power mode" => "",
        "Keep awake" => "",
        _ => ""
    };

    private static RowSpec Info(string text, string glyph, string? secondary = null)
        => new() { Text = text, Secondary = secondary, Glyph = glyph, Kind = RowKind.Info };

    /// <summary>
    /// A menu as cards: a separator or a section title starts a new card, a submenu becomes
    /// its own titled card, and a title with nothing under it stays as a note.
    /// </summary>
    private List<SettingsSection> Sections(IReadOnlyList<MenuNode> nodes, string? firstTitle = null)
    {
        var sections = new List<SettingsSection>();
        var title = firstTitle;
        string? note = null;
        var items = new List<MenuNode>();

        void Close()
        {
            if (items.Count > 0)
                sections.Add(new SettingsSection(title, items.Count == 1 && items[0] is { Toggle: false, Action: not null } single
                    ? [Row(single, RowKind.Action)]   // a lone command, not one option of a group
                    : items.Select(item => Row(item)).ToList()));
            else if (title is not null)
                sections.Add(new SettingsSection(null, [Info(title, "", note)]));
            (title, note, items) = (null, null, []);
        }

        foreach (var node in nodes)
        {
            if (node.Kind == MenuNodeKind.Separator)
                Close();
            else if (node is { Kind: MenuNodeKind.SubMenu, Children: { } children })
            {
                Close();
                sections.AddRange(Sections(children.Nodes, node.Text));
            }
            else if (node.IsHeader)
            {
                Close();
                (title, note) = (node.Text, node.Tooltip);
            }
            else
                items.Add(node);
        }
        Close();
        return sections;
    }

    // On a page, a clickable item that isn't a switch picks one option of its group.
    private RowSpec Row(MenuNode item, RowKind? kind = null) => new()
    {
        Text = item.Text,
        Secondary = item.Tooltip,
        Kind = kind ?? (item.Toggle ? RowKind.Toggle : item.Action is null ? RowKind.Info : RowKind.Choice),
        Checked = item.Checked,
        Enabled = item.Enabled,
        OnClick = item.Action is { } action ? () => run(action) : null
    };

    private List<SettingsSection> SystemSections()
    {
        var sections = new List<SettingsSection>();
        var device = _menu.Nodes.Where(node => node.Kind == MenuNodeKind.Item && !node.IsHeader && node.Text != Drivers).ToList();
        if (device.Count > 0)
            sections.Add(new SettingsSection("Device", device.Select(node => new RowSpec
            {
                Text = node.Text,
                Secondary = node.Tooltip,
                Kind = node.Toggle ? RowKind.Toggle : RowKind.Action,
                Checked = node.Checked,
                Enabled = node.Enabled,
                OnClick = node.Action is { } action ? () => run(action) : null
            }).ToList()));

        sections.Add(new SettingsSection("App",
        [
            new RowSpec
            {
                Text = "Start with Windows",
                Secondary = "Opens in the tray when you sign in, with admin rights and no UAC prompt.",
                Glyph = "",
                Kind = RowKind.Toggle,
                Checked = startsWithWindows(),
                OnClick = () => run(toggleStartup)
            },
            new RowSpec { Text = "Back up settings", Secondary = "Saves every choice to a small file, for a Windows reinstall.", Glyph = "", Kind = RowKind.Action, OnClick = () => run(SettingsBackup.Export) },
            new RowSpec { Text = "Restore settings", Secondary = "Reads such a file and applies its choices to the laptop.", Glyph = "", Kind = RowKind.Action, OnClick = () => run(SettingsBackup.Import) },
            new RowSpec { Text = "Open log", Secondary = "A record of what the app did, for finding problems.", Glyph = "", Kind = RowKind.Action, OnClick = OpenLog },
            new RowSpec
            {
                Text = "Copy diagnostics",
                Secondary = "Versions, laptop, what works, settings and the end of the log, for a bug report. No serial number; nothing is sent.",
                Glyph = "",
                Kind = RowKind.Action,
                OnClick = Diagnostics.Copy
            },
            new RowSpec { Text = "Version", Glyph = "", Value = AppInfo.Version, Kind = RowKind.Info }
        ]));
        sections.Add(new SettingsSection("About", [Info("Unofficial software", "", AppInfo.Disclaimer), Info("Credits", "", AppInfo.Credits), Info("License", "", AppInfo.License)]));
        sections.Add(new SettingsSection(null,
        [
            new RowSpec
            {
                Text = "Uninstall",
                Secondary = "Removes the app with its autostart and settings. You confirm first.",
                Glyph = "",
                Kind = RowKind.Action,
                OnClick = StartUninstall
            }
        ]));
        return sections;
    }

    // ---- Home ----

    /// <summary>The groups of a menu page, split at separators and section titles; only clickable items.</summary>
    private static List<(string? Title, List<MenuNode> Items)> Groups(IReadOnlyList<MenuNode> nodes)
    {
        var groups = new List<(string?, List<MenuNode>)>();
        string? title = null;
        var items = new List<MenuNode>();
        foreach (var node in nodes)
        {
            if (node.Kind == MenuNodeKind.Separator || node.IsHeader)
            {
                if (items.Count > 0)
                    groups.Add((title, items));
                (title, items) = (node.IsHeader ? node.Text : null, []);
            }
            else if (node is { Kind: MenuNodeKind.Item, Action: not null })
                items.Add(node);
        }
        if (items.Count > 0)
            groups.Add((title, items));
        return groups;
    }

    private List<MenuNode>? Group(string page, string? title)
    {
        var node = _menu.Nodes.FirstOrDefault(node => node.Kind == MenuNodeKind.SubMenu && node.Text == page);
        if (node?.Children is null)
            return null;
        var groups = Groups(node.Children.Nodes);
        return (title is null ? groups.FirstOrDefault() : groups.FirstOrDefault(group => group.Title == title)).Items;
    }

    /// <summary>"Home · 40–70%" → "Home"; "Auto: max on charger…" → "Auto".</summary>
    internal static string ShortName(string text)
    {
        var cut = text.IndexOfAny(['(', ':', '·']);
        return (cut > 0 ? text[..cut] : text).Trim();
    }

    internal List<QuickSetting> QuickSettings()
    {
        var tiles = new List<QuickSetting>();

        // value: what the tile shows when the checked item's name isn't the best summary.
        void Add(string page, string? group, string glyph, string title, string hint, string? value = null)
        {
            if (Group(page, group) is not { } choices)
                return;
            var current = choices.FirstOrDefault(choice => choice.Checked);
            // Two options flip on a click; more open the page.
            var flips = choices.Count == 2 && current is not null;
            Action click = flips ? () => run(choices.First(choice => !choice.Checked).Action!) : () => open(page);
            tiles.Add(new QuickSetting(glyph, title, value ?? (current is null ? "—" : ShortName(current.Text)), flips, click, hint));
        }

        Add("Keep awake", null, "", "Keep awake", "Opens Keep awake.",
            // When it ends says more than the duration picked.
            value: RemoteMode.IsOn ? "Remote mode" : KeepAwake.Until is { } until ? (until == DateTime.MaxValue ? "On" : $"Until {until:t}") : null);
        Add("Keyboard", "Brightness", "", "Keyboard light", "Opens Keyboard. Fn+Space changes it too.");
        Add("Display", "Refresh rate", "", "Refresh rate", "Opens Display.");
        Add("Display", "Color mode", "", "Color mode", "Opens Display.");
        Add("Power mode", null, "", "Power mode", "Opens Power mode.");
        Add("Fn lock", null, "", "Top row keys", "Click to flip between F-keys and media keys.");
        Add("Sound", null, "", "Sound", "Click to flip between spatial and stereo.");

        if (_menu.Nodes.FirstOrDefault(node => node is { Kind: MenuNodeKind.Item, Toggle: true, Text: "Touchscreen" }) is { Action: { } toggle } touch)
            tiles.Add(new QuickSetting("", "Touchscreen", touch.Checked ? "On" : "Off", true, () => run(toggle),
                "Click to turn the touchscreen on or off."));
        return tiles;
    }

    private static (string Name, string Details)? _laptop;

    private static (string Name, string Details) Laptop()
    {
        if (_laptop is { } known)
            return known;
        const string Bios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
        string? Read(string name) => Convert.ToString(Microsoft.Win32.Registry.GetValue(Bios, name, null))?.Trim() is { Length: > 0 } text ? text : null;
        var product = Read("SystemProductName");
        var version = Read("BIOSVersion");
        var details = string.Join(" · ", new[] { product, version is null ? null : $"BIOS {version}" }.OfType<string>());
        return (_laptop = (Read("SystemFamily") ?? product ?? "Laptop", details)).Value;
    }

    internal static DeviceState Device()
    {
        var (name, details) = Laptop();
        var status = StatusReport.ForHomePage();
        var battery = PowerSource.Battery;
        var line = battery switch
        {
            { Charging: true } => "Charging",
            { PluggedIn: true } => "Plugged in, not charging",
            { TimeLeft: { TotalHours: >= 1 } left } => $"On battery · {(int)left.TotalHours} h {left.Minutes} min left",
            { TimeLeft: { } left } => $"On battery · {left.Minutes} min left",
            _ => "On battery"
        };
        if (status.BatteryWatts is { } watts && Math.Abs(watts) >= 0.05)
            line += $" · {Math.Abs(watts):0.0} W";
        var limit = AppSettings.ChargeOnce ? "Charging to 100% once, then the limit" : status.ChargeLimit switch
        {
            "Off" => "No charge limit: charges to 100%",
            "—" => "Charge limit not read yet",
            var range => $"Charge limit {range}"
        };
        return new DeviceState(name, details, battery.Percent, line, limit, battery.Charging);
    }

    internal static List<StatusTile> Hardware()
    {
        var status = StatusReport.ForHomePage();
        var fans = (status.Fan1Rpm, status.Fan2Rpm) switch
        {
            ({ } first, { } second) when first > 0 || second > 0 => $"{first} / {second} rpm",
            (0, _) => "Off",
            ({ } first, _) => $"{first} rpm",
            _ => "—"
        };
        return
        [
            new StatusTile("", "Processor", status.CpuTemperature is { } cpu ? $"{cpu}°C" : "—"),
            new StatusTile("", "Battery", status.BatteryTemperature is { } battery ? $"{battery}°C" : "—"),
            new StatusTile("", "Fans", fans),
            .. Health() is { } health
                ? [new StatusTile("", "Battery health", health.Shown, HealthDetail(health))]
                : Array.Empty<StatusTile>()
        ];
    }

    /// <summary>Under the charge limits: the battery's health and Windows' detailed battery report.</summary>
    private static SettingsSection BatterySection()
    {
        var rows = new List<RowSpec>();
        if (Health() is { } health)
            rows.Add(new RowSpec
            {
                Text = "Battery health",
                Secondary = HealthDetail(health),
                Value = health.Shown,
                Glyph = "",
                Kind = RowKind.Info
            });
        rows.Add(new RowSpec
        {
            Text = "Battery report",
            Secondary = "Windows' detailed history: capacity over time, recent use and battery life, in the browser.",
            Glyph = "",
            Kind = RowKind.Action,
            OnClick = BatteryReport.Open
        });
        return new SettingsSection("Battery", rows);
    }

    internal static string HealthDetail(BatteryHealth health)
        => $"{health.FullMilliwattHours / 1000.0:0.0} Wh at full charge of {health.DesignedMilliwattHours / 1000.0:0.0} Wh designed"
           + (health.Cycles is { } cycles ? $"; {cycles} charge cycles" : "");

    private static (BatteryHealth? Health, long At) _health = (null, long.MinValue / 2);

    /// <summary>Battery health from its driver; it changes over weeks, so it's read at most every 10 minutes.</summary>
    private static BatteryHealth? Health()
    {
        var now = Environment.TickCount64;
        if (now - _health.At >= 600_000)
            _health = (Battery.ReadHealth(), now);
        return _health.Health;
    }

    /// <summary>One line on Home: advice when the current state calls for it, otherwise a tip that changes daily.</summary>
    private string Tip()
    {
        if (StatusReport.ForHomePage().ChargeLimit == "Off")
            return "The charge limit is off. If the laptop is plugged in most of the day, Home (40–70%) is kinder to the battery.";
        var rate = Group("Display", "Refresh rate")?.FirstOrDefault(choice => choice.Checked)?.Text;
        if (!PowerSource.IsPluggedIn && rate is not null && rate != "60 Hz" && !rate.StartsWith("Auto", StringComparison.Ordinal))
            return "You're on battery at the highest refresh rate. Auto drops to 60 Hz on battery and saves power.";
        string[] tips =
        [
            "Left-click the tray icon for this window, right-click it for the quick menu.",
            "Going to reinstall Windows? Back up your drivers on the Drivers page first.",
            "On Auto, the keyboard light follows the room's light, smoothly and at no extra battery cost.",
            "sRGB shows websites and photos as intended; P3 is wider, for photo and video work.",
            "Knock twice on the touchpad with a knuckle to save a screenshot to Pictures\\Screenshots."
        ];
        return tips[DateTime.Now.DayOfYear % tips.Length];
    }

    private static void OpenLog()
    {
        if (File.Exists(Log.Location))
            Shell.Reveal(Log.Location);
    }

    private static void StartUninstall()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Error("Uninstaller not started", exception);
        }
    }
}
