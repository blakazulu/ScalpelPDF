using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Scalpel.Services
{
    /// <summary>
    /// Per-user install/uninstall logic and the canonical cleanup inventory.
    /// WPF-free so it is unit-testable. All paths are HKCU + %LOCALAPPDATA% only.
    /// </summary>
    internal static class Installer
    {
        private const string AppName = "Scalpel";
        private const string ExeName = "Scalpel.exe";

        private static string Local =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // ── Canonical paths ───────────────────────────────────────────────
        public static string InstallDir   => Path.Combine(Local, "Programs", AppName);
        public static string InstallExe   => Path.Combine(InstallDir, ExeName);
        public static string DataDir      => Path.Combine(Local, AppName);   // signatures, logs, Temp, crash logs
        public static string StartMenuDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        public static string StartMenuLnk => Path.Combine(StartMenuDir, $"{AppName}.lnk");
        public static string DesktopLnk   => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

        // ── Cleanup inventory (single source of truth) ─────────────────────
        // HKCU subtrees removed wholesale.
        public static IReadOnlyList<string> OwnedRegistryKeys { get; } =
        [
            @"Software\Scalpel",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel",
            @"Software\Classes\Scalpel.pdf",
        ];

        // Stray values under shared shell keys we must NOT delete wholesale.
        public static IReadOnlyList<(string KeyPath, string ValueName)> OwnedRegistryValues { get; } =
        [
            (@"Software\Classes\.pdf\OpenWithProgids", "Scalpel.pdf"),
            (@"Software\RegisteredApplications", "Scalpel"),
        ];

        // Filesystem dirs + shortcut files removed on uninstall.
        public static IReadOnlyList<string> OwnedPaths { get; } =
        [
            Path.Combine(Local, "Programs", AppName),  // == InstallDir
            Path.Combine(Local, AppName),              // == DataDir
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName,
                $"{AppName}.lnk"),                     // == StartMenuLnk
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{AppName}.lnk"),                     // == DesktopLnk
        ];

        // ── Shell notify ──────────────────────────────────────────────────
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        // ── Wipe ──────────────────────────────────────────────────────────

        /// <summary>
        /// Removes everything that can be removed while the process is still running:
        /// registry subtrees + stray values, shortcut files + the Start-Menu dir, and
        /// %TEMP%\scalpel_*.pdf scratch. The install dir and data dir are NOT removed here
        /// (they may be locked) — defer those to WriteDeferredDirWipeScript().
        /// </summary>
        public static void WipeAllData()
        {
            foreach (var key in OwnedRegistryKeys)
                try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); } catch { }

            foreach (var (keyPath, valueName) in OwnedRegistryValues)
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                    k?.DeleteValue(valueName, throwOnMissingValue: false);
                }
                catch { }

            try { File.Delete(StartMenuLnk); } catch { }
            try { Directory.Delete(StartMenuDir, recursive: false); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            try
            {
                var temp = Path.GetTempPath();
                foreach (var f in Directory.GetFiles(temp, "scalpel_*.pdf"))
                    try { File.Delete(f); } catch { }
            }
            catch { }

            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        /// <summary>
        /// Writes a hidden batch file that (after a short delay, so the EXE can exit)
        /// removes both the install dir and the data dir, then deletes itself. Returns
        /// the .bat path. The caller is responsible for launching it.
        /// </summary>
        public static string WriteDeferredDirWipeScript()
        {
            string bat = Path.Combine(Path.GetTempPath(), "scalpel_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "setlocal\r\n" +
                "set /a tries=0\r\n" +
                ":retry\r\n" +
                $"rmdir /s /q \"{InstallDir}\" 2>nul\r\n" +
                $"if not exist \"{InstallExe}\" goto wipedata\r\n" +
                "set /a tries+=1\r\n" +
                "if %tries% geq 20 goto wipedata\r\n" +
                "ping -n 2 127.0.0.1 >nul\r\n" +
                "goto retry\r\n" +
                ":wipedata\r\n" +
                $"rmdir /s /q \"{DataDir}\" 2>nul\r\n" +
                "del \"%~f0\"\r\n");
            return bat;
        }
    }
}
