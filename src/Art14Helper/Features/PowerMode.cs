using System.Runtime.InteropServices;

namespace Art14Helper;

/// <summary>Windows' power mode, as in Settings &gt; System &gt; Power.</summary>
internal enum PowerModeChoice
{
    Efficiency,
    Balanced,
    Performance
}

/// <summary>
/// Windows' power mode slider (a power plan "overlay"), for the current power source:
/// Windows keeps one for the charger and one for battery. No admin rights needed.
/// Only works while the Balanced power plan is active, as in Windows Settings.
/// </summary>
internal static partial class PowerMode
{
    private static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");   // some builds report the plan itself
    private static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>The mode for the current power source; null when Windows doesn't offer power modes.</summary>
    internal static PowerModeChoice? Current
    {
        get
        {
            if (PowerGetEffectiveOverlayScheme(out var overlay) != 0)
                return null;
            return FromGuid(overlay);
        }
    }

    internal static PowerModeChoice? FromGuid(Guid overlay)
        => overlay == Efficiency ? PowerModeChoice.Efficiency
            : overlay == Balanced || overlay == BalancedPlan ? PowerModeChoice.Balanced
            : overlay == Performance ? PowerModeChoice.Performance
            : null;

    internal static bool Set(PowerModeChoice mode)
    {
        var overlay = mode switch
        {
            PowerModeChoice.Efficiency => Efficiency,
            PowerModeChoice.Performance => Performance,
            _ => Balanced
        };
        var result = PowerSetActiveOverlayScheme(overlay);
        if (result != 0)
            Log.Error($"Power mode {mode} not set (error {result})");
        return result == 0;
    }

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerSetActiveOverlayScheme(Guid overlay);
}

/// <summary>The power mode choices for the tray menu and the settings window.</summary>
internal static class PowerModeMenu
{
    // English here, translated where they're shown.
    private static readonly (PowerModeChoice Mode, string Text, string Hint)[] Choices =
    [
        (PowerModeChoice.Efficiency, N("Best power efficiency"), N("Longest battery life, quiet and cool; apps may feel slower.")),
        (PowerModeChoice.Balanced, N("Balanced"), N("Speed when needed, savings when not. Right for most of the time.")),
        (PowerModeChoice.Performance, N("Best performance"), N("Full speed for heavy work and games; uses more power and gets warmer."))
    ];

    /// <summary>The choices, with <paramref name="current"/> checked (read once by the caller).</summary>
    internal static void Build(IMenuBuilder menu, PowerModeChoice current)
    {
        menu.AddItem(PowerSource.IsPluggedIn ? T("On the charger") : T("On battery"), null, enabled: false,
            tooltip: T("Windows keeps one power mode for the charger and one for battery; this sets the one in use now."));
        foreach (var (mode, text, hint) in Choices)
            menu.AddItem(T(text), () => Choose(mode), @checked: current == mode, tooltip: T(hint));
    }

    /// <summary>No popup on success: the user chose it. Only a failure is worth saying.</summary>
    private static void Choose(PowerModeChoice mode)
    {
        if (!PowerMode.Set(mode))
            Popup.Failed(T("Power mode not changed"));
    }
}
