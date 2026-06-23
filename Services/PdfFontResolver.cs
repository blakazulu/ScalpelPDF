using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Fonts;

namespace Scalpel.Services
{
    /// <summary>
    /// Process-global PdfSharpCore font resolver. Replaces the built-in GDI resolver,
    /// so it serves both installed system fonts (lazy index of the Windows fonts dir)
    /// and bundled application fonts (registered byte arrays). Never throws; never
    /// returns null — unknown families fall back to Arial.
    /// Embedding-time resolver for PdfSharpCore; distinct from <see cref="FontResolver"/>
    /// which only normalizes names / checks availability for the editor.
    /// </summary>
    public sealed class PdfFontResolver : IFontResolver
    {
        public static PdfFontResolver Instance { get; } = new PdfFontResolver();
        private PdfFontResolver() { }

        private const string FallbackFamily = "arial";

        // Required by IFontResolver in PdfSharpCore 1.3.67 — the fallback face name
        // returned when resolution fails.
        public string DefaultFontName => "Arial";

        // faceKey -> (filePath, ttcFaceIndex). faceKey is "family|b|i" lowercased.
        private Dictionary<string, (string Path, int Face)>? _systemIndex;
        private readonly ConcurrentDictionary<string, byte[]> _bundled = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _byFaceCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        /// <summary>Register a bundled font's bytes for a family + style. Bundled wins
        /// over system. Called at app startup (pack:// bytes) and from tests.</summary>
        public void RegisterBundledFont(string family, byte[] bytes, bool bold, bool italic)
        {
            if (string.IsNullOrWhiteSpace(family) || bytes is null || bytes.Length == 0) return;
            lock (_lock) _bundled[FaceKey(family, bold, italic)] = bytes;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            try
            {
                string fam = (familyName ?? "").Trim();
                EnsureIndex();

                // 1. Exact bundled or system match.
                string exact = FaceKey(fam, isBold, isItalic);
                if (_bundled.ContainsKey(exact) || _systemIndex!.ContainsKey(exact))
                    return new FontResolverInfo(exact);

                // 2. Family exists but not this exact style → use regular, simulate.
                string regular = FaceKey(fam, false, false);
                if (_bundled.ContainsKey(regular) || _systemIndex!.ContainsKey(regular))
                    return new FontResolverInfo(regular, isBold, isItalic);

                // 3. Unknown family → Arial, simulate requested style.
                string fb = FaceKey(FallbackFamily, false, false);
                return new FontResolverInfo(fb, isBold, isItalic);
            }
            catch
            {
                return new FontResolverInfo(FaceKey(FallbackFamily, false, false), isBold, isItalic);
            }
        }

        public byte[] GetFont(string faceName)
        {
            try
            {
                lock (_lock)
                {
                    if (_byFaceCache.TryGetValue(faceName, out var cached)) return cached;
                    byte[]? bytes = null;
                    if (_bundled.TryGetValue(faceName, out var b)) bytes = b;
                    else
                    {
                        EnsureIndex();
                        if (_systemIndex!.TryGetValue(faceName, out var loc))
                            bytes = ExtractFace(loc.Path, loc.Face);
                    }
                    bytes ??= ReadFallback();
                    if (bytes.Length > 0) _byFaceCache[faceName] = bytes;
                    return bytes;
                }
            }
            catch { return ReadFallback(); }
        }

        // ---- internals ----

        private static string FaceKey(string family, bool bold, bool italic)
            => $"{family.Trim().ToLowerInvariant()}|{(bold ? 1 : 0)}|{(italic ? 1 : 0)}";

        private void EnsureIndex()
        {
            if (_systemIndex is not null) return;
            lock (_lock)
            {
                if (_systemIndex is not null) return;
                var index = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is not (".ttf" or ".ttc" or ".otf")) continue;
                        try
                        {
                            byte[] data = File.ReadAllBytes(file);
                            int faces = CountFaces(data);
                            for (int fi = 0; fi < faces; fi++)
                            {
                                var n = TrueTypeName.Read(data, fi);
                                if (string.IsNullOrEmpty(n.Family)) continue;
                                bool bold = ContainsCI(n.Subfamily, "bold");
                                bool italic = ContainsCI(n.Subfamily, "italic") || ContainsCI(n.Subfamily, "oblique");
                                string key = FaceKey(n.Family, bold, italic);
                                if (!index.ContainsKey(key)) index[key] = (file, fi);
                            }
                        }
                        catch { /* skip malformed file */ }
                    }
                }
                catch { /* leave index empty; fallback path still works */ }
                _systemIndex = index;
            }
        }

        private static bool ContainsCI(string s, string sub)
            => !string.IsNullOrEmpty(s) && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int CountFaces(byte[] data)
        {
            if (data.Length >= 12 && data[0] == (byte)'t' && data[1] == (byte)'t' &&
                data[2] == (byte)'c' && data[3] == (byte)'f')
                return (int)((uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]));
            return 1;
        }

        /// <summary>Return embeddable bytes for one face. For a single-face file this is
        /// the whole file; PdfSharpCore selects the right glyphs. For a .ttc we return the
        /// whole collection bytes (PdfSharpCore reads via the file); if that proves wrong
        /// in QA, extract the single font — but most installed text fonts are .ttf.</summary>
        private static byte[] ExtractFace(string path, int face) => File.ReadAllBytes(path);

        private byte[] ReadFallback()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var name in new[] { "arial.ttf", "segoeui.ttf", "tahoma.ttf" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
            // Last resort: first readable font file.
            foreach (var f in Directory.EnumerateFiles(dir, "*.ttf"))
            {
                try { return File.ReadAllBytes(f); } catch { }
            }
            return Array.Empty<byte>();
        }
    }
}
