
namespace Art14Helper;

/// <summary>
/// BIOS and driver updates from HONOR, for the Drivers page: what's installed, what
/// HONOR offers for it, and a verified download.
/// </summary>
/// <remarks>
/// A check asks the update platform first and the support site only when the
/// platform has nothing usable (it doesn't know every model, and some of its files
/// have been removed from the CDN).
/// </remarks>
internal sealed class DriverUpdateService
{
    /// <summary>Installed components plus a not-installed entry for the rest of HONOR's catalog.</summary>
    internal Task<IReadOnlyList<DriverComponent>> BuildDeviceListAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DriverComponent>>(
            () => DeviceInventory.WithCatalogPlaceholders(DeviceInventory.ReadFromSystem()), cancellationToken);

    /// <summary>Asks HONOR what it offers for <paramref name="components"/> (from <see cref="BuildDeviceListAsync"/>).</summary>
    internal async Task<DriverCheckResult> CheckAsync(IReadOnlyList<DriverComponent> components, CancellationToken cancellationToken = default)
    {
        var all = DeviceInventory.WithCatalogPlaceholders(components);
        // Devices without a driver (negative ids) mean nothing to HONOR's servers.
        var asked = all.Where(c => c.Id > 0).ToList();
        var machine = await Task.Run(MachineProfile.ReadFromSystem, cancellationToken);
        Log.Info($"Checking HONOR driver updates for {machine.DeviceName}/{machine.CVersion}: "
            + $"{components.Count} detected, {asked.Count} asked");
        Log.Info($"Installed component versions: {HonorUpdatePlatform.Dashboard(asked)}");

        var fromPlatform = await HonorUpdatePlatform.QueryAsync(asked, machine, cancellationToken);
        var stillThere = await KeepDownloadableAsync(fromPlatform, cancellationToken);

        var packages = stillThere;
        var complete = true;
        if (stillThere.Count == 0 || stillThere.Count < fromPlatform.Count)
        {
            var site = await HonorSupportSite.QueryAsync(machine, asked, cancellationToken);
            packages = [.. stillThere, .. await KeepDownloadableAsync(site.Packages, cancellationToken)];
            all.AddRange(site.NewComponents.Where(n => all.All(c => c.Id != n.Id)));
            complete = site.Reached;
        }

        var best = packages.GroupBy(p => p.Component.Id).Select(BestOf).ToList();
        foreach (var update in best.Where(p => p.IsUpdate))
            Log.Info($"Update available for {update.Component.Name}: {update.Component.CurrentVersion} -> {update.Version}");
        Log.Info($"HONOR driver update check done: {best.Count(p => p.IsUpdate)} update(s), {best.Count} package(s)");
        return new DriverCheckResult(all, best, complete);
    }

    /// <summary>
    /// Chooses one package per component. Packages whose numbering matches the
    /// installed driver come first (a build date looks big but tells nothing), then the
    /// highest version wins.
    /// </summary>
    internal static DriverUpdate BestOf(IEnumerable<DriverUpdate> candidates)
        => candidates
            .OrderByDescending(p => DriverVersions.AreComparable(p.Version, p.Component.CurrentVersion))
            .ThenByDescending(p => p.Version, DriverVersions.ByNumbers)
            .First();

    /// <summary>
    /// Drops packages whose file is gone from the server (404/410). Anything else,
    /// including a timeout, keeps the package: the download will tell.
    /// </summary>
    private static async Task<List<DriverUpdate>> KeepDownloadableAsync(
        IReadOnlyList<DriverUpdate> packages, CancellationToken cancellationToken)
    {
        var byUrl = packages.GroupBy(p => DownloadUrls.ForPackage(p.DownloadBaseUrl)).ToList();
        var gone = new HashSet<Uri>();
        await Parallel.ForEachAsync(byUrl,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (group, token) =>
            {
                try
                {
                    using var limit = DriverHttp.Limit(token, TimeSpan.FromSeconds(10));
                    using var probe = new HttpRequestMessage(HttpMethod.Head, group.Key);
                    using var response = await DriverHttp.Client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                    var status = (int)response.StatusCode;
                    if (status is not (404 or 410))
                        return;
                    Log.Info($"Skipping removed package {group.First().Component.Name} {group.First().Version} ({status})");
                    lock (gone)
                        gone.Add(group.Key);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    Log.Info($"Package check timed out for {group.Key.Host}");
                }
                catch (HttpRequestException exception)
                {
                    Log.Error($"Package check failed for {group.Key.Host}", exception);
                }
            });
        return byUrl.Where(group => !gone.Contains(group.Key)).SelectMany(group => group).ToList();
    }

    /// <summary>
    /// Asks where to save, downloads and verifies the package, then saves the installer
    /// (or the file itself, for a non-archive). Throws <see cref="OperationCanceledException"/>
    /// when the user cancels.
    /// </summary>
    internal async Task DownloadAsync(DriverUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var url = DownloadUrls.ForPackage(update.DownloadBaseUrl);
        var suggested = DownloadUrls.OriginalFileName(url) ?? FileNames.Safe(update.PackageTitle ?? update.Component.DisplayName);
        var chosen = AskWhereToSave(suggested, DriverPackage.SaveExtension(url));
        if (chosen is null)
            throw new OperationCanceledException("Saving the driver was cancelled.", cancellationToken);

        try
        {
            var verified = await DriverPackage.FetchAsync(update, progress, cancellationToken);

            // The dialog asked about overwriting its own name; if the verified file has
            // another extension, the real target needs its own question.
            var target = Path.ChangeExtension(chosen, Path.GetExtension(verified));
            if (!ConfirmReplace(target, alreadyConfirmed: chosen))
                throw new OperationCanceledException("Replacing the file was declined.", cancellationToken);
            File.Copy(verified, target, overwrite: true);
            Log.Info($"Saved verified driver package to {target}");
        }
        finally
        {
            // BIOS packages are large: nothing stays behind in the app's folder.
            try
            {
                Directory.Delete(DriverPackage.WorkFolder(update), recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Download folder not removed: {exception.Message}");
            }
        }
    }

    /// <summary>The save dialog; null when cancelled.</summary>
    private static string? AskWhereToSave(string name, string extension)
        => CommonDialogs.AskForFile(User32.GetActiveWindow(), save: true, T("Save driver"), T("Driver file (*{0})", extension) + $"|*{extension}",
            Path.ChangeExtension(name, extension), extension[1..]);

    private static bool ConfirmReplace(string target, string alreadyConfirmed)
        => string.Equals(target, alreadyConfirmed, StringComparison.OrdinalIgnoreCase)
           || !File.Exists(target)
           || CommonDialogs.Message(User32.GetActiveWindow(), T("The file {0} already exists. Replace it?", Path.GetFileName(target)), T("Save driver"),
               CommonDialogs.Icon.Warning, yesNo: true);
}
