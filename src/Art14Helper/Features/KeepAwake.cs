using System.Runtime.InteropServices;

namespace Art14Helper;

/// <summary>
/// Keeps the laptop from sleeping, for a while or until turned off, like the Caffeine tool:
/// Windows' own request (SetThreadExecutionState) on the UI thread, which lives as long as
/// the app. Optionally the screen stays on too, and an F15 tap after a minute without input
/// keeps chat apps from showing the user as away (F15 does nothing anywhere else).
/// Closing the lid still sleeps the laptop. Nothing runs while it's off; all calls on the UI thread.
/// </summary>
internal static partial class KeepAwake
{
    private const uint Continuous = 0x80000000, SystemRequired = 0x1, DisplayRequired = 0x2;   // ES_*
    private const uint PresenceCheckMilliseconds = 30_000;
    private const uint IdleBeforeTapMilliseconds = 50_000;
    private const byte F15 = 0x7E;

    private static UiTimer? _end;
    private static UiTimer? _presence;
    private static bool _screenHold;   // the screen is off on purpose: stay awake until it's back
    private static long _screenHeldSince;

    /// <summary>When it ends; <see cref="DateTime.MaxValue"/> until turned off; null while off.</summary>
    internal static DateTime? Until { get; private set; }

    /// <summary>The duration last chosen (null: until turned off), for the menu's check mark.</summary>
    internal static TimeSpan? Chosen { get; private set; }

    internal static bool IsOn => Until is not null;

    /// <summary>Turned on or off (also when the time ran out).</summary>
    internal static event Action? Changed;

    /// <summary>On for <paramref name="duration"/>, or until turned off when null.</summary>
    internal static void Start(TimeSpan? duration)
        => Begin(duration, duration is { } length ? (DateTime.UtcNow + length).ToLocalTime() : DateTime.MaxValue);

    /// <summary>
    /// At app start: still on from before a restart (an update, a crash, a reboot), so it goes
    /// on with the time that's left. Nothing when it ran out meanwhile.
    /// </summary>
    internal static void Resume()
    {
        if (IsOn || ResumeUntil(AppSettings.KeepAwakeUntil, DateTime.UtcNow) is not { } until)
        {
            if (!IsOn)
                AppSettings.KeepAwakeUntil = null;   // ran out while the app wasn't running
            return;
        }
        var chosen = AppSettings.KeepAwakeMinutes is > 0 and var minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        Begin(chosen, until, resumed: true);
    }

    /// <summary>
    /// After waking from sleep: the end is checked against the clock (a countdown doesn't run
    /// while the laptop sleeps), so it ends on time or at once if the time passed during sleep.
    /// </summary>
    internal static void Recheck()
    {
        if (Until is { } until && until != DateTime.MaxValue)
            ArmEnd(until);
    }

    /// <summary>
    /// The stored end time as local time: <see cref="DateTime.MaxValue"/> for "until turned
    /// off", null when it's missing, unreadable or already past.
    /// </summary>
    internal static DateTime? ResumeUntil(string? stored, DateTime nowUtc)
    {
        if (stored == UntilTurnedOff)
            return DateTime.MaxValue;
        return DateTime.TryParse(stored, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.RoundtripKind, out var end) && end.ToUniversalTime() > nowUtc
            ? end.ToLocalTime()
            : null;
    }

    private const string UntilTurnedOff = "until turned off";

    private static void Begin(TimeSpan? chosen, DateTime until, bool resumed = false)
    {
        StopTimers();
        Chosen = chosen;
        Until = until;
        if (until != DateTime.MaxValue)
            ArmEnd(until);
        // Saved, so a restart of the app (an update, a crash) doesn't end it.
        AppSettings.KeepAwakeUntil = until == DateTime.MaxValue ? UntilTurnedOff : until.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        AppSettings.KeepAwakeMinutes = chosen is { } length ? (int)length.TotalMinutes : 0;
        Apply();
        // In English for the log; Describe() is in the user's language.
        Log.Info($"Keep awake {(resumed ? "resumed after a restart" : "on")}, {(until == DateTime.MaxValue ? UntilTurnedOff : $"until {until:t}")}");   // no popup: the user chose it
        Changed?.Invoke();
    }

    /// <summary>A countdown to <paramref name="until"/>; ends it at once when that's already past.</summary>
    private static void ArmEnd(DateTime until)
    {
        _end?.Dispose();
        _end = null;
        var left = until.ToUniversalTime() - DateTime.UtcNow;   // in UTC: a daylight-saving change doesn't move the end
        if (left <= TimeSpan.Zero)
        {
            Stop(ended: true);
            return;
        }
        _end = new UiTimer((uint)Math.Min(left.TotalMilliseconds + 500, int.MaxValue), () => Stop(ended: true));
        _end.Start();
    }

    internal static void Stop(bool ended = false)
    {
        if (!IsOn)
            return;
        StopTimers();
        Until = null;
        Chosen = null;
        AppSettings.KeepAwakeUntil = null;
        AppSettings.KeepAwakeMinutes = null;
        Apply();
        Log.Info(ended ? "Keep awake ended" : "Keep awake off");
        if (ended)   // the time ran out by itself: worth a word; turned off by the user: not
            Popup.Show(PopupIcon.AwakeEnded, T("Keep awake ended"), T("Sleep allowed"));
        Changed?.Invoke();
    }

    /// <summary>
    /// The screen was turned off on purpose (Remote mode). On a Modern Standby
    /// laptop Windows treats a dark screen as the way into standby and starts pausing programs;
    /// this keeps the laptop fully awake until the screen is on again.
    /// </summary>
    internal static void HoldWhileScreenOff()
    {
        _screenHold = true;
        _screenHeldSince = Environment.TickCount64;
        Apply();
    }

    /// <summary>The screen reported dark (state 0); a hold whose screen never went dark is ended by the caller.</summary>
    internal static bool ScreenIsOff { get; set; }

    /// <summary>
    /// Ends a hold (the screen is on again, the laptop resumed, or the screen never went dark).
    /// Returns how long it lasted, or null when there was none.
    /// </summary>
    internal static TimeSpan? EndScreenHold()
    {
        if (!_screenHold)
            return null;
        _screenHold = false;
        Apply();
        return TimeSpan.FromMilliseconds(Environment.TickCount64 - _screenHeldSince);
    }

    /// <summary>An option changed: asks Windows again with the new screen choice, starts or stops the presence taps.</summary>
    internal static void Apply()
    {
        // Remote mode lets the screen go off: nobody is looking at it. Neither while it was turned off on purpose.
        var screen = AppSettings.KeepScreenOn && !RemoteMode.IsOn && !_screenHold ? DisplayRequired : 0;
        var awake = IsOn || _screenHold;
        if (SetThreadExecutionState(awake ? Continuous | SystemRequired | screen : Continuous) == 0)
            Log.Error("Keep awake request refused by Windows");

        var tap = IsOn && AppSettings.KeepAppsActive;
        if (tap && _presence is null)
        {
            _presence = new UiTimer(PresenceCheckMilliseconds, TapIfIdle);
            _presence.Start();
        }
        else if (!tap && _presence is not null)
        {
            _presence.Dispose();
            _presence = null;
        }
    }

    /// <summary>"until 15:30", "until turned off" or "off".</summary>
    internal static string Describe() => Until is not { } until ? T("off")
        : until == DateTime.MaxValue ? T("until turned off")
        : T("until {0:t}", until);

    private static void TapIfIdle()
    {
        if (User32.IdleMilliseconds >= IdleBeforeTapMilliseconds)
            User32.TapKey(F15);
    }

    private static void StopTimers()
    {
        _end?.Dispose();
        _end = null;
        _presence?.Dispose();
        _presence = null;
    }

    [LibraryImport(Libraries.Kernel32)]
    private static partial uint SetThreadExecutionState(uint flags);
}

/// <summary>The keep awake choices for the tray menu and the settings window.</summary>
internal static class KeepAwakeMenu
{
    // English here, translated where they're shown.
    private static readonly (TimeSpan? Duration, string Text)[] Durations =
    [
        (TimeSpan.FromMinutes(30), N("30 minutes")),
        (TimeSpan.FromHours(1), N("1 hour")),
        (TimeSpan.FromHours(2), N("2 hours")),
        (TimeSpan.FromHours(4), N("4 hours")),
        (null, N("Until turned off"))
    ];

    internal static void Build(IMenuBuilder menu)
    {
        var remote = RemoteMode.IsOn;
        var awake = KeepAwake.IsOn;

        menu.AddItem(T("Stay awake"), null, enabled: false,
            tooltip: remote ? T("Remote mode keeps the laptop awake until you turn it off.") : null);
        // A duration or Off ends remote mode first: the user took over.
        menu.AddItem(T("Off"), () => { RemoteMode.Stop(); KeepAwake.Stop(); }, @checked: !awake,
            tooltip: T("The laptop sleeps as Windows' power settings say."));
        foreach (var (duration, text) in Durations)
        {
            var current = awake && !remote && KeepAwake.Chosen == duration;
            menu.AddItem(current && duration is not null ? T("{0} ({1})", T(text), KeepAwake.Describe()) : T(text),
                () => { RemoteMode.Stop(); KeepAwake.Start(duration); }, @checked: current);
        }

        // Only matter while awake; remote mode always lets the screen go off.
        menu.AddSeparator();
        menu.AddItem(T("While awake"), null, enabled: false,
            tooltip: awake ? null : T("Turn on keep awake above to use these."));
        menu.AddItem(T("Keep the screen on"), () => Flip(() => AppSettings.KeepScreenOn, on => AppSettings.KeepScreenOn = on),
            @checked: AppSettings.KeepScreenOn && !remote, toggle: true, enabled: awake && !remote,
            tooltip: remote ? T("Remote mode turns the screen off: nobody is looking at it.")
                : T("Off: only the laptop stays awake; the screen still turns off and locks as usual."));
        menu.AddItem(T("Stop apps showing you as away"), () => Flip(() => AppSettings.KeepAppsActive, on => AppSettings.KeepAppsActive = on),
            @checked: AppSettings.KeepAppsActive, toggle: true, enabled: awake,
            tooltip: T("A harmless F15 key press after a minute without input keeps Teams, Slack and others from showing you as away."));

        menu.AddSeparator();
        menu.AddItem(T("Away from the laptop"), null, enabled: false);
        menu.AddItem(T("Remote mode"), () => { if (RemoteMode.IsOn) RemoteMode.Stop(); else RemoteMode.Start(); },
            @checked: remote, toggle: true,
            tooltip: T("For work that runs while you're away (a long task, a build, a download): stays awake, turns the screen off and keeps running with the lid closed, on the charger and on battery. Off puts everything back."));

        menu.AddSeparator();
        menu.AddItem(T("When the lid closes"), null, enabled: false,
            tooltip: remote ? T("Remote mode keeps the laptop running with the lid closed.") : null);
        var (charger, battery) = PowerSettings.GetLids();
        AddLid(menu, onBattery: false, charger == LidAction.Nothing, T("Keep running on the charger"), T("For an external monitor, or remote work at the desk."), !remote);
        AddLid(menu, onBattery: true, battery == LidAction.Nothing, T("Keep running on battery"),
            T("Careful in a bag: a closed laptop that keeps running gets warm and drains the battery."), !remote);
    }

    /// <summary>A switch of the page: flips the stored value, then asks Windows again.</summary>
    private static void Flip(Func<bool> get, Action<bool> set)
    {
        set(!get());
        KeepAwake.Apply();
    }

    private static void AddLid(IMenuBuilder menu, bool onBattery, bool keepsRunning, string text, string hint, bool enabled)
        => menu.AddItem(text, () =>
            {
                // Read again on click: the menu may be older than the setting.
                if (!LidClosed.SetKeepRunning(onBattery, !LidClosed.KeepsRunning(onBattery)))
                    Popup.Failed(T("Lid setting not changed"));
            },
            @checked: keepsRunning, toggle: true, enabled: enabled, tooltip: hint);
}
