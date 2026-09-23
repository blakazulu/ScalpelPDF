using System;
using System.Windows;

namespace Scalpel.Services
{
    internal enum Locale { EnUS, Es, ZhTW, ZhCN, Bn, TrTR, He, Ar, Ru }

    internal static class LocaleManager
    {
        private static Locale _current = Locale.EnUS;

        public static Locale Current => _current;

        /// <summary>
        /// Call once at startup (after ThemeManager.Initialize) to restore the saved locale.
        /// </summary>
        public static void Initialize()
        {
            var saved = App.GetSetting("Locale");
            _current = Enum.TryParse<Locale>(saved, out var l) ? l : Locale.EnUS;
            ApplyInternal(_current);
        }

        /// <summary>
        /// Switch locale, persist choice, and hot-swap the string ResourceDictionary.
        /// </summary>
        public static void Apply(Locale locale)
        {
            _current = locale;
            App.SetSetting("Locale", locale.ToString());
            ApplyInternal(locale);
        }

        // ── Internal ─────────────────────────────────────────────────────

        /// <summary>
        /// A translation file supplied on the command line with --lang-file, or null.
        /// <para>Lets a translator run their own <c>.xaml</c> against the real app and see every
        /// string in place without a rebuild - the loop that otherwise makes translating this many
        /// keys guesswork. Any key the file omits falls back to English, because the English
        /// dictionary is merged underneath it.</para>
        /// </summary>
        public static string? OverrideFile { get; private set; }

        /// <summary>Records a --lang-file path, if it names a readable file.</summary>
        public static bool TrySetOverrideFile(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return false;
                OverrideFile = System.IO.Path.GetFullPath(path);
                return true;
            }
            catch { return false; }
        }

        private static void ApplyInternal(Locale locale)
        {
            var uri = locale switch
            {
                Locale.Es   => new Uri("pack://application:,,,/Strings/es.xaml"),
                Locale.ZhTW => new Uri("pack://application:,,,/Strings/zh-TW.xaml"),
                Locale.ZhCN => new Uri("pack://application:,,,/Strings/zh-CN.xaml"),
                Locale.Bn   => new Uri("pack://application:,,,/Strings/bn.xaml"),
                Locale.TrTR => new Uri("pack://application:,,,/Strings/tr-TR.xaml"),
                Locale.He   => new Uri("pack://application:,,,/Strings/he.xaml"),
                Locale.Ar   => new Uri("pack://application:,,,/Strings/ar.xaml"),
                Locale.Ru   => new Uri("pack://application:,,,/Strings/ru.xaml"),
                _           => new Uri("pack://application:,,,/Strings/en-US.xaml"),
            };

            var dict   = new ResourceDictionary { Source = uri };
            var merged = Application.Current.Resources.MergedDictionaries;

            // A --lang-file translation is layered ON TOP of English rather than replacing it,
            // so a partial file still runs: its keys win, everything else stays readable.
            if (OverrideFile is not null)
            {
                try
                {
                    var baseDict = new ResourceDictionary
                    { Source = new Uri("pack://application:,,,/Strings/en-US.xaml") };
                    var overrideDict = new ResourceDictionary
                    { Source = new Uri(OverrideFile, UriKind.Absolute) };

                    var combined = new ResourceDictionary();
                    combined.MergedDictionaries.Add(baseDict);
                    combined.MergedDictionaries.Add(overrideDict);
                    dict = combined;
                }
                catch (Exception ex)
                {
                    // A malformed translation must not stop the app starting in English.
                    Logger.Warn("Locale", "langfile.load.fail", "Could not load --lang-file",
                                new { path = OverrideFile, error = ex.Message });
                }
            }

            // Index 0 = theme dict, Index 1 = strings dict
            if (merged.Count > 1)
                merged[1] = dict;
            else
                merged.Add(dict);

            // Mirror the whole UI for RTL locales (Hebrew/Arabic). Safe if MainWindow not yet created.
            var win = Application.Current?.MainWindow;
            if (win != null)
                win.FlowDirection = IsRtlLocale(locale) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        /// <summary>True for locales whose script reads right-to-left (UI should mirror).</summary>
        public static bool IsRtlLocale(Locale l) => l == Locale.He || l == Locale.Ar;
    }
}
