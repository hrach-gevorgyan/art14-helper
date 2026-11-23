using System.Globalization;
namespace Art14Helper;

/// <summary>
/// BIOS and driver updates as a page of the settings window: the device list,
/// HONOR's update check, downloads, and driver backup/restore for a clean
/// reinstall. Holds its own state; the window draws it and redraws on
/// <see cref="Changed"/>.
/// </summary>
internal sealed class DriversPage : IDisposable
{
    private const int BiosId = 23;

    private readonly Func<Task<IReadOnlyList<DriverComponent>>> _initialList;
    private readonly DriverUpdateService _service = new();
    private readonly CancellationTokenSource _lifetime = new();
    // Taken once: the source is disposed with the page while work may still be running.
    private readonly CancellationToken _stop;
    private bool _disposed;
    private IReadOnlyList<DriverComponent>? _components;
    private Dictionary<int, DriverUpdate> _updates = [];
    private string _status = "Reading installed drivers…";
    private bool _statusIsWarning;
    private bool _busy;
    private int? _downloadingId;
    private string? _downloadText;
    private string? _serialNumber;
    private bool _started;
    private DateTime? _lastCheck;

    internal DriversPage(Func<Task<IReadOnlyList<DriverComponent>>> initialList)
    {
        _initialList = initialList;
        _stop = _lifetime.Token;
    }

    /// <summary>Raised on the UI thread whenever something on the page changed.</summary>
    internal event Action? Changed;

    /// <summary>Window the page's dialogs belong to.</summary>
    internal IntPtr Owner { get; set; }

    /// <summary>First visit: read the device list, then ask HONOR for updates.</summary>
    internal void StartIfNeeded()
    {
        if (_started)
            return;
        _started = true;
        _ = LoadAsync();
    }

    // After the window closed, work that finishes later changes nothing on screen.
    private void Notify()
    {
        if (!_disposed)
            Changed?.Invoke();
    }

    /// <summary>The owner for a message box; none once the window is gone, so the message still shows.</summary>
    private IntPtr DialogOwner => _disposed ? IntPtr.Zero : Owner;

    private async Task LoadAsync()
    {
        _ = LoadSerialNumberAsync();
        // The list read in the background at startup is used when it's ready; if that
        // read failed, one fresh attempt is made before giving up.
        _components = await TryAsync(_initialList, "Driver list from startup unusable")
            ?? await TryAsync(() => _service.BuildDeviceListAsync(_stop), "Driver list unreadable");
        if (_components is null)
        {
            SetStatus("The installed drivers couldn't be read", warning: true);
            return;
        }
        Notify();
        await CheckAsync();
    }

    private static async Task<IReadOnlyList<DriverComponent>?> TryAsync(Func<Task<IReadOnlyList<DriverComponent>>> read, string problem)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error(problem, exception);
            return null;
        }
    }

    private async Task LoadSerialNumberAsync()
    {
        _serialNumber = await Task.Run(ReadSerialNumber);
        Notify();
    }

    private void SetStatus(string text, bool warning = false)
    {
        _status = text;
        _statusIsWarning = warning;
        Notify();
    }

    private async Task CheckAsync()
    {
        if (_busy || _components is null)
            return;
        _busy = true;
        SetStatus("Checking for updates…");
        try
        {
            var result = await Task.Run(() => _service.CheckAsync(_components, _stop));
            if (!result.IsComplete)
            {
                SetStatus("HONOR's server didn't respond. Try again later.", warning: true);
                return;
            }
            _components = result.Components;
            _lastCheck = DateTime.Now;
            _updates = result.Updates.ToDictionary(update => update.Component.Id);   // one package per component already
            var count = Shown(_components).Count(c => _updates.TryGetValue(c.Id, out var u) && u.IsUpdate);
            SetStatus(count == 0 ? "Everything is up to date" : $"{count} update{(count == 1 ? "" : "s")} available",
                warning: count > 0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Error("Update check stopped", exception);
            SetStatus("The update check didn't finish", warning: true);
        }
        finally
        {
            _busy = false;
            Notify();
        }
    }

    private static IEnumerable<DriverComponent> Shown(IEnumerable<DriverComponent> components)
        => components.Where(c => c.Id > 0 && c.Id is not 87 and not 88);

    // ---- Page content ----

    internal List<(string? Title, List<RowSpec> Rows)> Sections()
    {
        var sections = new List<(string?, List<RowSpec>)>();
        var shown = _components is null ? [] : Shown(_components).ToList();
        var installed = shown.Where(c => c.CurrentVersion != "0").ToList();
        var checkedLine = _lastCheck is { } at
            ? $"Checked " + ((DateTime.Now - at).TotalMinutes < 1 ? "just now" : at.ToString("t", CultureInfo.CurrentCulture))
              + $" · HONOR lists {installed.Count(c => _updates.ContainsKey(c.Id))} of your {installed.Count} drivers"
            : $"{MachineProfile.ProductName} · serial {_serialNumber ?? "…"}";
        sections.Add((null,
        [
            new RowSpec
            {
                Text = _status,
                Secondary = checkedLine,
                Glyph = _busy ? "\uE895" : _statusIsWarning ? "\uE7BA" : "\uE73E",
                Kind = _busy ? RowKind.Info : RowKind.Action,
                Value = _busy ? null : "Check again",
                OnClick = _busy ? null : () => _ = CheckAsync()
            },
            new RowSpec
            {
                Text = "What the check sends to HONOR",
                Secondary = "Laptop model, BIOS and driver versions, Windows version and language. No serial number or personal data.",
                Glyph = "\uE72E",
                Kind = RowKind.Info
            }
        ]));

        if (_components is null)
            return sections;

        // Updates first: the only rows that need a decision.
        var pending = shown.Where(c => _updates.TryGetValue(c.Id, out var u) && u.IsUpdate && c.CurrentVersion != "0").ToList();
        if (pending.Count > 0)
            sections.Add(("Updates available", pending.Select(Row).ToList()));

        // HONOR's server lists only the drivers HONOR still publishes for this model.
        // The rest usually come through Windows Update or the device maker; the app
        // can't tell, so it only says they're not in HONOR's list. BIOS always
        // belongs with HONOR.
        var rest = installed.Where(c => !pending.Contains(c))
            .OrderBy(c => c.Id == BiosId ? 0 : 1).ThenBy(Category).ThenBy(c => Clean(TitleOf(c))).ToList();
        var fromHonor = rest.Where(c => _updates.ContainsKey(c.Id) || c.Id == BiosId).ToList();
        var fromWindows = rest.Where(c => !_updates.ContainsKey(c.Id) && c.Id != BiosId).ToList();
        if (_lastCheck is null)
        {
            if (rest.Count > 0)
                sections.Add(("Installed", rest.Select(Row).ToList()));
        }
        else
        {
            if (fromHonor.Count > 0)
                sections.Add(("Checked with HONOR", fromHonor.Select(Row).ToList()));
            if (fromWindows.Count > 0)
                sections.Add(("Not in HONOR's list (usually updated by Windows Update)", fromWindows.Select(Row).ToList()));
        }

        // Not installed, but HONOR offers a package: optional extras.
        var optional = shown.Where(c => c.CurrentVersion == "0" && _updates.ContainsKey(c.Id)).ToList();
        if (optional.Count > 0)
            sections.Add(("Available, not installed", optional.Select(Row).ToList()));

        sections.Add(("Backup for a clean reinstall",
        [
            new RowSpec
            {
                Text = "Back up drivers",
                Secondary = "Saves every installed driver and the panel's factory color calibration in one zip.",
                Glyph = "\uE896",
                Kind = RowKind.Action,
                Enabled = !_busy,
                OnClick = () => _ = ExportAsync()
            },
            new RowSpec
            {
                Text = "Restore drivers",
                Secondary = "Installs every driver from such a zip, for example after reinstalling Windows.",
                Glyph = "\uE8B5",
                Kind = RowKind.Action,
                Enabled = !_busy,
                OnClick = () => _ = ImportAsync()
            }
        ]));
        return sections;
    }

    private RowSpec Row(DriverComponent component)
    {
        _updates.TryGetValue(component.Id, out var update);
        var notInstalled = component.CurrentVersion == "0";
        var hasUpdate = update is { IsUpdate: true } && !notInstalled;
        var downloading = _downloadingId == component.Id;
        var canDownload = update is not null && !downloading && _downloadingId is null && (hasUpdate || notInstalled);
        var date = FormatDate(update?.ReleaseDate);

        string? badge = null;
        var tone = BadgeTone.Neutral;
        string? value = null;
        if (downloading)
        {
            value = _downloadText ?? "Downloading…";
        }
        else if (hasUpdate)
        {
            badge = "Update";
            tone = BadgeTone.Attention;
            value = update!.Version;
        }
        else if (notInstalled && update is not null)
        {
            badge = "Get";
            tone = BadgeTone.Attention;
            value = update.Version;
        }
        else if (update is not null)
        {
            badge = "Up to date";
            tone = BadgeTone.Success;
        }
        else if (component.Id == BiosId && _lastCheck is not null)
        {
            badge = "No update listed";
        }

        var secondary = $"{Category(component)} · " + (notInstalled ? "not installed" : component.CurrentVersion);
        if ((hasUpdate || notInstalled) && date.Length > 0)
            secondary += $" · new version from {date}";
        return new RowSpec
        {
            Text = Clean(TitleOf(component)),
            Secondary = secondary,
            Badge = badge,
            BadgeTone = tone,
            Value = value,
            Kind = canDownload ? RowKind.Action : RowKind.Info,
            OnClick = canDownload ? () => _ = DownloadAsync(update!) : null
        };
    }

    /// <summary>"Intel(R) Wi-Fi 6E AX211" → "Intel Wi-Fi 6E AX211".</summary>
    private static string Clean(string name)
    {
        foreach (var mark in new[] { "(R)", "(TM)", "(r)", "(tm)", "\u00AE", "\u2122" })
            name = name.Replace(mark, "");
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- Actions ----

    private async Task DownloadAsync(DriverUpdate update)
    {
        _downloadingId = update.Component.Id;
        _downloadText = "Downloading 0%";
        Notify();
        try
        {
            var shown = -1;
            var progress = new Progress<int>(percent =>
            {
                if (percent == shown)
                    return;   // the page redraws only when the number changes
                shown = percent;
                _downloadText = percent < 100 ? $"Downloading {percent}%" : "Checking file…";
                Notify();
            });
            await _service.DownloadAsync(update, progress, _stop);
        }
        catch (OperationCanceledException)
        {
            // Save dialog closed or page closed.
        }
        catch (Exception exception)
        {
            Log.Error($"Download of {update.Component.Name} {update.Version} failed", exception);
            CommonDialogs.Message(DialogOwner, exception.Message, "Drivers", CommonDialogs.Icon.Error);
        }
        finally
        {
            _downloadingId = null;
            _downloadText = null;
            Notify();
        }
    }

    private async Task ExportAsync()
    {
        if (CommonDialogs.AskForFile(Owner, save: true, "Back up drivers", "Zip archive (*.zip)|*.zip",
                $"{MachineProfile.ProductName}-drivers-{DateTime.Now:yyyy-MM-dd}.zip", "zip") is not { } zip)
            return;
        await RunBackupAsync("Backing up drivers…", stop => DriverBackup.BackupAsync(zip, stop),
            done => $"{done.Packages} driver packages are in\n{zip}", rereadDrivers: false);
    }

    private async Task ImportAsync()
    {
        if (CommonDialogs.AskForFile(Owner, save: false, "Restore drivers", "Driver backup or .inf (*.zip;*.inf)|*.zip;*.inf|All files (*.*)|*.*") is not { } source)
            return;
        await RunBackupAsync("Restoring drivers…", stop => DriverBackup.RestoreAsync(source, stop),
            done => $"{done.Packages} driver packages installed." + (done.NeedsRestart ? "\nRestart Windows to finish." : ""),
            rereadDrivers: true);
    }

    /// <summary>
    /// Runs a backup or restore with the page marked busy, reports the outcome, and after a
    /// restore reads the installed versions again and re-checks with HONOR.
    /// </summary>
    private async Task RunBackupAsync(string busyText, Func<CancellationToken, Task<DriverBackupResult>> work,
        Func<DriverBackupResult, string> describeSuccess, bool rereadDrivers)
    {
        if (_busy)
            return;
        var before = (_status, _statusIsWarning);
        _busy = true;
        SetStatus(busyText);
        DriverBackupResult? outcome = null;
        try
        {
            outcome = await work(_stop);
        }
        catch (OperationCanceledException)
        {
            // UAC declined or page closed.
        }
        catch (Exception exception)
        {
            Log.Error(busyText.TrimEnd('…') + " failed", exception);
            outcome = DriverBackupResult.Failed(exception.Message);
        }
        finally
        {
            _busy = false;
            SetStatus(before._status, before._statusIsWarning);
        }
        if (outcome is null)
            return;

        CommonDialogs.Message(DialogOwner, outcome.Done ? describeSuccess(outcome) : outcome.Problem ?? "Something went wrong.", "Drivers",
            outcome.Done ? CommonDialogs.Icon.Information : CommonDialogs.Icon.Error);
        if (outcome.Done && rereadDrivers && await TryAsync(() => _service.BuildDeviceListAsync(_stop), "Driver list unreadable after restore") is { } fresh)
        {
            _components = fresh;
            _updates = [];
            Notify();
            await CheckAsync();
        }
    }

    // ---- Names ----

    /// <summary>The section a driver is listed under; packages without a catalog entry use HONOR's category name.</summary>
    private static string Category(DriverComponent component)
        => DeviceInventory.CatalogEntry(component.Id)?.Group ?? component.DisplayName;

    /// <summary>The device's own name when Windows has one, else the component's generic name.</summary>
    private static string TitleOf(DriverComponent component)
        => string.IsNullOrWhiteSpace(component.DeviceName) ? component.DisplayName : component.DeviceName;

    /// <summary>A yyyy-MM-dd release date in the user's short date format; empty when there's none.</summary>
    private static string FormatDate(string? isoDate)
        => DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("d", CultureInfo.CurrentCulture)
            : "";

    private static string ReadSerialNumber()
    {
        try
        {
            return Wmi.FirstText("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber") ?? "—";
        }
        catch (WmiException exception)
        {
            Log.Error("Serial number unreadable", exception);
            return "—";
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
