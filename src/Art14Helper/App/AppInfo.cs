using System.Reflection;

namespace Art14Helper;

/// <summary>Names shown to the user, kept in one place.</summary>
internal static class AppInfo
{
    internal const string Name = "Art14 Helper";

    /// <summary>The release number, without the build metadata after "+".</summary>
    internal static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    internal const string Disclaimer =
        "Unofficial tool for the HONOR MagicBook Art 14. Not made, endorsed or supported by HONOR. "
        + "HONOR and MagicBook are trademarks of their owner.";

    internal const string Credits =
        "Made to replace HONOR PC Manager's bloatware with one small app. "
        + "Inspired by open-source projects on GitHub.";

    internal const string License =
        "Free software under the GNU General Public License v3: use, study, change and share it; "
        + "versions you share stay free too. Comes with no warranty.";
}
