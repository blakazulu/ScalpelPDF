using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Scalpel.Tests;

/// <summary>
/// Every locale file must carry exactly the English key set (a missing key blanks that
/// DynamicResource in that language), every value must be non-empty, and format
/// placeholders ({0}, {1:N0}, ...) must match the English entry so string.Format never throws.
/// </summary>
public sealed class LocaleParityTests
{
    private static readonly string[] Locales =
        ["en-US", "es", "zh-TW", "zh-CN", "bn", "tr-TR", "he", "ar", "ru"];

    private static readonly string StringsDirectory = FindStringsDirectory();
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AllExpectedLocaleFilesArePresent()
    {
        var present = Directory.GetFiles(StringsDirectory, "*.xaml")
            .Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray();
        Assert.Equal(Locales.OrderBy(x => x), present);
    }

    [Fact]
    public void EveryLocaleHasTheEnglishKeysAndPlaceholders()
    {
        var english = ReadStrings(Path.Combine(StringsDirectory, "en-US.xaml"));
        Assert.NotEmpty(english);

        foreach (var file in Directory.GetFiles(StringsDirectory, "*.xaml"))
        {
            var localized = ReadStrings(file);
            string name = Path.GetFileName(file);

            var missing = english.Keys.Except(localized.Keys).OrderBy(x => x).ToArray();
            var extra = localized.Keys.Except(english.Keys).OrderBy(x => x).ToArray();
            Assert.True(missing.Length == 0 && extra.Length == 0,
                $"{name} does not contain exactly the English resource-key set. " +
                $"Missing: [{string.Join(", ", missing)}] Extra: [{string.Join(", ", extra)}]");

            foreach (var key in english.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(localized[key]),
                    $"{name} has an empty value for {key}.");
                Assert.True(Placeholders(english[key]).SequenceEqual(Placeholders(localized[key])),
                    $"{name} has different placeholders for {key}.");
            }
        }
    }

    [Fact]
    public void NoLocaleFileHasDuplicateKeys()
    {
        foreach (var file in Directory.GetFiles(StringsDirectory, "*.xaml"))
        {
            var keys = XDocument.Load(file).Root!.Elements()
                .Select(e => e.Attribute(Xaml + "Key")?.Value)
                .Where(k => k is not null)
                .ToList();
            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.True(duplicates.Length == 0,
                $"{Path.GetFileName(file)} defines these keys more than once: {string.Join(", ", duplicates)}");
        }
    }

    private static Dictionary<string, string> ReadStrings(string path)
    {
        var result = new Dictionary<string, string>();
        foreach (var e in XDocument.Load(path).Root!.Elements())
        {
            string? key = e.Attribute(Xaml + "Key")?.Value;
            if (key is null || result.ContainsKey(key)) continue;
            result[key] = e.Value;
        }
        return result;
    }

    // The distinct set of format items: a translation may legitimately mention an argument
    // once where English mentions it twice, but it must not reference one English lacks
    // (string.Format would throw) or drop one entirely.
    private static IEnumerable<string> Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+(?::[^}]*)?\}").Cast<Match>().Select(m => m.Value).Distinct().OrderBy(x => x);

    // Walk up from the test bin dir to the repo root (the folder that has a Strings dir).
    private static string FindStringsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Strings")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "Strings");
    }
}
