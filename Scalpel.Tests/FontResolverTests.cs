using System;
using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class FontResolverTests
    {
        // A representative set of installed families for deterministic IsInstalled checks.
        private static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase)
        {
            "Times New Roman", "Arial", "Helvetica", "Segoe UI"
        };

        [Theory]
        // raw,                         expDisplay,        expBold, expItalic, expInstalled
        [InlineData("ABCDEF+Minion-BoldItalic", "Minion",          true,  true,  false)]
        [InlineData("TimesNewRomanPSMT",        "Times New Roman", false, false, true)]
        [InlineData("TimesNewRomanPS-BoldMT",   "Times New Roman", true,  false, true)]
        [InlineData("Arial,BoldItalic",         "Arial",           true,  true,  true)]
        [InlineData("Helvetica-Oblique",        "Helvetica",       false, true,  true)]
        [InlineData("ArialMT",                  "Arial",           false, false, true)]
        public void Resolve_ParsesNameStyleAndAvailability(
            string raw, string expDisplay, bool expBold, bool expItalic, bool expInstalled)
        {
            var r = FontResolver.Resolve(raw, Installed);
            Assert.Equal(expDisplay, r.DisplayName);
            Assert.Equal(expBold, r.IsBold);
            Assert.Equal(expItalic, r.IsItalic);
            Assert.Equal(expInstalled, r.IsInstalled);
        }

        [Fact]
        public void Resolve_MissingFont_FallsBackFamilyToSegoeUI_ButKeepsDisplayName()
        {
            var r = FontResolver.Resolve("ABCDEF+Minion-BoldItalic", Installed);
            Assert.Equal("Minion", r.DisplayName);
            Assert.Equal("Segoe UI", r.FamilyName);   // substitute used for actual drawing
            Assert.False(r.IsInstalled);
        }

        [Fact]
        public void Resolve_InstalledFont_FamilyMatchesAvailableCasing()
        {
            var r = FontResolver.Resolve("ArialMT", Installed);
            Assert.Equal("Arial", r.FamilyName);
            Assert.True(r.IsInstalled);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_EmptyOrNull_ReturnsSafeDefault(string? raw)
        {
            var r = FontResolver.Resolve(raw, Installed);
            Assert.Equal("Segoe UI", r.DisplayName);
            Assert.Equal("Segoe UI", r.FamilyName);
            Assert.False(r.IsBold);
            Assert.False(r.IsItalic);
            Assert.True(r.IsInstalled);  // no spurious toast for unknown fonts
        }
    }
}
