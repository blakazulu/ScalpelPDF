using System;

namespace Scalpel.Services
{
    public enum Theme  { Dark, Light, HighContrast }
    public enum Accent { Amber, Red, Green, Cyan }

    /// <summary>
    /// Pure resolution of persisted settings into a (Theme, Accent) pair.
    /// Kept WPF-free so it is unit-testable and linkable into the test project.
    /// Handles the legacy single-axis theme names (Blood/Greed/Cyanotic).
    /// </summary>
    public static class ThemeMigration
    {
        public static (Theme theme, Accent accent) Resolve(string? themeRaw, string? accentRaw)
        {
            // Base theme + the accent implied by a legacy single-axis value.
            var (theme, legacyAccent) = themeRaw switch
            {
                "Light"        => (Theme.Light,        Accent.Amber),
                "HighContrast" => (Theme.HighContrast, Accent.Amber),
                "Blood"        => (Theme.Dark,         Accent.Red),
                "Greed"        => (Theme.Dark,         Accent.Green),
                "Cyanotic"     => (Theme.Dark,         Accent.Cyan),
                "Dark"         => (Theme.Dark,         Accent.Amber),
                _              => (Theme.Dark,         Accent.Amber),
            };

            // An explicit, valid Accent setting always wins over the legacy derivation.
            var accent = Enum.TryParse<Accent>(accentRaw, out var a) ? a : legacyAccent;
            return (theme, accent);
        }
    }
}
