using System.Text.RegularExpressions;

namespace Art14Helper;

/// <summary>
/// What identifies this laptop to HONOR: the device name and platform code HONOR's
/// BIOS writes to the registry, plus model, SKU and board names from SMBIOS (used to
/// find the laptop in the support site's product list).
/// </summary>
internal sealed record MachineProfile(
    string DeviceName,
    string CVersion,
    string Processor,
    int MemoryGb,
    IReadOnlyList<string> Identifiers)
{
    private const string BiosKey = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";

    internal static MachineProfile ReadFromSystem()
    {
        var names = new List<string>();
        void Note(string? value)
        {
            if (IsMeaningful(value) && !names.Contains(value!.Trim(), StringComparer.OrdinalIgnoreCase))
                names.Add(value.Trim());
        }

        using var honor = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\HONOR\BIOS");
        var deviceName = Convert.ToString(honor?.GetValue("DeviceTypeEx"));
        var cVersion = Convert.ToString(honor?.GetValue("CVersion"));
        Note(deviceName);
        Note(cVersion);

        string? model = null, sku = null, memory = null;
        Wmi.ForEach("SELECT Model, SystemFamily, SystemSKUNumber, TotalPhysicalMemory FROM Win32_ComputerSystem", row =>
        {
            model ??= row.Text("Model");
            sku ??= row.Text("SystemSKUNumber");
            memory ??= row.Text("TotalPhysicalMemory");
            Note(row.Text("Model"));
            Note(row.Text("SystemFamily"));
            Note(row.Text("SystemSKUNumber"));
        });
        Wmi.ForEach("SELECT Name, Version, SKUNumber FROM Win32_ComputerSystemProduct", row =>
        {
            Note(row.Text("Name"));
            Note(row.Text("Version"));
            Note(row.Text("SKUNumber"));
        });
        Wmi.ForEach("SELECT Product FROM Win32_BaseBoard", row => Note(row.Text("Product")));

        // Without HONOR's registry values, fall back to SMBIOS, then to the BIOS key.
        deviceName = FirstMeaningful(deviceName, model, BiosValue("SystemProductName")) ?? "HONOR";
        cVersion = FirstMeaningful(cVersion, sku, BiosValue("SystemSKU")) ?? "";

        var processor = Wmi.FirstText("SELECT Name FROM Win32_Processor", "Name") ?? "";
        var memoryGb = ulong.TryParse(memory, out var bytes) ? (int)Math.Round(bytes / (double)(1L << 30)) : 0;
        return new MachineProfile(deviceName, cVersion, processor, memoryGb, names);
    }

    private static string? FirstMeaningful(params string?[] candidates)
        => candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    internal static string? BiosValue(string name) => Convert.ToString(Microsoft.Win32.Registry.GetValue(BiosKey, name, ""));

    /// <summary>The laptop's model name from the BIOS (e.g. MRA-XXX), or "HONOR" when it has none.</summary>
    internal static string ProductName => BiosValue("SystemProductName") is { Length: > 0 } name ? name.Trim() : "HONOR";

    /// <summary>SMBIOS placeholders some boards leave in the fields.</summary>
    private static bool IsMeaningful(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Trim().Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
           && !value.Trim().Equals("Default string", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Finds this laptop in the support site's product list. The list only has product
/// descriptions, so each product is scored by how well its description names this
/// machine; only clear matches count.
/// </summary>
internal static class SupportProductMatcher
{
    internal sealed record Product(string OfferingCode, string Description);

    /// <summary>Upper-case letters and digits only: "ZQC-P" → "ZQCP".</summary>
    internal static string Compact(string text)
    {
        var chars = new char[text.Length];
        var length = 0;
        foreach (var c in text)
            if (char.IsAsciiLetterOrDigit(c))
                chars[length++] = char.ToUpperInvariant(c);
        return new string(chars, 0, length);
    }

    /// <summary>
    /// Model-like parts of a processor name: "Intel(R) Core(TM) Ultra X7 358H" → "358H",
    /// "i7-1360P" → "I71360P". Short or purely alphabetic/numeric words don't count.
    /// </summary>
    internal static List<string> ProcessorTokens(string processor)
    {
        var tokens = new List<string>();
        foreach (var word in Regex.Split(processor, "[^A-Za-z0-9-]+"))
        {
            var firstDigit = word.IndexOfAny("0123456789".ToCharArray());
            if (firstDigit < 0 || word.Length - firstDigit - 1 < 2)
                continue;
            var token = Compact(word);
            if (token.Length >= 4 && token.Any(char.IsAsciiLetter) && token.Any(char.IsAsciiDigit) && !tokens.Contains(token))
                tokens.Add(token);
        }
        return tokens;
    }

    /// <summary>
    /// How well a product description names the machine. A model or board code
    /// ("ZQC-P", "BRN-F58") counts more than a marketing word; the processor and
    /// memory size add to an existing match. 0 when no identifier appears at all.
    /// </summary>
    internal static int Score(string description, MachineProfile machine)
    {
        var text = Compact(description);
        var best = 0;
        foreach (var identifier in machine.Identifiers)
        {
            var key = Compact(identifier);
            if (key.Length < 4 || !text.Contains(key, StringComparison.Ordinal))
                continue;
            var looksLikeCode = !identifier.Any(char.IsWhiteSpace) && (identifier.Contains('-') || identifier.Any(char.IsAsciiDigit));
            best = Math.Max(best, (looksLikeCode ? 100 : 70) + Math.Min(key.Length, 20));
        }
        if (best == 0)
            return 0;
        best += 30 * ProcessorTokens(machine.Processor).Count(token => text.Contains(token, StringComparison.Ordinal));
        if (machine.MemoryGb > 0 && Regex.IsMatch(description, $@"\b{machine.MemoryGb}\s*GB\b", RegexOptions.IgnoreCase))
            best += 5;
        return best;
    }

    /// <summary>
    /// Offering codes of the best-scoring products. Nothing when no product reaches a
    /// code-level match, or when several weak matches tie although the processor could
    /// have told them apart (picking one would be a guess).
    /// </summary>
    internal static List<string> Pick(IEnumerable<Product> products, MachineProfile machine)
    {
        var scored = products.Select(p => (p.OfferingCode, Score: Score(p.Description, machine)))
            .Where(p => p.Score >= 100)
            .ToList();
        if (scored.Count == 0)
            return [];
        var top = scored.Max(p => p.Score);
        var winners = scored.Where(p => p.Score == top).ToList();
        if (top < 130 && winners.Count > 1 && ProcessorTokens(machine.Processor).Count > 0)
            return [];
        return winners.Select(p => p.OfferingCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Offering codes that need no product-tree lookup (that lookup is slow and can
    /// stall). The one entry is a MagicBook Pro 14 2026 with an Ultra X7 358H (board
    /// ZQC-P). The check uses board and CPU, not the CVersion code: C233 is shared with
    /// other 2026 models, which would get the wrong BIOS and audio packages.
    /// </summary>
    internal static List<string> Known(MachineProfile machine)
    {
        var names = machine.Identifiers.Append(machine.DeviceName).Select(Compact).ToList();
        var isZhuqueC = names.Contains("ZQCP") || names.Contains("ZHUQUEC");
        return isZhuqueC && ProcessorTokens(machine.Processor).Contains("358H") ? ["OFFE00461151"] : [];
    }
}
