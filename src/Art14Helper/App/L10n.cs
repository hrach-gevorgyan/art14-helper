using System.Globalization;
using System.Text.Json;

namespace Art14Helper;

/// <summary>A language the app's own text can show in.</summary>
internal enum AppLanguage
{
    /// <summary>Windows' display language when the app has it, otherwise English.</summary>
    Auto,
    English,
    Russian,
    ChineseSimplified
}

/// <summary>
/// The app's text in the user's language. Every text the user sees is written in English in
/// the code and goes through <see cref="T(string)"/>, which looks it up in the table for the
/// chosen language (an embedded <c>Translations/&lt;code&gt;.json</c>: English text → translation)
/// and falls back to the English when a line has no translation yet.
/// </summary>
/// <remarks>
/// The log, Copy diagnostics and HONOR's driver names stay in English on purpose: they're read
/// in bug reports. A unit test checks that every <c>T("…")</c> in the code has a line in every
/// table and that placeholders match, so a new text can't silently stay untranslated.
/// </remarks>
internal static class L10n
{
    private static Dictionary<string, string>? _table;
    private static AppLanguage? _resolved;

    /// <summary>The table's code for a language, or null for English (no table).</summary>
    internal static string? Code(AppLanguage language) => language switch
    {
        AppLanguage.Russian => "ru",
        AppLanguage.ChineseSimplified => "zh-Hans",
        _ => null
    };

    /// <summary>The language for Windows' display language: Russian, Simplified Chinese, or English.</summary>
    internal static AppLanguage FromCulture(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            switch (c.Name)
            {
                case "ru":
                    return AppLanguage.Russian;
                case "zh-Hans" or "zh-CN" or "zh-SG" or "zh-CHS":
                    return AppLanguage.ChineseSimplified;
                case "zh-Hant" or "zh-TW" or "zh-HK" or "zh-MO" or "zh-CHT":
                    return AppLanguage.English;   // Traditional Chinese has no table yet
            }
        }
        return AppLanguage.English;
    }

    /// <summary>
    /// The language shown now: the user's choice, or Windows' for Auto. Setting it shows another
    /// language at once without storing a choice (the unit tests use it).
    /// </summary>
    internal static AppLanguage Current
    {
        get => _resolved ??= Resolve(AppSettings.Language);
        set
        {
            Reload();
            _resolved = Resolve(value);
        }
    }

    private static AppLanguage Resolve(AppLanguage chosen)
        => chosen == AppLanguage.Auto ? FromCulture(CultureInfo.CurrentUICulture) : chosen;

    /// <summary>After the language setting changed: the next text comes from the new table.</summary>
    internal static void Reload()
    {
        _resolved = null;
        _table = null;
        FluentTheme.ReloadFonts();   // Chinese has its own font
    }

    /// <summary>
    /// Separates a text from a context that tells two meanings of the same English apart:
    /// <c>T("Home§page")</c> (the settings page) and <c>T("Home")</c> (the charge limit preset)
    /// get their own translations, and both show "Home" in English.
    /// </summary>
    internal const char Context = '§';

    /// <summary>The text in the current language, or <paramref name="english"/> (without a context) when there's no translation.</summary>
    internal static string T(string english)
        => (_table ??= Load(Code(Current))).TryGetValue(english, out var translated) ? translated : WithoutContext(english);

    /// <summary>"Home§page" → "Home".</summary>
    internal static string WithoutContext(string text) => text.IndexOf(Context) is var cut and >= 0 ? text[..cut] : text;

    /// <summary>
    /// Marks an English text as one that gets translated later, through a variable
    /// (<c>T(word)</c>), and returns it unchanged. The tables and their test see it as a
    /// <c>T("…")</c> text.
    /// </summary>
    internal static string N(string english) => english;

    /// <summary><see cref="T(string)"/> with <c>{0}</c>, <c>{1}</c>… filled in.</summary>
    internal static string T(string english, params object?[] values)
        => string.Format(CultureInfo.CurrentCulture, T(english), values);

    /// <summary>The embedded table for <paramref name="code"/>; empty for English or when it can't be read.</summary>
    internal static Dictionary<string, string> Load(string? code)
    {
        if (code is null)
            return [];
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream($"Art14Helper.Translations.{code}.json");
        if (stream is null)
            return [];
        try
        {
            return Parse(stream);
        }
        catch (JsonException exception)
        {
            Log.Error($"Translation table {code} unreadable", exception);
            return [];
        }
    }

    /// <summary>A table: one JSON object of English text → translation. Empty translations count as missing.</summary>
    internal static Dictionary<string, string> Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in document.RootElement.EnumerateObject())
            if (line.Value.GetString() is { Length: > 0 } translated)
                table[line.Name] = translated;
        return table;
    }
}
