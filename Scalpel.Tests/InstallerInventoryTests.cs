using System;
using System.IO;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class InstallerInventoryTests
    {
        private static string Local =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        [Fact]
        public void OwnedRegistryKeys_cover_all_owned_subtrees()
        {
            Assert.Contains(@"Software\Scalpel", Installer.OwnedRegistryKeys);
            Assert.Contains(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel",
                Installer.OwnedRegistryKeys);
            Assert.Contains(@"Software\Classes\Scalpel.pdf", Installer.OwnedRegistryKeys);
        }

        [Fact]
        public void OwnedRegistryValues_cover_shared_shell_keys()
        {
            Assert.Contains((@"Software\Classes\.pdf\OpenWithProgids", "Scalpel.pdf"),
                Installer.OwnedRegistryValues);
            Assert.Contains((@"Software\RegisteredApplications", "Scalpel"),
                Installer.OwnedRegistryValues);
        }

        [Fact]
        public void OwnedPaths_cover_install_dir_data_dir_and_shortcuts()
        {
            Assert.Contains(Path.Combine(Local, "Programs", "Scalpel"), Installer.OwnedPaths);
            Assert.Contains(Path.Combine(Local, "Scalpel"), Installer.OwnedPaths);
            Assert.Contains(Installer.StartMenuLnk, Installer.OwnedPaths);
            Assert.Contains(Installer.DesktopLnk, Installer.OwnedPaths);
        }

        [Fact]
        public void DataDir_is_the_parent_of_the_temp_and_logs_dirs()
        {
            // The data dir must be the whole %LOCALAPPDATA%\Scalpel tree, not just a subdir,
            // so signatures.json + logs + Temp are all removed.
            Assert.Equal(Path.Combine(Local, "Scalpel"), Installer.DataDir);
        }

        [Fact]
        public void WriteDeferredDirWipeScript_targets_both_dirs_and_retries()
        {
            var bat = Installer.WriteDeferredDirWipeScript();
            try
            {
                Assert.True(File.Exists(bat));
                var text = File.ReadAllText(bat);
                Assert.Contains(Installer.InstallDir, text);
                Assert.Contains(Installer.DataDir, text);
                Assert.Contains(":retry", text);
                Assert.Contains("del \"%~f0\"", text);
            }
            finally { try { File.Delete(bat); } catch { } }
        }
    }
}
