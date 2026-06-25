using System;
using System.Linq;
using System.Text.Json;

namespace Scalpel.Services
{
    /// <summary>Latest-version metadata fetched from the website's version.json.</summary>
    public sealed record UpdateInfo(string Version, string SiteUrl, string StoreUrl, string[] Notes);

    /// <summary>
    /// Update-check logic: parse the published version.json, compare versions, and resolve the
    /// distribution-appropriate download URL. WPF-free and registry-free so it can be unit-tested.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>Fallback when a packaged build has no explicit storeUrl.</summary>
        public const string StoreSearchUrl = "https://apps.microsoft.com/search?query=Scalpel+PDF";

        /// <summary>True when <paramref name="latest"/> is a strictly newer 3-part version.</summary>
        public static bool IsNewer(string latest, Version current)
        {
            if (!TryParseVersion(latest, out var l)) return false;
            var c = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            return l > c;
        }

        /// <summary>Parses a version.json document; returns null on any malformed/missing-version input.</summary>
        public static UpdateInfo? TryParse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String)
                    return null;
                string version = v.GetString() ?? "";
                if (version.Length == 0) return null;

                string site = StringProp(root, "siteUrl");
                string store = StringProp(root, "storeUrl");
                string[] notes = Array.Empty<string>();
                if (root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.Array)
                    notes = n.EnumerateArray()
                             .Where(e => e.ValueKind == JsonValueKind.String)
                             .Select(e => e.GetString() ?? "")
                             .Where(s => s.Length > 0)
                             .ToArray();

                return new UpdateInfo(version, site, store, notes);
            }
            catch { return null; }
        }

        /// <summary>Store URL for packaged builds (search fallback if empty), else the site URL.</summary>
        public static string ResolveUrl(UpdateInfo info, bool packaged)
        {
            if (packaged)
                return string.IsNullOrWhiteSpace(info.StoreUrl) ? StoreSearchUrl : info.StoreUrl;
            return info.SiteUrl;
        }

        private static string StringProp(JsonElement root, string name) =>
            root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "" : "";

        private static bool TryParseVersion(string s, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split('.');
            if (parts.Length == 0) return false;
            int Get(int i) => i < parts.Length && int.TryParse(parts[i], out var n) && n >= 0 ? n : 0;
            // Require at least the major to be a real number.
            if (!int.TryParse(parts[0], out _)) return false;
            version = new Version(Get(0), Get(1), Get(2));
            return true;
        }
    }
}
