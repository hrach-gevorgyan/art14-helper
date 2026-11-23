using System.Globalization;

namespace Art14Helper;

/// <summary>
/// Compares versions from Windows ("32.0.101.6000"), HONOR's update server and the
/// support site, whose fields can be prose ("WLAN 23.60.0.5 for Windows 11 24H2") or a
/// build date ("20261003"). Only the runs of digits matter.
/// </summary>
internal static class DriverVersions
{
    /// <summary>Orders versions by their digit groups only (missing groups count as 0).</summary>
    internal static readonly IComparer<string> ByNumbers =
        Comparer<string>.Create((left, right) => CompareNumbers(Numbers(left), Numbers(right)));

    /// <summary>Every run of digits in <paramref name="text"/>, as numbers.</summary>
    internal static List<long> Numbers(string? text)
    {
        var numbers = new List<long>();
        if (text is null)
            return numbers;
        var position = 0;
        while (position < text.Length)
        {
            if (!char.IsAsciiDigit(text[position]))
            {
                position++;
                continue;
            }
            var start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
                position++;
            // A run too long for a long (never seen in practice) saturates instead of failing.
            numbers.Add(long.TryParse(text.AsSpan(start, position - start), NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) ? value : long.MaxValue);
        }
        return numbers;
    }

    /// <summary>
    /// Compares by digit groups; "1.2" equals "1.2.0". When one side has no digits at
    /// all, falls back to a case-insensitive text comparison.
    /// </summary>
    internal static int Compare(string left, string right)
    {
        var a = Numbers(left);
        var b = Numbers(right);
        var result = CompareNumbers(a, b);
        if (result != 0 || a.Count > 0 && b.Count > 0)
            return result;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareNumbers(IReadOnlyList<long> a, IReadOnlyList<long> b)
    {
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var result = (i < a.Count ? a[i] : 0).CompareTo(i < b.Count ? b[i] : 0);
            if (result != 0)
                return result;
        }
        return 0;
    }

    /// <summary>"20260619": an eight-digit build date starting with 20.</summary>
    internal static bool IsBuildDate(string value)
        => value.Length == 8 && value.StartsWith("20", StringComparison.Ordinal) && value.All(char.IsAsciiDigit);

    /// <summary>
    /// True when the two versions use the same kind of numbering, so comparing them
    /// means something. False for a missing driver, a date code next to a dotted
    /// version, or a short number against a long one ("3.1" against "30.100.2405.44"):
    /// such pairs would announce updates that aren't.
    /// </summary>
    internal static bool AreComparable(string offered, string installed)
    {
        if (string.IsNullOrWhiteSpace(offered) || string.IsNullOrWhiteSpace(installed)
            || installed == DriverComponent.NotInstalled
            || IsBuildDate(offered) != IsBuildDate(installed))
            return false;
        var offeredGroups = Numbers(offered).Count;
        var installedGroups = Numbers(installed).Count;
        return offeredGroups > 0 && installedGroups > 0 && Math.Abs(offeredGroups - installedGroups) <= 1;
    }

    /// <summary>An honest "newer than installed": comparable and greater.</summary>
    internal static bool IsNewer(string offered, string installed)
        => AreComparable(offered, installed) && Compare(offered, installed) > 0;

    /// <summary>
    /// The version inside a support-site version field or title. A title can hold more
    /// than one dotted number, and the one with the most parts is the driver version
    /// (the others are Windows or revision numbers). Without a dotted number, the last
    /// long digit run (a build date) is used.
    /// </summary>
    internal static string FromSupportText(string text)
    {
        string? bestDotted = null;
        var bestDots = 0;
        string? lastLongRun = null;
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                i++;
                continue;
            }
            var start = i;
            var dots = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
            var digitsOnlyLength = i - start;
            // Extend over ".digits" groups; a trailing dot is not part of the version.
            while (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                dots++;
            }
            if (dots > bestDots)
            {
                bestDots = dots;
                bestDotted = text[start..i];
            }
            if (dots == 0 && digitsOnlyLength >= 6)
                lastLongRun = text[start..i];
        }
        return bestDotted ?? lastLongRun ?? text.Trim();
    }

    /// <summary>A date in any common format as yyyy-MM-dd, or null.</summary>
    internal static string? IsoDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var moment)
            ? DateOnly.FromDateTime(moment).ToString("O", CultureInfo.InvariantCulture)   // ISO 8601: yyyy-MM-dd
            : null;
}
