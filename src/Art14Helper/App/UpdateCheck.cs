using System.Net;
using System.Text.Json;

namespace Art14Helper;

/// <summary>A published release: its version and the page it's on.</summary>
internal sealed record ReleaseInfo(Version Version, string Page);

/// <summary>
/// The optional update notice: off unless the user turns it on (Settings > System). Then, at
/// most once a week, it asks GitHub for the latest release and, when it's newer, says so
/// once with a popup and keeps a tray menu entry that opens the release page.
/// </summary>
/// <remarks>
/// It never downloads or installs anything (the app has no self-updater): the user reads the
/// release and runs the new exe themselves. The request carries nothing but the app's name
/// and version as its user agent; GitHub sees the IP address, as with any web request. It
/// runs at start and after wake, when a week has passed, never on a timer of its own.
/// </remarks>
internal static class UpdateCheck
{
    private const string Repository = "hrach-gevorgyan/art14-helper";
    internal const string LatestApi = $"https://api.github.com/repos/{Repository}/releases/latest";
    internal const string ReleasesPage = $"https://github.com/{Repository}/releases/latest";
    private const string PagePrefix = $"https://github.com/{Repository}/releases/";

    internal static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,   // as for the driver checks: a dead proxy left by a VPN tool would make every check time out
        AutomaticDecompression = DecompressionMethods.All
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    private static int _running;

    /// <summary>A newer release found this run, or null.</summary>
    internal static ReleaseInfo? Available { get; private set; }

    /// <summary>Whether a week has passed since the last check (or the clock went backwards).</summary>
    internal static bool IsDue(DateTime? lastUtc, DateTime nowUtc)
        => lastUtc is not { } last || nowUtc - last >= Interval || last > nowUtc;

    /// <summary>
    /// GitHub's answer for the latest release: its version from the tag ("v1.2.0") and its page.
    /// Null for a draft, a prerelease, or anything unexpected. The page must be on this
    /// repository's releases; otherwise the general releases page is used.
    /// </summary>
    internal static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True
                || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagText
                || !Version.TryParse(tagText.TrimStart('v', 'V'), out var version))
                return null;
            var page = root.TryGetProperty("html_url", out var url) && url.GetString() is { } link
                       && link.StartsWith(PagePrefix, StringComparison.Ordinal) && Uri.IsWellFormedUriString(link, UriKind.Absolute)
                ? link
                : ReleasesPage;
            return new ReleaseInfo(version, page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="available"/> is newer than the running version (as X.Y.Z).</summary>
    internal static bool IsNewer(Version available, string running)
        => Version.TryParse(running, out var current) && Normalize(available) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>
    /// Checks when the notice is on and a week has passed (or <paramref name="now"/> asks for
    /// it at once, right after turning it on). True when a newer release was found that
    /// hasn't been announced before, so the caller shows the popup once per version.
    /// </summary>
    internal static async Task<bool> CheckAsync(bool now = false)
    {
        if (!AppSettings.UpdateCheck || !now && !IsDue(AppSettings.UpdateCheckedAt, DateTime.UtcNow)
            || Interlocked.Exchange(ref _running, 1) != 0)
            return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.UserAgent.ParseAdd($"Art14Helper/{AppInfo.Version}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            AppSettings.UpdateCheckedAt = DateTime.UtcNow;   // an answer came, even "none yet": wait a week
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"Update check: GitHub answered {(int)response.StatusCode}");
                return false;
            }
            if (Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) is not { } latest)
                return false;
            if (!IsNewer(latest.Version, AppInfo.Version))
            {
                Available = null;
                Log.Info($"Update check: {AppInfo.Version} is the latest");
                return false;
            }
            Available = latest;
            var text = latest.Version.ToString(3);
            Log.Info($"Update check: {text} is available");
            if (AppSettings.UpdateAnnounced == text)
                return false;
            AppSettings.UpdateAnnounced = text;
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Update check: no answer ({exception.GetType().Name})");   // offline is normal; try again next time
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary><see cref="CheckAsync"/>, then the popup when a version is announced for the first time. Never throws.</summary>
    internal static async Task CheckAndAnnounceAsync(bool now = false)
    {
        try
        {
            if (await CheckAsync(now).ConfigureAwait(false) && Available is { } release)
                Popup.Show(PopupIcon.Info, $"Art14 Helper {release.Version.ToString(3)} is out", "See the tray menu");
        }
        catch (Exception exception)
        {
            Log.Error("Update check failed", exception);
        }
    }

    /// <summary>The settings switch: on checks at once; off forgets a found version.</summary>
    internal static void Toggle()
    {
        AppSettings.UpdateCheck = !AppSettings.UpdateCheck;
        if (AppSettings.UpdateCheck)
            _ = Task.Run(() => CheckAndAnnounceAsync(now: true));
        else
            Available = null;
    }

    /// <summary>Opens the release page in the user's browser.</summary>
    internal static void OpenPage() => Shell.Open(Available?.Page ?? ReleasesPage);
}
