using System.Diagnostics;

namespace Art14Helper;

/// <summary>Runs a Windows console tool from System32 (pnputil, schtasks) without a window.</summary>
internal static class SystemTool
{
    /// <summary>The tool's exit code and everything it printed (output and errors).</summary>
    internal readonly record struct Outcome(int ExitCode, string Output)
    {
        /// <summary>The last few non-empty lines: enough to explain a failure in a message.</summary>
        internal string Tail(int lines = 3)
            => string.Join(' ', Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(lines));
    }

    internal static Outcome Run(string tool, params string[] arguments)
    {
        // No shell (the .NET default), no console window, both output pipes captured.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, tool)) { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        arguments.ToList().ForEach(start.ArgumentList.Add);

        using var process = Process.Start(start) ?? throw Shown(m => new InvalidOperationException(m), N("{0} didn't start."), tool);
        // Both pipes are drained at once: a tool that fills one while the other is being
        // read would wait forever (pnputil's export prints a line per driver).
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var outcome = new Outcome(process.ExitCode, (output.Result + errors.Result).Trim());
        Log.Info($"{tool} {string.Join(' ', arguments)} → {outcome.ExitCode}");
        return outcome;
    }
}
