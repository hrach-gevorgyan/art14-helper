using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>
/// Tests that switch the app's language (a process-wide setting) run on their own, never
/// alongside others that check English text.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageSwitching
{
    public const string Name = "Language switching";
}

/// <summary>
/// The translation tables against the code: every text the code marks with T("…") or N("…")
/// has a translation in every table, no table keeps lines the code no longer has, and each
/// translation has the same placeholders as its English.
/// </summary>
[Collection(LanguageSwitching.Name)]
public partial class TranslationTests
{
    private static readonly string[] Codes = ["ru", "zh-Hans"];

    // T("…") or N("…") with a plain C# string literal (escapes allowed), not an interpolated or verbatim one.
    [GeneratedRegex("""\b[TN]\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex Marked();

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string SourceFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Art14Helper"));

    /// <summary>Every marked English text in the app's code, unescaped as the compiler would.</summary>
    internal static SortedSet<string> MarkedTexts()
    {
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(SourceFolder(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            foreach (Match match in Marked().Matches(File.ReadAllText(file)))
                texts.Add(Regex.Unescape(match.Groups[1].Value));
        }
        return texts;
    }

    private static Dictionary<string, string> Table(string code)
    {
        using var stream = File.OpenRead(Path.Combine(SourceFolder(), "Translations", code + ".json"));
        return L10n.Parse(stream);
    }

    [Fact]
    public void TheCodeMarksTexts() => Assert.True(MarkedTexts().Count > 200, "the scanner found almost no T(\"…\") texts");

    [Theory]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void EveryTextHasATranslation(string code)
    {
        var table = Table(code);
        var missing = MarkedTexts().Where(text => !table.ContainsKey(text)).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} text(s) without a {code} translation:\n" + string.Join("\n", missing.Take(40)));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void NoLeftoverLines(string code)
    {
        var marked = MarkedTexts();
        var unused = Table(code).Keys.Where(key => !marked.Contains(key)).ToList();
        Assert.True(unused.Count == 0, $"{unused.Count} {code} line(s) the code no longer uses:\n" + string.Join("\n", unused.Take(40)));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void PlaceholdersMatch(string code)
    {
        static string Shape(string text) => string.Join(",", Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal));
        var wrong = Table(code).Where(line => Shape(line.Key) != Shape(line.Value)).Select(line => $"{line.Key} → {line.Value}").ToList();
        Assert.True(wrong.Count == 0, "placeholders differ:\n" + string.Join("\n", wrong));
        foreach (var (english, translated) in Table(code))   // and each one formats
            _ = string.Format(CultureInfo.InvariantCulture, translated, Enumerable.Repeat<object?>("x", 9).ToArray());
    }

    [Fact]
    public void TablesAreEmbeddedAndLoad()
    {
        foreach (var code in Codes)
            Assert.NotEmpty(L10n.Load(code));
        Assert.Empty(L10n.Load(null));
    }

    [Theory]
    [InlineData("ru-RU", "Russian")]
    [InlineData("ru", "Russian")]
    [InlineData("zh-CN", "ChineseSimplified")]
    [InlineData("zh-Hans-HK", "ChineseSimplified")]
    [InlineData("zh-TW", "English")]
    [InlineData("zh-Hant", "English")]
    [InlineData("hy-AM", "English")]
    [InlineData("en-US", "English")]
    public void FollowsWindowsDisplayLanguage(string culture, string expected)
        => Assert.Equal(Enum.Parse<AppLanguage>(expected), L10n.FromCulture(new CultureInfo(culture)));

    [Fact]
    public void TranslatesAndFallsBackToEnglish()
    {
        try
        {
            L10n.Current = AppLanguage.Russian;
            var anyLine = Table("ru").First();
            Assert.Equal(anyLine.Value, L10n.T(anyLine.Key));
            Assert.Equal("Not a text the app has", L10n.T("Not a text the app has"));
            L10n.Current = AppLanguage.English;
            Assert.Equal(anyLine.Key, L10n.T(anyLine.Key));
        }
        finally
        {
            L10n.Current = AppLanguage.English;
        }
    }

    [Fact]
    public void ParsesTablesWithEmptyLinesAsMissing()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""{ "On": "Вкл", "Off": "", }"""));
        var table = L10n.Parse(json);
        Assert.Equal("Вкл", table["On"]);
        Assert.False(table.ContainsKey("Off"));
    }
}
