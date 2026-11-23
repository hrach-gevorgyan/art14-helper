namespace Art14Helper;

/// <summary>
/// <c>--self-test</c>: proves a build works where it matters for NativeAOT (COM calls into
/// WMI, GDI+ drawing, windows and the message loop) without touching any setting. Writes
/// one line per check to the log; exit code 0 when every check passed, 1 otherwise.
/// Used by CI on the published exe.
/// </summary>
internal static class SelfTest
{
    internal const string Switch = "--self-test";

    internal static int Run()
    {
        var failed = 0;
        void Check(string name, Func<string> probe)
        {
            try
            {
                Log.Info($"Self-test {name}: {probe()}");
            }
            catch (Exception exception)
            {
                failed++;
                Log.Error($"Self-test {name} failed", exception);
            }
        }

        Check("WMI query", () => Wmi.FirstText("SELECT Name FROM Win32_Processor", "Name") ?? throw new InvalidOperationException("no processor listed"));
        Check("WMI rows", () =>
        {
            var count = 0;
            Wmi.ForEach("SELECT Name FROM Win32_Service WHERE Name = 'Winmgmt'", row => count += row.Text("Name").Length > 0 ? 1 : 0);
            return count == 1 ? "1 row" : throw new InvalidOperationException($"{count} rows");
        });
        Check("devices", () => Devices.ListPresent() is { Count: > 0 } devices ? $"{devices.Count} present" : throw new InvalidOperationException("no devices listed"));
        Check("tray icon", () => $"{TrayIcons.Create().Width} px");
        Check("popup", () =>
        {
            using var card = Popup.Render(PopupIcon.Battery, "Charging · 55%", "Self-test", 1f, battery: 55);
            return $"{card.Width}x{card.Height}";
        });
        Check("settings window", () =>
        {
            using var window = new SettingsWindow(_ => { }, () => false, () => { }, () => { },
                () => Task.FromResult<IReadOnlyList<DriverComponent>>([]));
            UiThread.After(800, window.Close);
            UiThread.RunUntil(() => !window.IsOpen);
            return "opened and closed";
        });
        Check("Intel color library", () =>
        {
            // Reads only: proves the driver's library still loads with the System32-only DLL rule.
            if (!IntelControlLibrary.IsPresent())
                return "no Intel graphics driver";
            return IntelColorPipeline.CanStart() ? "loads and starts" : throw new InvalidOperationException("didn't start (see the log)");
        });
        Check("efficiency mode", () => Efficiency.Set(true) ? "on" : "not supported by this Windows");
        Check("memory", () =>
        {
            Memory.Trim();
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            return $"{self.PrivateMemorySize64 / 1048576.0:0.0} MB private, {self.WorkingSet64 / 1048576.0:0.0} MB working set, managed heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MB";
        });
        Log.Info(failed == 0 ? "Self-test passed" : $"Self-test: {failed} check(s) failed");
        return failed == 0 ? 0 : 1;
    }
}
