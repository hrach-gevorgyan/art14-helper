using System.Globalization;
using System.Numerics;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Art14Helper;

/// <summary>When a hardware action may bring up a UAC prompt (only matters without admin rights).</summary>
internal enum AdminPrompt
{
    /// <summary>Never: the user didn't trigger it (wake restore, background work).</summary>
    Never,

    /// <summary>Ask, but stop asking for this session after the first "No".</summary>
    UntilDeclined,

    /// <summary>Always: the user just asked for it.</summary>
    Always
}

/// <summary>
/// Actions that change hardware or system settings and so need admin rights. When the
/// tray app is elevated (the usual case, via its logon task) they run in-process.
/// Otherwise, if the <see cref="AdminPrompt"/> allows, the exe is started again
/// through UAC to do just that one action.
/// </summary>
internal static class Privileged
{
    /// <summary>True when this process runs with admin rights (elevated).</summary>
    internal static bool IsElevated { get; } = Environment.IsPrivilegedProcess;

    private static volatile bool _promptDeclined;

    internal static readonly PrivilegedAction<KeyboardLight> KeyboardLight =
        PrivilegedAction.ForEnum<KeyboardLight>("keyboard-light", KeyboardLights.Set);

    internal static readonly PrivilegedAction<ushort> KeyboardLightTimeout =
        PrivilegedAction.ForNumber<ushort>("keyboard-light-timeout", KeyboardLights.SetIdleTimeout);

    /// <summary>Mode plus idle timeout, used when putting the light back after a restart.</summary>
    internal static readonly PrivilegedAction<(KeyboardLight Light, ushort Seconds)> KeyboardLightState =
        new("keyboard-light-state",
            state => $"{state.Light},{state.Seconds}",
            ParseLightState,
            state => KeyboardLights.SetWithTimeout(state.Light, state.Seconds));

    internal static readonly PrivilegedAction<ChargeLimit> ChargeLimit =
        PrivilegedAction.ForEnum<ChargeLimit>("charge-limit", ChargeLimits.Apply);

    internal static readonly PrivilegedAction<bool> CameraOff =
        PrivilegedAction.ForBool("camera-off", CameraSwitch.Set);

    internal static readonly PrivilegedAction<FnKeyMode> FnMode =
        PrivilegedAction.ForEnum<FnKeyMode>("fn-mode", FnLockController.SetMode);

    internal static readonly PrivilegedAction<bool> SpatialAudio =
        PrivilegedAction.ForBool("spatial-audio", SpatialAudioController.SetSpatial);

    internal static readonly PrivilegedAction<bool> TouchscreenOff =
        PrivilegedAction.ForBool("touchscreen-off", TouchscreenControl.SetDisabled);

    internal static readonly PrivilegedAction<bool> StartupEnabled =
        PrivilegedAction.ForBool("startup-enabled", Autostart.SetEnabled);

    private static readonly PrivilegedAction[] All =
    [
        KeyboardLight, KeyboardLightTimeout, KeyboardLightState,
        ChargeLimit, CameraOff, FnMode, SpatialAudio, TouchscreenOff, StartupEnabled
    ];

    /// <summary>
    /// Runs the action a UAC-launched copy was started for. Returns false for an
    /// unknown name or a value that doesn't parse; hardware errors are thrown.
    /// </summary>
    internal static bool RunFromCommandLine(string name, string value)
        => All.FirstOrDefault(action => action.Name == name)?.RunHere(value) ?? false;

    internal static bool MayPrompt(AdminPrompt prompt)
        => prompt == AdminPrompt.Always || prompt == AdminPrompt.UntilDeclined && !_promptDeclined;

    /// <summary>Remembers a "No" to UAC so <see cref="AdminPrompt.UntilDeclined"/> stops asking.</summary>
    internal static void NotePromptDeclined() => _promptDeclined = true;

    /// <summary>Launches this exe through UAC for one action and waits for its exit code.</summary>
    internal static bool RunInElevatedCopy(string name, string value)
    {
        var exe = Environment.ProcessPath ?? throw Shown(m => new InvalidOperationException(m), N("The app's own path is unknown."));   // can reach the user through ApplyFromMenuAsync
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--set-" + name);
        start.ArgumentList.Add(value);
        start.ArgumentList.Add(L10n.LanguageArgument);   // its error box in this copy's language
        try
        {
            using var copy = System.Diagnostics.Process.Start(start);
            copy?.WaitForExit();
            return copy?.ExitCode == 0;   // no process means Windows didn't start one
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == PrivilegedAction.PromptCancelled)
        {
            NotePromptDeclined();
            throw;
        }
        catch (Win32Exception exception)
        {
            Log.Error("UAC launch failed", exception);
            return false;
        }
    }

    internal static bool ParseLightState(string text, out (KeyboardLight Light, ushort Seconds) state)
    {
        state = default;
        var comma = text.IndexOf(',');
        if (comma <= 0
            || !EnumText.TryParse<KeyboardLight>(text.AsSpan(0, comma), out var level)
            || !ushort.TryParse(text.AsSpan(comma + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return false;
        state = (level, seconds);
        return true;
    }
}

/// <summary>Name-addressable part of an action, for the command line.</summary>
internal abstract class PrivilegedAction(string name)
{
    /// <summary>ERROR_CANCELLED, what Windows reports when UAC is answered with "No".</summary>
    internal const int PromptCancelled = 1223;

    private static readonly Lock RunLock = new();

    internal string Name { get; } = name;

    /// <summary>Parses and runs in this (elevated) process. False when the value doesn't parse.</summary>
    internal abstract bool RunHere(string value);

    /// <summary>One action at a time: the BIOS and the registry aren't made for concurrent writers.</summary>
    protected static void Exclusive(Action work)
    {
        lock (RunLock)
            work();
    }

    internal static PrivilegedAction<T> ForEnum<T>(string name, Action<T> apply) where T : struct, Enum
        => new(name, value => value.ToString(), (string text, out T value) => EnumText.TryParse(text, out value), apply);

    internal static PrivilegedAction<bool> ForBool(string name, Action<bool> apply)
        => new(name, value => value.ToString(), bool.TryParse, apply);

    internal static PrivilegedAction<T> ForNumber<T>(string name, Action<T> apply) where T : struct, IBinaryInteger<T>
        => new(name, value => value.ToString(null, CultureInfo.InvariantCulture),
            (string text, out T value) => T.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value), apply);
}

/// <summary>One hardware action with a typed value.</summary>
internal sealed class PrivilegedAction<T>(string name, Func<T, string> format, PrivilegedAction<T>.Parser parse, Action<T> apply)
    : PrivilegedAction(name)
{
    internal delegate bool Parser(string text, out T value);

    internal override bool RunHere(string value)
    {
        if (!parse(value, out var parsed))
            return false;
        Exclusive(() => apply(parsed));
        return true;
    }

    /// <summary>
    /// Runs the action: here when elevated, else in an elevated copy if
    /// <paramref name="prompt"/> allows. True = applied; false = couldn't run.
    /// Throws when the hardware refused (the message says why) or the user
    /// cancelled the UAC prompt (<see cref="Win32Exception"/> 1223).
    /// </summary>
    internal bool Run(T value, AdminPrompt prompt = AdminPrompt.Always)
    {
        if (!Privileged.IsElevated)
            return Privileged.MayPrompt(prompt) && Privileged.RunInElevatedCopy(Name, format(value));
        try
        {
            Exclusive(() => apply(value));
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Hardware action {Name} {format(value)} failed", exception);
            throw;
        }
    }

    /// <summary><see cref="Run"/> on the thread pool (it may wait for a UAC prompt or the BIOS).</summary>
    internal Task<bool> RunAsync(T value, AdminPrompt prompt = AdminPrompt.Always) => Task.Run(() => Run(value, prompt));

    /// <summary>
    /// A menu or settings change: runs the action and calls <paramref name="onApplied"/>
    /// only when it took effect. Problems are shown to the user; a cancelled UAC prompt
    /// is not a problem.
    /// </summary>
    internal async Task ApplyFromMenuAsync(T value, string failureMessage, Action? onApplied = null)
    {
        string? problem = null;
        try
        {
            if (await RunAsync(value))
                onApplied?.Invoke();
            else
            {
                Log.Error($"Setting not applied: {Name} {format(value)}");
                // Without admin rights a false result means the elevated copy already
                // showed its own error, or the prompt policy didn't allow asking.
                if (Privileged.IsElevated)
                    problem = failureMessage;
            }
        }
        catch (Win32Exception cancelled) when (cancelled.NativeErrorCode == PromptCancelled)
        {
            // The user said no to UAC: nothing to report.
        }
        catch (Exception failure)
        {
            problem = ShownMessage(failure);
        }
        if (problem is not null)
            CommonDialogs.Message(User32.GetActiveWindow(), problem, AppInfo.Name, CommonDialogs.Icon.Error);
    }
}
