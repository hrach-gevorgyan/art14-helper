using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>What closing the lid does (the "Lid close action" power setting).</summary>
internal enum LidAction : uint
{
    Nothing = 0,
    Sleep = 1,
    Hibernate = 2,
    ShutDown = 3
}

/// <summary>
/// The lid close action of the active power plan, separately for the charger (AC) and
/// battery (DC), as Control Panel's "Choose what closing the lid does" sets it.
/// Writing needs admin rights, which the installed app has.
/// </summary>
internal static unsafe partial class PowerSettings
{
    private static readonly Guid Buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");      // SUB_BUTTONS
    private static readonly Guid LidClose = new("5ca83367-6e45-459f-a27b-476b1d01c936");     // LIDACTION

    /// <summary>The action now; null when Windows didn't say.</summary>
    internal static LidAction? GetLid(bool onBattery) => ActiveScheme() is { } scheme ? ReadLid(scheme, onBattery) : null;

    /// <summary>The action in a given plan (the one a change was made in), active or not.</summary>
    internal static LidAction? GetLid(bool onBattery, Guid plan) => ReadLid(plan, onBattery);

    /// <summary>The active power plan; null when Windows didn't say.</summary>
    internal static Guid? ActivePlan => ActiveScheme();

    /// <summary>Both actions from one look at the active plan (a menu shows both).</summary>
    internal static (LidAction? Charger, LidAction? Battery) GetLids()
        => ActiveScheme() is { } scheme ? (ReadLid(scheme, false), ReadLid(scheme, true)) : (null, null);

    private static LidAction? ReadLid(Guid scheme, bool onBattery)
    {
        var (buttons, lid) = (Buttons, LidClose);
        uint value;
        var result = onBattery
            ? PowerReadDCValueIndex(IntPtr.Zero, &scheme, &buttons, &lid, &value)
            : PowerReadACValueIndex(IntPtr.Zero, &scheme, &buttons, &lid, &value);
        return result == 0 && Enum.IsDefined((LidAction)value) ? (LidAction)value : null;
    }

    /// <summary>
    /// Sets the action in <paramref name="plan"/> (the active plan when null) and, in the active
    /// plan, makes it take effect at once; false when Windows refused.
    /// </summary>
    internal static bool SetLid(bool onBattery, LidAction action, Guid? plan = null)
    {
        var active = ActiveScheme();
        if ((plan ?? active) is not { } scheme)
            return false;
        var (buttons, lid) = (Buttons, LidClose);
        var result = onBattery
            ? PowerWriteDCValueIndex(IntPtr.Zero, &scheme, &buttons, &lid, (uint)action)
            : PowerWriteACValueIndex(IntPtr.Zero, &scheme, &buttons, &lid, (uint)action);
        if (result == 0 && scheme == active)
            result = PowerSetActiveScheme(IntPtr.Zero, &scheme);   // re-applies the plan, so the change counts now
        if (result != 0)
            Log.Error($"Lid action {action} ({(onBattery ? "battery" : "charger")}) not set (error {result})");
        return result == 0;
    }

    private static Guid? ActiveScheme()
    {
        Guid* scheme;
        if (PowerGetActiveScheme(IntPtr.Zero, &scheme) != 0 || scheme == null)
            return null;
        var copy = *scheme;
        LocalFree(scheme);
        return copy;
    }

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerGetActiveScheme(IntPtr root, Guid** scheme);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerSetActiveScheme(IntPtr root, Guid* scheme);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerReadACValueIndex(IntPtr root, Guid* scheme, Guid* subgroup, Guid* setting, uint* value);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerReadDCValueIndex(IntPtr root, Guid* scheme, Guid* subgroup, Guid* setting, uint* value);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerWriteACValueIndex(IntPtr root, Guid* scheme, Guid* subgroup, Guid* setting, uint value);

    [LibraryImport(Libraries.PowrProf)]
    private static partial uint PowerWriteDCValueIndex(IntPtr root, Guid* scheme, Guid* subgroup, Guid* setting, uint value);

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr LocalFree(void* memory);
}
