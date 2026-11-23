namespace Art14Helper;

/// <summary>
/// A driver or firmware piece as HONOR's update service numbers it. A component that
/// isn't installed carries the version "0" (that's also what the server expects for
/// it). Devices Windows has no driver for get negative ids.
/// </summary>
internal sealed record DriverComponent
{
    internal const string NotInstalled = "0";

    /// <summary>HONOR's component number (componentID), or negative for a device without driver.</summary>
    public required int Id { get; init; }

    /// <summary>HONOR's package name, e.g. "WiFi" or "BIOS".</summary>
    public required string Name { get; init; }

    /// <summary>Generic English name, used when no device name is known.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Installed version as Windows reports it, or <see cref="NotInstalled"/>.</summary>
    public string CurrentVersion { get; init; } = NotInstalled;

    /// <summary>Extra matching key the server wants for some components (BIOS: model, fingerprint: VID/PID).</summary>
    public string PackageType { get; init; } = "";

    /// <summary>Name of the matched device in Windows, if any.</summary>
    public string? DeviceName { get; init; }

    /// <summary>Application name the server expects when it differs from <see cref="Name"/>.</summary>
    public string? AppName { get; init; }

    internal bool IsInstalled => CurrentVersion != NotInstalled;
}

/// <summary>A package HONOR offers for one component.</summary>
internal sealed record DriverUpdate
{
    public required DriverComponent Component { get; init; }
    public required string Version { get; init; }

    /// <summary>Where the package lives; see <see cref="DownloadUrls.ForPackage"/>.</summary>
    public required string DownloadBaseUrl { get; init; }

    /// <summary>True when <see cref="Version"/> is comparable with the installed one and newer.</summary>
    public bool IsUpdate { get; init; }

    /// <summary>Expected SHA-256 of the download as hex, when the server gives one.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Release date as yyyy-MM-dd, when known.</summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Title of the package on the support site, used for the file name.</summary>
    public string? PackageTitle { get; init; }
}

/// <summary>
/// Outcome of one update check. <see cref="IsComplete"/> is false when a catalog that
/// was needed did not answer, so "no updates" can't be trusted.
/// </summary>
internal sealed record DriverCheckResult(
    IReadOnlyList<DriverComponent> Components,
    IReadOnlyList<DriverUpdate> Updates,
    bool IsComplete);
