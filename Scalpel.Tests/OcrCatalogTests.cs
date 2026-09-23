using System;
using System.IO;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// The OCR catalog must keep pace with the interface languages: shipping a UI language with no
    /// recognition model leaves those users unable to OCR in the language they read the app in.
    /// </summary>
    public class OcrCatalogTests
    {
        private static string StringsDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Strings")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "Strings");
        }

        [Fact]
        public void EveryInterfaceLocaleHasAnOcrModel()
        {
            var locales = Directory.GetFiles(StringsDir(), "*.xaml")
                                   .Select(Path.GetFileNameWithoutExtension)
                                   .ToArray();
            Assert.NotEmpty(locales);

            foreach (var locale in locales)
            {
                Assert.True(OcrAssets.LocaleToCode.ContainsKey(locale!),
                    $"interface locale '{locale}' has no OCR model mapping");
                var code = OcrAssets.LocaleToCode[locale!];
                Assert.True(OcrAssets.Languages.Any(l => l.Code == code),
                    $"OCR model '{code}' for locale '{locale}' is not in the catalog");
            }
        }

        [Fact]
        public void CatalogHasNoDuplicateCodesAndNoEmptyNames()
        {
            Assert.Equal(OcrAssets.Languages.Length,
                         OcrAssets.Languages.Select(l => l.Code).Distinct(StringComparer.Ordinal).Count());
            Assert.All(OcrAssets.Languages, l => Assert.False(string.IsNullOrWhiteSpace(l.Name)));
        }

        [Fact]
        public void CodeForLocaleFallsBackToEnglish()
        {
            Assert.Equal("eng", OcrAssets.CodeForLocale("xx-YY"));
            Assert.Equal("eng", OcrAssets.CodeForLocale(null));
            Assert.Equal("heb", OcrAssets.CodeForLocale("he"));
            Assert.Equal("chi_tra", OcrAssets.CodeForLocale("zh-TW"));
        }

        [Theory]
        [InlineData("ben")]
        [InlineData("tur")]
        [InlineData("ces")]
        [InlineData("hun")]
        [InlineData("kaz")]
        [InlineData("pol")]
        public void CatalogCoversTheLanguagesAddedInThisRound(string code)
            => Assert.Contains(OcrAssets.Languages, l => l.Code == code);
    }
}
