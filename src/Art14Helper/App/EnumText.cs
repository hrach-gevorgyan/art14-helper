namespace Art14Helper;

/// <summary>Reads a choice stored or passed as text.</summary>
internal static class EnumText
{
    /// <summary>
    /// One name only, ignoring case. Enum.TryParse alone also takes numbers ("7") and lists
    /// ("Auto, Dim", combined bitwise), even ones that name no value, and those would reach
    /// the BIOS unchecked.
    /// </summary>
    internal static bool TryParse<T>(ReadOnlySpan<char> text, out T value) where T : struct, Enum
    {
        var trimmed = text.Trim();
        if (trimmed.IsEmpty || !char.IsLetter(trimmed[0]) || trimmed.Contains(',')
            || !Enum.TryParse(trimmed, ignoreCase: true, out value) || !Enum.IsDefined(value))
        {
            value = default;
            return false;
        }
        return true;
    }
}
