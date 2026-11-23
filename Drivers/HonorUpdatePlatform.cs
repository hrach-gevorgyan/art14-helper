using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Art14Helper;

/// <summary>The one HTTP client all driver traffic goes through.</summary>
internal static class DriverHttp
{
    /// <summary>
    /// No system proxy: VPN and proxy tools often leave a dead 127.0.0.1 proxy behind,
    /// which turned every check into a timeout. Responses are compressed and
    /// connections reused, which matters for the many small catalog requests.
    /// </summary>
    internal static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { UseProxy = false };
        handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;
        handler.PooledConnectionLifetime = TimeSpan.FromSeconds(120);
        // Downloads are large; each request sets its own shorter limit where it matters.
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(600) };
    }

    /// <summary>Text of a JSON property whatever its JSON type, or null when absent.</summary>
    internal static string? Field(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Null ? null : value.ToString()
            : null;

    /// <summary>First present field of several alternative names.</summary>
    internal static string? Field(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.Field(name) is { } value)
                return value;
        return null;
    }

    /// <summary>A token that cancels with <paramref name="outer"/> or after <paramref name="limit"/>.</summary>
    internal static CancellationTokenSource Limit(CancellationToken outer, TimeSpan limit)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        source.CancelAfter(limit);
        return source;
    }
}

/// <summary>
/// HONOR's update platform (the service HONOR PC Manager uses): one POST with all
/// component versions, one answer with the packages it has. Fast when it knows the
/// model, but it doesn't know every model, hence the support site as fallback.
/// </summary>
internal static class HonorUpdatePlatform
{
    private const string Endpoint =
        "https://update.platform.hihonorcloud.com/hid_and_common/v2/CheckEx.action?latest=true&verType=true&defenceHijack=true";

    /// <summary>"Name:version;" for each component, the platform's "Dashboard" field.</summary>
    internal static string Dashboard(IEnumerable<DriverComponent> components)
    {
        var text = new StringBuilder();
        foreach (var component in components)
            text.Append(component.Name).Append(':').Append(component.CurrentVersion).Append(';');
        return text.ToString();
    }

    /// <summary>The request body. Written by hand, so no reflection-based serializer is needed.</summary>
    internal static byte[] BuildRequest(IReadOnlyList<DriverComponent> components, MachineProfile machine, string appVersion)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("components");
            foreach (var component in components)
            {
                json.WriteStartObject();
                json.WriteString("AppName", component.AppName ?? component.Name);
                json.WriteString("PackageName", component.Name);
                json.WriteString("PackageType", component.PackageType);
                json.WriteString("PackageVersionCode", component.CurrentVersion);
                json.WriteString("PackageVersionName", appVersion);
                json.WriteString("componentID", component.Id.ToString(CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartObject("rules");
            json.WriteString("C_version", machine.CVersion);
            json.WriteString("Dashboard", Dashboard(components));
            json.WriteString("DashboardFlash", "");
            json.WriteString("DeviceName", machine.DeviceName);
            json.WriteString("FirmWare", "");
            json.WriteString("Language", CultureInfo.CurrentUICulture.Name);
            json.WriteString("OS", Environment.OSVersion.VersionString);
            json.WriteString("deviceId", "");
            json.WriteString("udid", "");
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Packages in a platform answer, for components that were asked about. An answer
    /// with a status other than "0" has none. Entries without version or URL are skipped.
    /// </summary>
    internal static List<DriverUpdate> ParseAnswer(JsonElement answer, IReadOnlyList<DriverComponent> asked)
    {
        var packages = new List<DriverUpdate>();
        if (answer.Field("status") != "0"
            || !answer.TryGetProperty("components", out var list) || list.ValueKind != JsonValueKind.Array)
            return packages;

        foreach (var item in list.EnumerateArray())
        {
            if (!int.TryParse(item.Field("componentID"), CultureInfo.InvariantCulture, out var id))
                continue;
            var component = asked.FirstOrDefault(c => c.Id == id);
            var version = item.Field("version", "name");
            var url = item.Field("url");
            if (component is null || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url))
                continue;
            packages.Add(new DriverUpdate
            {
                Component = component,
                Version = version,
                DownloadBaseUrl = url,
                Sha256 = item.Field("sha256", "hash"),
                ReleaseDate = DriverVersions.IsoDate(item.Field("releaseDate", "updateTime", "date")),
                IsUpdate = DriverVersions.IsNewer(version, component.CurrentVersion)
            });
        }
        return packages;
    }

    /// <summary>
    /// Queries the platform with a 10-second budget and returns an empty list on any
    /// problem. The support site is the real fallback, so there's no point in waiting.
    /// </summary>
    internal static async Task<List<DriverUpdate>> QueryAsync(
        IReadOnlyList<DriverComponent> components, MachineProfile machine, CancellationToken stop)
    {
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
        try
        {
            using var limit = DriverHttp.Limit(stop, TimeSpan.FromSeconds(10));
            using var body = new ByteArrayContent(BuildRequest(components, machine, appVersion));
            body.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
            using var response = await DriverHttp.Client.PostAsync(Endpoint, body, limit.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            using var answer = await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token);
            return ParseAnswer(answer.RootElement, components);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            Log.Info("HONOR update platform did not answer in time; trying the support site");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            Log.Error("HONOR update platform request failed; trying the support site", exception);
        }
        return [];
    }
}
