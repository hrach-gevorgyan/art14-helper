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
    // The three pages of the settings window's own, by their English names (their identity);
    // the window shows them through T(). Marked here so the tables have them.
    internal static readonly string[] OwnPageTitles = [N("Drivers"), N("System")];

    /// <summary>
    /// A page's title as shown. Home has a context: "Home" alone is the charge limit preset,
    /// which other languages name differently from the start page.
    /// </summary>
    internal static string Shown(string title) => title == Home ? T("Home§page") : T(title);

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
                node.Text == T("Charge limit") ? () => [.. Sections(children), BatterySection()] : () => Sections(children)));
        }
        pages.Add(new SettingsPage(Drivers, "", T("BIOS and driver updates from HONOR, and a driver backup."),
            () => drivers.Sections().Select(section => new SettingsSection(section.Title, section.Rows)).ToList()));
        pages.Add(new SettingsPage(System, "", T("Touchscreen, startup and app info."), SystemSections));
        return pages;
    }

    private static readonly string[] PageNames = ["Charge limit", "Keyboard", "Fn lock", "F10 / F11 keys", "Touchpad", "Sound", "Display", "Power mode", "Keep awake"];

    private static string GlyphFor(string page) => GlyphForEnglish(PageNames.FirstOrDefault(name => T(name) == page) ?? page);

    private static string GlyphForEnglish(string page) => page switch
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
        var device = _menu.Nodes.Where(node => node.Kind == MenuNodeKind.Item && !node.IsHeader && node.Text != T(Drivers)).ToList();
        if (device.Count > 0)
            sections.Add(new SettingsSection(T("Device"), device.Select(node => new RowSpec
            {
                Text = node.Text,
                Secondary = node.Tooltip,
                Kind = node.Toggle ? RowKind.Toggle : RowKind.Action,
                Checked = node.Checked,
                Enabled = node.Enabled,
                OnClick = node.Action is { } action ? () => run(action) : null
            }).ToList()));

        sections.Add(new SettingsSection(T("Language"), LanguageRows()));

        sections.Add(new SettingsSection(T("App"),
        [
            new RowSpec
            {
                Text = T("Start with Windows"),
                Secondary = T("Opens in the tray when you sign in, with admin rights and no UAC prompt."),
                Glyph = "",
                Kind = RowKind.Toggle,
                Checked = startsWithWindows(),
                OnClick = () => run(toggleStartup)
            },
            new RowSpec { Text = T("Back up settings"), Secondary = T("Saves every choice to a small file, for a Windows reinstall."), Glyph = "", Kind = RowKind.Action, OnClick = () => run(SettingsBackup.Export) },
            new RowSpec { Text = T("Restore settings"), Secondary = T("Reads such a file and applies its choices to the laptop."), Glyph = "", Kind = RowKind.Action, OnClick = () => run(SettingsBackup.Import) },
            new RowSpec { Text = T("Open log"), Secondary = T("A record of what the app did, for finding problems."), Glyph = "", Kind = RowKind.Action, OnClick = OpenLog },
            new RowSpec
            {
                Text = T("Copy diagnostics"),
                Secondary = T("Versions, laptop, what works, settings and the end of the log, for a bug report. No serial number; nothing is sent."),
                Glyph = "",
                Kind = RowKind.Action,
                OnClick = Diagnostics.Copy
            },
            new RowSpec
            {
                Text = T("Tell me about new versions"),
                Secondary = T("Once a week, asks GitHub whether a newer version is out. Sends only the app's version and downloads nothing."),
                Glyph = "",
                Kind = RowKind.Toggle,
                Checked = AppSettings.UpdateCheck,
                OnClick = () => run(UpdateCheck.Toggle)
            },
            .. UpdateCheck.Available is { } release
                ? new[] { new RowSpec { Text = T("Version {0} is out", release.Version.ToString(3)), Secondary = T("Opens the release page on GitHub: download the new exe there and run it to update."), Glyph = "", Kind = RowKind.Action, OnClick = UpdateCheck.OpenPage } }
                : [],
            new RowSpec { Text = T("Version"), Glyph = "", Value = AppInfo.Version, Kind = RowKind.Info }
        ]));
        sections.Add(new SettingsSection(T("About"), [Info(T("Unofficial software"), "", AppInfo.Disclaimer), Info(T("Credits"), "", AppInfo.Credits), Info(T("License"), "", AppInfo.License)]));
        sections.Add(new SettingsSection(null,
        [
            new RowSpec
            {
                Text = T("Uninstall"),
                Secondary = T("Removes the app with its autostart and settings. You confirm first."),
                Glyph = "",
                Kind = RowKind.Action,
                OnClick = StartUninstall
            }
        ]));
        return sections;
    }

    /// <summary>
    /// The language choice. Each language is named in itself (never translated), so it can be
    /// found whatever language the app shows now. A choice applies at once.
    /// </summary>
    private List<RowSpec> LanguageRows()
    {
        var chosen = AppSettings.Language;
        RowSpec Choice(AppLanguage language, string name, string? secondary = null) => new()
        {
            Text = name,
            Secondary = secondary,
            Kind = RowKind.Choice,
            Checked = chosen == language,
            OnClick = () => run(() =>
            {
                AppSettings.Language = language;
                L10n.Reload();
            })
        };
        return
        [
            Choice(AppLanguage.Auto, T("Same as Windows"), T("Russian or Simplified Chinese when Windows uses it, otherwise English.")),
            Choice(AppLanguage.English, "English"),
            Choice(AppLanguage.Russian, "Русский"),
            Choice(AppLanguage.ChineseSimplified, "简体中文")
        ];
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
        var cut = text.IndexOfAny(['(', ':', '·', '（', '：']);   // full-width forms for Chinese
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

        Add(T("Keep awake"), null, "", T("Keep awake"), T("Opens Keep awake."),
            // When it ends says more than the duration picked.
            value: RemoteMode.IsOn ? T("Remote mode") : KeepAwake.Until is { } until ? (until == DateTime.MaxValue ? T("On") : T("Until {0:t}", until)) : null);
        Add(T("Keyboard"), T("Brightness"), "", T("Keyboard light"), T("Opens Keyboard. Fn+Space changes it too."));
        Add(T("Display"), T("Refresh rate"), "", T("Refresh rate"), T("Opens Display."));
        Add(T("Display"), T("Color mode"), "", T("Color mode"), T("Opens Display."));
        Add(T("Power mode"), null, "", T("Power mode"), T("Opens Power mode."));
        Add(T("Fn lock"), null, "", T("Top row keys"), T("Click to flip between F-keys and media keys."));
        Add(T("Sound"), null, "", T("Sound"), T("Click to flip between spatial and stereo."));

        if (_menu.Nodes.FirstOrDefault(node => node is { Kind: MenuNodeKind.Item, Toggle: true, Text: var touchText } && touchText == T("Touchscreen")) is { Action: { } toggle } touch)
            tiles.Add(new QuickSetting("", T("Touchscreen"), touch.Checked ? T("On") : T("Off"), true, () => run(toggle),
                T("Click to turn the touchscreen on or off.")));
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
        return (_laptop = (Read("SystemFamily") ?? product ?? N("Laptop"), details)).Value;
    }

    internal static DeviceState Device()
    {
        var (name, details) = Laptop();
        name = T(name);   // only the fallback "Laptop" has a translation; it's cached in English
        var status = StatusReport.ForHomePage();
        var battery = PowerSource.Battery;
        var line = battery switch
        {
            { Charging: true } => T("Charging"),
            { PluggedIn: true } => T("Plugged in, not charging"),
            { TimeLeft: { TotalHours: >= 1 } left } => T("On battery · {0} h {1} min left", (int)left.TotalHours, left.Minutes),
            { TimeLeft: { } left } => T("On battery · {0} min left", left.Minutes),
            _ => T("On battery")
        };
        if (status.BatteryWatts is { } watts && Math.Abs(watts) >= 0.05)
            line += " · " + T("{0:0.0} W", Math.Abs(watts));
        var limit = AppSettings.ChargeOnce ? T("Charging to 100% once, then the limit") : status.ChargeLimitOff ? T("No charge limit: charges to 100%") : status.ChargeLimit switch
        {
            "—" => T("Charge limit not read yet"),
            var range => T("Charge limit {0}", range)
        };
        return new DeviceState(name, details, battery.Percent, line, limit, battery.Charging);
    }

    internal static List<StatusTile> Hardware()
    {
        var status = StatusReport.ForHomePage();
        var fans = (status.Fan1Rpm, status.Fan2Rpm) switch
        {
            ({ } first, { } second) when first > 0 || second > 0 => T("{0} / {1} rpm", first, second),
            (0, _) => T("Off"),
            ({ } first, _) => T("{0} rpm", first),
            _ => "—"
        };
        return
        [
            new StatusTile("", T("Processor"), status.CpuTemperature is { } cpu ? $"{cpu}°C" : "—"),
            new StatusTile("", T("Battery"), status.BatteryTemperature is { } battery ? $"{battery}°C" : "—"),
            new StatusTile("", T("Fans"), fans),
            .. Health() is { } health
                ? [new StatusTile("", T("Battery health§tile"), health.Shown, HealthDetail(health))]
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
                Text = T("Battery health"),
                Secondary = HealthDetail(health),
                Value = health.Shown,
                Glyph = "",
                Kind = RowKind.Info
            });
        rows.Add(new RowSpec
        {
            Text = T("Battery report"),
            Secondary = T("Windows' detailed history: capacity over time, recent use and battery life, in the browser."),
            Glyph = "",
            Kind = RowKind.Action,
            OnClick = BatteryReport.Open
        });
        return new SettingsSection(T("Battery"), rows);
    }

    internal static string HealthDetail(BatteryHealth health)
        => T("{0:0.0} Wh at full charge of {1:0.0} Wh designed", health.FullMilliwattHours / 1000.0, health.DesignedMilliwattHours / 1000.0)
           + (health.Cycles is { } cycles ? T("; {0} charge cycles", cycles) : "");

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
        if (StatusReport.ForHomePage().ChargeLimitOff)
            return T("The charge limit is off. If the laptop is plugged in most of the day, Home (40–70%) is kinder to the battery.");
        if (!PowerSource.IsPluggedIn && !RefreshRate.IsAuto && RefreshRate.Highest > 60 && RefreshRate.Current == RefreshRate.Highest)
            return T("You're on battery at the highest refresh rate. Auto drops to 60 Hz on battery and saves power.");
        string[] tips =
        [
            T("Left-click the tray icon for this window, right-click it for the quick menu."),
            T("Going to reinstall Windows? Back up your drivers on the Drivers page first."),
            T("On Auto, the keyboard light turns on in the dark and off in bright light, by itself."),
            T("sRGB shows websites and photos as intended; P3 is wider, for photo and video work."),
            T("Knock twice on the touchpad with a knuckle to save a screenshot to Pictures\\Screenshots.")
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
