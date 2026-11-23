namespace Art14Helper;

/// <summary>How full the battery may charge; each limit is a start/stop pair kept by the EC.</summary>
internal enum ChargeLimit
{
    Off,
    Home,
    Office,
    Travel
}

/// <summary>Reads and writes the EC's charge window through the BIOS.</summary>
internal static class ChargeLimits
{
    private static readonly ulong ReadCommand = Bios.Command(0x03, 0x11);

    /// <summary>The window a limit stands for: charging starts below the first number and stops at the second.</summary>
    internal static (int Start, int End) Range(ChargeLimit limit) => limit switch
    {
        ChargeLimit.Home => (40, 70),
        ChargeLimit.Office => (70, 90),
        ChargeLimit.Travel => (95, 100),
        ChargeLimit.Off => (0, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(limit))
    };

    /// <summary>The limit a window read from the EC belongs to; null for a window this app didn't set.</summary>
    internal static ChargeLimit? FromRange(int? start, int? end)
    {
        foreach (var limit in Enum.GetValues<ChargeLimit>())
            if (Range(limit) == (start, end))
                return limit;
        return null;
    }

    /// <summary>The EC's current window, or nulls if the BIOS didn't answer.</summary>
    internal static (int? Start, int? End) Read()
        => Bios.TryRun(ReadCommand, out var reply) ? (Bios.Byte(reply, 1), Bios.Byte(reply, 2)) : (null, null);

    /// <summary>
    /// Writes a limit. Turning the limit off goes through an empty window first, with a
    /// second's pause, which is how the firmware accepts a full 0-100 window.
    /// </summary>
    internal static void Apply(ChargeLimit limit)
    {
        if (limit == ChargeLimit.Off)
        {
            Write(0, 0);
            Thread.Sleep(1000);
        }
        var (start, end) = Range(limit);
        Write(start, end);
    }

    private static void Write(int start, int end) => Bios.Run(Bios.Command(0x03, 0x10, (byte)start, (byte)end));

    /// <summary>
    /// After wake some firmware drops the window back to 0-100 a few seconds late; this
    /// checks three times, 3 s apart, and puts the chosen limit back. Only when elevated:
    /// asking for UAC each time the lid opens would be worse than losing the limit.
    /// </summary>
    internal static SettleWatch CreateWakeGuard() => new("Charge limit check after wake", 3, TimeSpan.FromSeconds(3), async () =>
    {
        if (!Privileged.IsElevated || AppSettings.ChargeOnce || AppSettings.ChosenCharge is not { } chosen || Read() == Range(chosen))
            return;
        Log.Info($"The EC lost the charge limit after wake; setting {chosen} again");
        if (await Privileged.ChargeLimit.RunAsync(chosen, AdminPrompt.Never))
            AppSettings.CurrentCharge = chosen;
    });
}

/// <summary>The charge limit choices for the tray menu and the settings window.</summary>
internal static class ChargeLimitMenu
{
    private static readonly (ChargeLimit Limit, string Text, string Hint)[] Choices =
    [
        (ChargeLimit.Home, "Home · 40–70%", "Best for a laptop that lives on the charger: the battery stays in its gentlest range."),
        (ChargeLimit.Office, "Office · 70–90%", "More charge for meetings away from the desk, still easier on the battery."),
        (ChargeLimit.Travel, "Travel · 95–100%", "Full battery for a day on the road."),
        (ChargeLimit.Off, "Off · always 100%", "No limit: the battery charges fully every time.")
    ];

    internal static void Build(IMenuBuilder menu)
    {
        var current = AppSettings.CurrentCharge;   // unknown (a window this app didn't set): nothing checked
        foreach (var (limit, text, hint) in Choices)
            menu.AddItem(text, async () => await ChooseAsync(limit), @checked: current == limit, tooltip: hint);

        // Usable only with a limit to come back to; shown either way, so it can be found.
        var once = AppSettings.ChargeOnce;
        var usable = once || AppSettings.ChosenCharge is ChargeLimit.Home or ChargeLimit.Office or ChargeLimit.Travel;
        menu.AddSeparator();
        menu.AddItem("Charge to 100% once", async () => await ToggleChargeOnceAsync(), @checked: once, toggle: true, enabled: usable,
            tooltip: usable
                ? "A full battery this one time, for a trip. The limit comes back by itself when you unplug."
                : "Pick Home, Office or Travel first: this charges fully once, then goes back to that limit.");
    }

    private static Task ChooseAsync(ChargeLimit limit)
        => Privileged.ChargeLimit.ApplyFromMenuAsync(limit, "The charge limit wasn't changed.", () =>
        {
            AppSettings.CurrentCharge = limit;
            AppSettings.ChosenCharge = limit;   // what the wake check restores
            AppSettings.ChargeOnce = false;
        });

    /// <summary>On: the EC charges to 100% (the stored choice stays). Off: the chosen limit again.</summary>
    private static Task ToggleChargeOnceAsync()
    {
        if (AppSettings.ChargeOnce)
            return ChooseAsync(AppSettings.ChosenCharge ?? ChargeLimit.Home);
        // No popup: the switch just flipped shows it, and the tooltip and Home page say "100% once".
        return Privileged.ChargeLimit.ApplyFromMenuAsync(ChargeLimit.Off, "Charging to 100% couldn't be turned on.",
            () =>
            {
                AppSettings.ChargeOnce = true;
                AppSettings.ChargeOnceSawCharger = PowerSource.IsPluggedIn;
            });
    }

    /// <summary>
    /// The charger was unplugged (or the app started on battery) after a one-time full
    /// charge: puts the chosen limit back, quietly. Without admin rights it waits for the next try.
    /// </summary>
    internal static async Task EndChargeOnceAsync()
    {
        if (!AppSettings.ChargeOnce || AppSettings.ChosenCharge is not { } chosen)
            return;
        if (!await Privileged.ChargeLimit.RunAsync(chosen, AdminPrompt.Never))
            return;
        AppSettings.CurrentCharge = chosen;
        AppSettings.ChargeOnce = false;
        AppSettings.ChargeOnceSawCharger = false;
        Log.Info($"Charge limit {chosen} back after the one-time full charge");
    }
}
