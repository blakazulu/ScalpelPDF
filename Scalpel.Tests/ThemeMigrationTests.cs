using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ThemeMigrationTests
    {
        [Theory]
        [InlineData("Dark",  Theme.Dark,         Accent.Amber)]
        [InlineData("Light", Theme.Light,        Accent.Amber)]
        [InlineData("HighContrast", Theme.HighContrast, Accent.Amber)]
        [InlineData("Blood",    Theme.Dark, Accent.Red)]
        [InlineData("Greed",    Theme.Dark, Accent.Green)]
        [InlineData("Cyanotic", Theme.Dark, Accent.Cyan)]
        public void Resolve_maps_legacy_theme_when_no_accent_saved(
            string themeRaw, Theme expectedTheme, Accent expectedAccent)
        {
            var (theme, accent) = ThemeMigration.Resolve(themeRaw, accentRaw: null);
            Assert.Equal(expectedTheme, theme);
            Assert.Equal(expectedAccent, accent);
        }

        [Fact]
        public void Resolve_uses_explicit_accent_over_legacy_derivation()
        {
            // New-style settings: base Light, explicit accent Green.
            var (theme, accent) = ThemeMigration.Resolve("Light", "Green");
            Assert.Equal(Theme.Light, theme);
            Assert.Equal(Accent.Green, accent);
        }

        [Fact]
        public void Resolve_explicit_accent_wins_even_with_legacy_theme_name()
        {
            // Legacy "Blood" would derive Red, but a stored Accent must win.
            var (theme, accent) = ThemeMigration.Resolve("Blood", "Cyan");
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Cyan, accent);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        public void Resolve_defaults_to_Dark_Amber_on_unknown_theme(string? themeRaw)
        {
            var (theme, accent) = ThemeMigration.Resolve(themeRaw, accentRaw: null);
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Amber, accent);
        }

        [Fact]
        public void Resolve_ignores_invalid_accent_string()
        {
            var (theme, accent) = ThemeMigration.Resolve("Dark", "notacolor");
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Amber, accent);
        }
    }
}
