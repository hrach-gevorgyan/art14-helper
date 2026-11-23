using System.Text.Json;

namespace Art14Helper;

/// <summary>
/// The driver downloads of HONOR's support website. Slower than the update platform
/// but it lists every model: find the laptop's offering code in the product tree, read
/// the offering's driver categories, then the packages of each category. Answers are
/// JSONP.
/// </summary>
internal static class HonorSupportSite
{
    /// <summary>A regional API host and the locale parameters it wants.</summary>
    internal sealed record Region(string Host, string Area, string Language, string Site, bool ShortQuery = false)
    {
        internal string Url(string path) => $"https://{Host}/ccpcmd/services/dispatch/secured/CCPC/EN{path}";
    }

    /// <summary>Product tree lookups try these in order; the first also serves the catalogs.</summary>
    private static readonly Region[] Regions =
    [
        new("selfservice-ap.honor.com", "UZ", "ru", "ru_UZ_X"),
        new("selfservice-eu.honor.com", "GB", "en", "en_GB_X"),
        new("selfservice-cn.honor.com", "CN", "zh_cn", "zh_CN_X")
    ];

    /// <summary>Package lists come from the Chinese host, which has the complete set.</summary>
    private static readonly Region PackageRegion = new("selfservice-cn.honor.com", "CN", "zh-cn", "", ShortQuery: true);

    /// <summary>Product id of the "Laptops" branch of the product tree.</summary>
    private const string LaptopBranch = "CMCG10000120";

    private const int ParallelRequests = 6;

    internal sealed record Answer(List<DriverComponent> NewComponents, List<DriverUpdate> Packages, bool Reached);

    private sealed record Package(string Title, string Version, string Url, string? Date, string CategoryId, string CategoryName);

    /// <summary>
    /// Packages the support site has for this machine, mapped onto <paramref name="asked"/>
    /// where possible; packages for components not asked about come with a new
    /// (not installed) component. <see cref="Answer.Reached"/> is false when a request
    /// failed, so an empty answer doesn't mean "nothing available".
    /// </summary>
    internal static async Task<Answer> QueryAsync(
        MachineProfile machine, IReadOnlyList<DriverComponent> asked, CancellationToken token)
    {
        var known = SupportProductMatcher.Known(machine);
        // A known offering skips the product tree; the catalog requests then always hit
        // the first host, so other regions add nothing. Instead that host is asked twice,
        // because it occasionally stalls.
        Region[] plan = known.Count > 0 ? [Regions[0], Regions[0]] : Regions;
        var failed = false;
        for (var step = 0; step < plan.Length; step++)
        {
            var region = plan[step];
            var retrySameRegion = step + 1 < plan.Length && plan[step + 1] == region;
            using var limit = DriverHttp.Limit(token, TimeSpan.FromSeconds(30));
            try
            {
                var offerings = known.Count > 0 ? known : await FindOfferingsAsync(region, machine, limit.Token);
                if (offerings.Count == 0)
                    continue;
                var answer = await ReadPackagesAsync(offerings, asked, limit.Token);
                if (answer.Packages.Count > 0)
                {
                    Log.Info($"HONOR support site: {answer.Packages.Count} package(s) for {string.Join(',', offerings)}");
                    return answer;
                }
                if (retrySameRegion)
                    step++;
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested && !token.IsCancellationRequested)
            {
                Log.Info($"HONOR support site ({region.Area}) did not answer in time" + (retrySameRegion ? "; trying again" : ""));
                failed |= !retrySameRegion;
            }
            catch (Exception exception) when (retrySameRegion && exception is HttpRequestException or IOException)
            {
                Log.Info($"HONOR support site ({region.Area}) failed; trying again: {exception.Message}");
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException)
            {
                Log.Error($"HONOR support site ({region.Area}) failed", exception);
                failed = true;
                if (retrySameRegion)
                    step++;
            }
        }
        Log.Info($"HONOR support site has nothing for {machine.DeviceName}/{machine.CVersion}");
        return new([], [], !failed);
    }

    // ---- Product tree ----

    private static async Task<List<string>> FindOfferingsAsync(Region region, MachineProfile machine, CancellationToken token)
    {
        using var series = await GetAsync(region, "/ccpc/queryNewCommodityList/1000",
            [("productLevel", "lv3"), ("productId", LaptopBranch)], token);
        var seriesIds = ProductList(series.RootElement)
            .Select(item => item.Field("productId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var products = await InParallel(seriesIds, async id =>
        {
            using var models = await GetAsync(region, "/ccpc/queryNewCommodityList/1000",
                [("productLevel", "lv5"), ("productId", id)], token);
            return ProductList(models.RootElement)
                .Select(item => new SupportProductMatcher.Product(
                    item.Field("offeringCode", "productId") ?? "",
                    string.Join(' ', new[] { "displayName", "productName", "backName", "keyword" }
                        .Select(name => item.Field(name)).Where(text => !string.IsNullOrWhiteSpace(text)))))
                .Where(product => product.OfferingCode.Length > 0)
                .ToList();
        }, token);
        return SupportProductMatcher.Pick(products.SelectMany(list => list), machine);
    }

    private static IEnumerable<JsonElement> ProductList(JsonElement root)
        => root.Field("responseCode") == "200"
           && root.TryGetProperty("responseData", out var data)
           && data.TryGetProperty("productList", out var list)
           && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : [];

    // ---- Driver catalogs ----

    private static async Task<Answer> ReadPackagesAsync(
        IReadOnlyList<string> offerings, IReadOnlyList<DriverComponent> asked, CancellationToken token)
    {
        var newComponents = new Dictionary<int, DriverComponent>();
        var packages = new List<DriverUpdate>();
        foreach (var offering in offerings)
        {
            using var catalog = await GetAsync(Regions[0], "/knowledgeBase/getKnowCatalog/1000",
                [("knowTypeId", "63"), ("orderType", "1"), ("page", "1"), ("pageSize", "100"),
                 ("offeringCode", offering), ("cflag", "1")], token);
            var categories = List(catalog.RootElement, "catalogList")
                .Select(item => (Id: item.Field("knowTypeId") ?? "", Name: item.Field("knowTypeName") ?? ""))
                .Where(category => category.Id.Length > 0)
                .ToList();

            var perCategory = await InParallel(categories, async category =>
            {
                using var policy = await GetAsync(PackageRegion, "/knowledgeBase/getServicePolicy/1000",
                    [("knowTypeId", category.Id), ("orderType", "1"), ("page", "1"), ("pageSize", "999"),
                     ("offeringCode", offering), ("driverDown", "true"), ("cflag", "1")], token);
                return List(policy.RootElement, "detailList")
                    .Select(item => new Package(
                        item.Field("resourceTitle") ?? category.Name,
                        DriverVersions.FromSupportText(item.Field("versionNumber") ?? ""),
                        item.Field("downloadUrl") ?? "",
                        item.Field("updateTime"),
                        category.Id, category.Name))
                    .Where(p => !string.IsNullOrWhiteSpace(p.Version) && !string.IsNullOrWhiteSpace(p.Url))
                    .ToList();
            }, token);

            foreach (var package in perCategory.SelectMany(list => list))
            {
                var title = CleanTitle(package.Title, package.Version);
                var id = ComponentIdFor(package.Title, package.CategoryId, package.CategoryName);
                if (id == 0)
                    id = SyntheticId(package.CategoryId + "|" + package.Title);
                var component = asked.FirstOrDefault(c => c.Id == id);
                if (component is null && !newComponents.TryGetValue(id, out component))
                {
                    var entry = DeviceInventory.CatalogEntry(id);
                    component = new DriverComponent
                    {
                        Id = id,
                        Name = entry?.Name ?? package.Title,
                        DisplayName = entry?.DisplayName ?? package.CategoryName,
                        AppName = entry?.AppName,
                        DeviceName = title
                    };
                    newComponents[id] = component;
                }
                packages.Add(new DriverUpdate
                {
                    Component = component,
                    Version = package.Version,
                    DownloadBaseUrl = package.Url,
                    ReleaseDate = DriverVersions.IsoDate(package.Date),
                    IsUpdate = DriverVersions.IsNewer(package.Version, component.CurrentVersion),
                    PackageTitle = title
                });
            }
        }
        return new(newComponents.Values.ToList(), packages, true);
    }

    private static IEnumerable<JsonElement> List(JsonElement root, string name)
        => root.TryGetProperty("responseData", out var data)
           && data.TryGetProperty(name, out var list)
           && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : [];

    /// <summary>Package title keywords → component id, checked in this order.</summary>
    private static readonly (string Keyword, int Id)[] TitleKeywords =
    [
        ("Camera", 76), ("ISST", 65), ("IntelME", 2), ("Monitor", 41), ("MEP", 78), ("SerialIO", 4),
        ("VHID", 88), ("DPTF", 6), ("PPM", 55), ("TXT", 56), ("NPU", 74), ("NFC", 52), ("iPMT", 73),
        ("VDisplay", 87), ("WDT", 12)
    ];

    /// <summary>Support-site category id → component id, for titles without a keyword.</summary>
    private static readonly Dictionary<string, int> CategoryIds = new()
    {
        ["631"] = 1, ["633"] = 3, ["634"] = 15, ["635"] = 16, ["637"] = 18, ["639"] = 14, ["6310"] = DeviceInventory.BiosId
    };

    /// <summary>The HONOR component a support-site package belongs to, or 0 when unknown.</summary>
    internal static int ComponentIdFor(string title, string categoryId, string categoryName)
    {
        var text = title + " " + categoryName;
        foreach (var (keyword, id) in TitleKeywords)
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return id;
        return CategoryIds.GetValueOrDefault(categoryId);
    }

    /// <summary>
    /// A stable id (1000 and up, away from HONOR's numbers) for a package that matches
    /// no known component, so it keeps its row between checks. FNV-1a over the
    /// upper-cased text.
    /// </summary>
    internal static int SyntheticId(string text)
    {
        const uint offsetBasis = 2166136261, prime = 16777619;
        var hash = offsetBasis;
        foreach (var c in text)
            hash = unchecked((hash ^ char.ToUpperInvariant(c)) * prime);
        return 1000 + (int)(hash % 1_000_000);
    }

    /// <summary>"Touchpad_1.2.3_Firmware" → "Touchpad": no "Firmware" suffix, no trailing version.</summary>
    internal static string CleanTitle(string title, string version)
    {
        static string Without(string text, string suffix)
            => suffix.Length > 0 && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? text[..^suffix.Length] : text;

        char[] separators = [' ', '\t', '_'];
        var name = Without(title.TrimEnd(), "Firmware").Trim(separators);
        return Without(name, version).Trim(separators);
    }

    // ---- Transport ----

    /// <summary>Runs <paramref name="work"/> for every item, at most <see cref="ParallelRequests"/> at a time.</summary>
    private static async Task<List<TResult>> InParallel<TItem, TResult>(
        IReadOnlyList<TItem> items, Func<TItem, Task<TResult>> work, CancellationToken token)
    {
        var results = new TResult[items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = ParallelRequests, CancellationToken = token },
            async (index, _) => results[index] = await work(items[index]));
        return [.. results];
    }

    private static async Task<JsonDocument> GetAsync(
        Region region, string path, (string Name, string Value)[] parameters, CancellationToken token)
    {
        var query = new List<(string, string)>(parameters) { ("areaCode", region.Area), ("langCode", region.Language) };
        if (!region.ShortQuery)
            query.AddRange([("channelCode", "HONOR"), ("countryCode", region.Area), ("country", region.Area),
                ("language", region.Language), ("siteCode", region.Site)]);
        query.Add(("jsonp", "callback"));
        var url = region.Url(path) + "?" + string.Join('&',
            query.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
        return JsonDocument.Parse(UnwrapJsonp(await DriverHttp.Client.GetStringAsync(url, token)));
    }

    /// <summary>"callback({...})" → "{...}".</summary>
    internal static string UnwrapJsonp(string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
            throw new InvalidDataException("HONOR's support site sent an answer that isn't JSONP.");
        return text[(open + 1)..close];
    }
}
