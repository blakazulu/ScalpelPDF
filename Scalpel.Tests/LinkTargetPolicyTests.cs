using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LinkTargetPolicyTests
    {
        [Theory]
        [InlineData("https://example.com", "https://example.com/")]
        [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
        [InlineData("HTTPS://Example.COM/A", "https://example.com/A")]
        public void AllowsWebSchemes(string raw, string expected)
        {
            Assert.True(LinkTargetPolicy.TryNormalize(raw, out var target));
            Assert.Equal(expected, target);
        }

        [Fact]
        public void AllowsMailtoUnchanged()
        {
            Assert.True(LinkTargetPolicy.TryNormalize("mailto:someone@example.com?subject=Hi", out var t));
            Assert.Equal("mailto:someone@example.com?subject=Hi", t);
        }

        [Theory]
        [InlineData("www.example.com", "https://www.example.com/")]
        [InlineData("example.com/path", "https://example.com/path")]
        [InlineData("example.co.uk:8080/x", "https://example.co.uk:8080/x")]
        public void PromotesBareDottedHostToHttps(string raw, string expected)
        {
            Assert.True(LinkTargetPolicy.TryNormalize(raw, out var target));
            Assert.Equal(expected, target);
        }

        [Theory]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("vbscript:msgbox(1)")]
        [InlineData("ms-msdt:/id")]
        [InlineData("search-ms:query=x")]
        [InlineData("shell:startup")]
        [InlineData("\\\\server\\share\\payload.exe")]
        [InlineData("C:\\Windows\\System32\\calc.exe")]
        [InlineData("//example.com/x")]
        [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
        public void RejectsEverythingElse(string raw)
        {
            Assert.False(LinkTargetPolicy.TryNormalize(raw, out var target));
            Assert.Equal(string.Empty, target);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("localhost")]
        [InlineData("just some text")]
        [InlineData("mailto:")]
        [InlineData(".com")]
        [InlineData("example.")]
        public void RejectsEmptyAndNonHosts(string? raw)
        {
            Assert.False(LinkTargetPolicy.TryNormalize(raw, out _));
        }

        [Fact]
        public void RejectsControlCharacters()
        {
            Assert.False(LinkTargetPolicy.TryNormalize("https://example.com\r\nX-Evil: 1", out _));
        }
    }
}
