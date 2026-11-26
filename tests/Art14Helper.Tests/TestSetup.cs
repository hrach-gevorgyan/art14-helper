using System.Runtime.CompilerServices;

namespace Art14Helper.Tests;

internal static class TestSetup
{
    /// <summary>Tests feed the app bad data on purpose; none of it belongs in the user's log.</summary>
    [ModuleInitializer]
    internal static void MuteLog()
    {
        Log.Muted = true;
        // Texts are checked in English, whatever the machine's display language or the app's setting.
        L10n.Current = AppLanguage.English;
    }
}
