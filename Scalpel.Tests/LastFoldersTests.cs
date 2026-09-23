using System.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LastFoldersTests
    {
        [Fact]
        public void SettingNameIsStableAndScopedPerPurpose()
        {
            Assert.Equal("LastFolder.Image", LastFolders.SettingName(LastFolders.Image));
            Assert.Equal("LastFolder.Signature", LastFolders.SettingName(LastFolders.Signature));
            Assert.NotEqual(LastFolders.SettingName(LastFolders.Stamp),
                            LastFolders.SettingName(LastFolders.Watermark));
        }

        [Fact]
        public void ResolveReturnsAnExistingFolder()
        {
            var dir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            Assert.Equal(dir, LastFolders.Resolve(dir));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ResolveIgnoresEmptyValues(string? stored)
            => Assert.Null(LastFolders.Resolve(stored));

        [Fact]
        public void ResolveIgnoresAFolderThatNoLongerExists()
            => Assert.Null(LastFolders.Resolve(Path.Combine(Path.GetTempPath(), "scalpel-gone-" + Path.GetRandomFileName())));

        [Fact]
        public void FromPickedPathReturnsTheContainingFolder()
        {
            var picked = Path.Combine(Path.GetTempPath(), "sig.png");
            Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                         LastFolders.FromPickedPath(picked)!.TrimEnd(Path.DirectorySeparatorChar));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("bare-name.png")]
        public void FromPickedPathReturnsNullWhenThereIsNoFolder(string? picked)
            => Assert.Null(LastFolders.FromPickedPath(picked));
    }
}
