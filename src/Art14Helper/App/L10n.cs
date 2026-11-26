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
/// <para>The log, Copy diagnostics and HONOR's driver names stay in English on purpose: they're
/// read in bug reports. Code that builds such text runs it through <see cref="InEnglish{TResult}"/>;
/// error messages are made in English and carry their translation for display
/// (<see cref="Shown{TException}"/>, <see cref="ShownMessage"/>).</para>
/// <para>A unit test checks that every <c>T("…")</c> in the code has a line in every table and
/// that placeholders match, so a new text can't silently stay untranslated.</para>
/// <para>Texts are looked up from any thread while the language can change on the UI thread: the
/// language and its table are one immutable snapshot, swapped in one step.</para>
/// </remarks>
internal static class L10n
{
    private sealed record State(AppLanguage Language, Dictionary<string, string> Table);

    private static State? _state;
    private static int _version;           // bumped by every Reload: a snapshot built across one is stale
    private static int _forced = -1;       // an AppLanguage set by the tests or the elevated copy, never stored; -1 none
    [ThreadStatic] private static int _englishDepth;

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
    /// language at once without storing a choice (the unit tests, and an elevated copy that
    /// speaks the language of the copy that started it).
    /// </summary>
    internal static AppLanguage Current
    {
        get => Snapshot().Language;
        set
        {
            Volatile.Write(ref _forced, (int)value);
            Reload();
        }
    }

    private const string LanguageSwitch = "--lang=";

    /// <summary>
    /// For a copy started through UAC for one job: it speaks this copy's language, even when
    /// another administrator's account (with its own settings) approves the prompt.
    /// </summary>
    internal static string LanguageArgument => LanguageSwitch + Current;

    /// <summary>At start: takes the language from <see cref="LanguageArgument"/> when the command line has one.</summary>
    internal static void TakeLanguageArgument(IEnumerable<string> args)
    {
        foreach (var arg in args)
            if (arg.StartsWith(LanguageSwitch, StringComparison.Ordinal)
                && Enum.TryParse<AppLanguage>(arg.AsSpan(LanguageSwitch.Length), out var language) && Enum.IsDefined(language))
                Current = language;
    }

    private static AppLanguage Resolve(AppLanguage chosen)
        => chosen == AppLanguage.Auto ? FromCulture(CultureInfo.CurrentUICulture) : chosen;

    private static State Snapshot()
    {
        while (true)
        {
            if (Volatile.Read(ref _state) is { } state)
                return state;
            var version = Volatile.Read(ref _version);
            var forced = Volatile.Read(ref _forced);
            var language = Resolve(forced >= 0 ? (AppLanguage)forced : AppSettings.Language);
            var fresh = new State(language, Load(Code(language)));
            if (Interlocked.CompareExchange(ref _state, fresh, null) is { } first)
                return first;   // another thread was quicker
            if (Volatile.Read(ref _version) == version)
                return fresh;
            // The language changed while this was built: take it back and build again.
            Interlocked.CompareExchange(ref _state, null, fresh);
        }
    }

    /// <summary>After the language setting changed: the next text comes from the new table.</summary>
    internal static void Reload()
    {
        Interlocked.Increment(ref _version);
        Volatile.Write(ref _state, null);
        FluentTheme.ReloadFonts();   // Chinese has its own font
    }

    /// <summary>
    /// Runs <paramref name="build"/> with every <see cref="T(string)"/> on this thread returning
    /// the English: for the log and Copy diagnostics, which stay English for bug reports.
    /// </summary>
    internal static TResult InEnglish<TResult>(Func<TResult> build)
    {
        _englishDepth++;
        try
        {
            return build();
        }
        finally
        {
            _englishDepth--;
        }
    }

    /// <summary>
    /// Separates a text from a context that tells two meanings of the same English apart:
    /// <c>T("Home§page")</c> (the settings page) and <c>T("Home")</c> (the charge limit preset)
    /// get their own translations, and both show "Home" in English.
    /// </summary>
    internal const char Context = '§';

    /// <summary>The text in the current language, or <paramref name="english"/> (without a context) when there's no translation.</summary>
    internal static string T(string english)
        => _englishDepth == 0 && Snapshot().Table.TryGetValue(english, out var translated) ? translated : WithoutContext(english);

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

    private const string ShownKey = "Art14Helper.Shown";

    /// <summary>
    /// An exception whose message is the English (so the log stays English) and which carries
    /// the text in the user's language for a dialog (<see cref="ShownMessage"/>). Write the
    /// English with N("…") so the tables have it: <c>throw Shown(m => new InvalidOperationException(m), N("…"))</c>.
    /// </summary>
    internal static TException Shown<TException>(Func<string, TException> create, string english, params object?[] values)
        where TException : Exception
    {
        var exception = create(InEnglish(() => values.Length == 0 ? WithoutContext(english) : T(english, values)));
        exception.Data[ShownKey] = values.Length == 0 ? T(english) : T(english, values);
        return exception;
    }

    /// <summary>The text of an exception for the user: its translation when it carries one, else its message.</summary>
    internal static string ShownMessage(Exception exception)
        => exception.Data[ShownKey] as string ?? exception.Message;

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
