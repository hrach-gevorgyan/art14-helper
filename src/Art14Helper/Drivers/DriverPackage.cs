using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Art14Helper;

/// <summary>Package URLs: HTTPS only, and which hosts count as HONOR's own.</summary>
internal static class DownloadUrls
{
    private static readonly string[] HonorDomains = ["hihonor.com", "honor.com", "hihonorcloud.com"];

    /// <summary>
    /// True for the domain and its subdomains only, so a look-alike such as
    /// "evilhonor.com" doesn't pass for "honor.com".
    /// </summary>
    internal static bool IsHostIn(string host, string domain)
    {
        if (host.Length == domain.Length)
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase);
        return host.Length > domain.Length
               && host[^(domain.Length + 1)] == '.'
               && host.EndsWith(domain, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsHonorHost(Uri url) => HonorDomains.Any(domain => IsHostIn(url.Host, domain));

    /// <summary>
    /// The URL to download a package from: always HTTPS on the default port. The
    /// update platform gives a folder on its CDN; the full package is
    /// "full/update.zip" inside it.
    /// </summary>
    internal static Uri ForPackage(string baseUrl)
    {
        var url = new UriBuilder(baseUrl) { Scheme = Uri.UriSchemeHttps, Port = -1 };
        if (Path.GetExtension(url.Path).Length == 0 && IsHostIn(url.Host, "hihonorcloud.com"))
            url.Path = url.Path.TrimEnd('/') + "/full/update.zip";
        return url.Uri;
    }

    /// <summary>
    /// The package's real file name. Support-site links end in a descriptor (gzip'ed
    /// JSON, base64url) whose "fileName" has it; other links end in the file name
    /// itself. Null when neither gives a name with an extension.
    /// </summary>
    internal static string? OriginalFileName(Uri url)
    {
        var last = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath));
        var stem = Path.GetFileNameWithoutExtension(last);
        if (stem.Length is > 0 and <= 8192 && DecodeDescriptorName(stem) is { } fromDescriptor)
            return FileNames.Safe(Path.GetFileName(fromDescriptor));
        return Path.HasExtension(last) ? FileNames.Safe(last) : null;
    }

    private static string? DecodeDescriptorName(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var bytes = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, bytes, out var length) || length < 2
            || bytes[0] != 0x1F || bytes[1] != 0x8B) // gzip magic
            return null;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(bytes, 0, length), CompressionMode.Decompress);
            using var descriptor = JsonDocument.Parse(gzip);
            return descriptor.RootElement.Field("fileName") is { } name && !string.IsNullOrWhiteSpace(name) ? name : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return null;
        }
    }
}

internal static class FileNames
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>A usable file name: invalid characters become '_'; empty becomes "HONOR-driver".</summary>
    internal static string Safe(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Invalid, chars[i]) >= 0)
                chars[i] = '_';
        var name = new string(chars).Trim();
        return name.Length == 0 ? "HONOR-driver" : name;
    }
}

/// <summary>
/// Downloads one package and proves it is what it claims: checksum when the server
/// gave one, archives unpacked without leaving their folder, and the installer's
/// Authenticode signature checked (from a non-HONOR host it must also be signed by
/// HONOR or Huawei).
/// </summary>
internal static class DriverPackage
{
    /// <summary>Extension the downloaded file is stored under; ".download" when the URL has none.</summary>
    internal static string DownloadExtension(Uri url)
    {
        var extension = Path.GetExtension(url.AbsolutePath);
        return extension.Length is 0 or > 8 ? ".download" : extension;
    }

    /// <summary>
    /// Extension the user saves under. Archives are unpacked to their installer, so
    /// those are offered as ".exe".
    /// </summary>
    internal static string SaveExtension(Uri url)
    {
        var extension = DownloadExtension(url);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".download", StringComparison.OrdinalIgnoreCase)
            ? ".exe"
            : extension;
    }

    /// <summary>Where a package is downloaded and unpacked; the caller deletes it once the result is saved.</summary>
    /// <remarks>In the app's own folder (<see cref="AppFolders"/>), where only administrators can
    /// write, so nothing can swap a verified file before it is copied out.</remarks>
    internal static string WorkFolder(DriverUpdate update)
        => Path.Combine(AppFolders.Root, "Work", "DriverUpdates", $"{update.Component.Id}_{FileNames.Safe(update.Version)}");

    /// <summary>
    /// Downloads and verifies; returns the path of the file to hand to the user: the
    /// installer when an archive holds just that, otherwise the archive itself, since the
    /// installer needs the files next to it.
    /// </summary>
    internal static async Task<string> FetchAsync(DriverUpdate update, IProgress<int>? progress, CancellationToken token)
    {
        var url = DownloadUrls.ForPackage(update.DownloadBaseUrl);
        var workFolder = WorkFolder(update);
        Directory.CreateDirectory(workFolder);
        var downloaded = Path.Combine(workFolder, "package" + DownloadExtension(url));
        var unpacked = Path.Combine(workFolder, "package");
        if (Directory.Exists(unpacked))
            Directory.Delete(unpacked, recursive: true);

        Log.Info($"Downloading driver {update.Component.Name} {update.Version} from {url.Host}");
        var finalUrl = await DownloadAsync(url, downloaded, progress, token);

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var file = File.OpenRead(downloaded);
            var actual = await SHA256.HashDataAsync(file, token);
            if (!Convert.ToHexString(actual).Equals(update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw Shown(m => new InvalidDataException(m), N("The download is damaged: its SHA-256 differs from the one HONOR published."));
        }

        // Judged on where the file really came from too: a redirect may lead elsewhere.
        var fromHonor = DownloadUrls.IsHonorHost(url) && DownloadUrls.IsHonorHost(finalUrl);
        var result = downloaded;
        var programs = new List<string>();
        var code = new List<string>();   // everything Windows would run or load: .exe, .dll, .sys
        if (StartsWithZipMagic(downloaded))
        {
            UnpackAll(downloaded, unpacked);
            var files = Directory.GetFiles(unpacked, "*", SearchOption.AllDirectories);
            programs.AddRange(files.Where(file => Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)));
            code.AddRange(files.Where(file => Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".dll" or ".sys"));
            if (files.Length == 1 && programs.Count == 1)
                result = programs[0];
            else
            {
                // Drivers (.inf, .sys, .cat) or a setup with its own files: keep them together.
                result = Path.ChangeExtension(downloaded, ".zip");
                if (result != downloaded)
                    File.Move(downloaded, result, overwrite: true);
            }
        }
        else if (Path.GetExtension(result).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            programs.Add(result);

        // The installer (the largest program) must always be signed. From HONOR's own server the
        // rest of an archive comes with it over HTTPS. From anywhere else without a published
        // checksum, every program and driver file in it must carry a valid signature too.
        if (programs.OrderByDescending(program => new FileInfo(program).Length).FirstOrDefault() is { } installer)
            Authenticode.Verify(installer, requireHonorSigner: !fromHonor);
        if (!fromHonor && string.IsNullOrWhiteSpace(update.Sha256))
            foreach (var file in code)
                Authenticode.Verify(file, requireHonorSigner: false);
        if (programs.Count == 0 && !fromHonor && string.IsNullOrWhiteSpace(update.Sha256))
            throw Shown(m => new InvalidDataException(m), N("This download has neither a signature nor a checksum to check, so it wasn't saved."));
        return result;
    }

    /// <returns>The address the file finally came from, after any redirects.</returns>
    private static async Task<Uri> DownloadAsync(Uri url, string path, IProgress<int>? progress, CancellationToken token)
    {
        using var response = await DriverHttp.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new HttpRequestException(
                T("HONOR has removed this file from its server. Check for updates again to get the current list."), null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        var expected = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var chunk = new byte[1 << 16];
        long received = 0;
        for (int count; (count = await source.ReadAsync(chunk, token)) > 0;)
        {
            await target.WriteAsync(chunk.AsMemory(0, count), token);
            received += count;
            if (expected > 0)
                progress?.Report((int)Math.Min(100, received * 100 / expected));
        }
        return response.RequestMessage?.RequestUri ?? url;
    }

    private static bool StartsWithZipMagic(string path)
    {
        using var file = File.OpenRead(path);
        return file.ReadByte() == 'P' && file.ReadByte() == 'K';
    }

    /// <summary>Unpacks an archive and the archives inside it, down to four levels.</summary>
    internal static void UnpackAll(string archive, string folder)
    {
        UnpackInside(archive, folder);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var level = 0; level < 4; level++)
        {
            var inner = Directory.EnumerateFiles(folder, "*.zip", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(done.Add)
                .ToList();
            if (inner.Count == 0)
                return;
            foreach (var nested in inner)
                UnpackInside(nested, Path.Combine(Path.GetDirectoryName(nested)!, Path.GetFileNameWithoutExtension(nested)));
        }
    }

    /// <summary>The most one archive may unpack to; HONOR's largest packages are under 2 GB.</summary>
    internal const long MaxUnpackedBytes = 8L << 30;
    internal const int MaxEntries = 50_000;

    /// <summary>
    /// Unpacks one archive, refusing any entry that would land outside <paramref name="folder"/>,
    /// and archives that would unpack to more than the limits or than the drive has room for
    /// (this runs before the signature check, on the system drive).
    /// </summary>
    internal static void UnpackInside(string archive, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(archive);
        var total = zip.Entries.Sum(entry => entry.Length);
        var room = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace - (1L << 30);
        if (zip.Entries.Count > MaxEntries || total > MaxUnpackedBytes || total > room)
            throw Shown(m => new InvalidDataException(m), N("The package is too large to unpack ({0} files, {1} MB)."), zip.Entries.Count, total / 1048576);
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw Shown(m => new InvalidDataException(m), N("The driver package tries to write outside its folder ({0})."), entry.FullName);
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target); // a folder entry
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
