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

        /// <summary>Make this the resolver PdfSharpCore draws with. Never guard this with
        /// <c>GlobalFontSettings.FontResolver is null</c>: that getter never returns null, it
        /// silently installs PdfSharpCore's own system-font resolver on first read, after which
        /// the bundled Geist/Noto faces are unreachable and text burns in with an arbitrary
        /// system font (boxes for any script it lacks). The setter throws once a font has been
        /// used, so call this before any XFont is created. Returns true when it is active.</summary>
        public static bool Install()
        {
            try
            {
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = Instance;
            }
            catch { /* a font was already resolved through another resolver */ }
            try { return ReferenceEquals(PdfSharpCore.Fonts.GlobalFontSettings.FontResolver, Instance); }
            catch { return false; }
        }

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

                // 3. Unknown family → Arial, in its real bold/italic face when installed:
                // PdfSharpCore 1.3.67 does not honour simulated styles, so a simulated bold
                // would come out regular.
                string fbStyled = FaceKey(FallbackFamily, isBold, isItalic);
                if (_systemIndex!.ContainsKey(fbStyled)) return new FontResolverInfo(fbStyled);
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

        /// <summary>True when this exact family + style exists as its own face (bundled or
        /// installed). PdfSharpCore 1.3.67 ignores simulated bold/italic, so callers use this to
        /// decide whether a requested style will really appear. Never throws.</summary>
        public bool HasExactFace(string family, bool bold, bool italic)
        {
            try
            {
                string fam = (family ?? "").Trim();
                if (fam.Length == 0) return false;
                EnsureIndex();
                string key = FaceKey(fam, bold, italic);
                return _bundled.ContainsKey(key) || _systemIndex!.ContainsKey(key);
            }
            catch { return false; }
        }

        /// <summary>True + bytes when <paramref name="family"/> (with style, then regular)
        /// is an EXACT bundled or system-indexed face. Unlike <see cref="GetFont"/> this does
        /// NOT fall back to Arial — so a glyph-coverage check can't be fooled into testing
        /// Arial's glyphs for an unknown family. Never throws.</summary>
        public bool TryGetExactFontBytes(string family, bool bold, bool italic, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                string fam = (family ?? "").Trim();
                if (fam.Length == 0) return false;
                EnsureIndex();
                foreach (var key in new[] { FaceKey(fam, bold, italic), FaceKey(fam, false, false) })
                {
                    if (_bundled.TryGetValue(key, out var bb) && bb.Length > 0) { bytes = bb; return true; }
                    lock (_lock)
                    {
                        if (_systemIndex!.TryGetValue(key, out var loc))
                        {
                            var data = ExtractFace(loc.Path, loc.Face);
                            if (data.Length > 0) { bytes = data; return true; }
                        }
                    }
                }
                return false;
            }
            catch { return false; }
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
                    // Machine-wide fonts, then fonts installed for this user only (Windows 10 1809+),
                    // which WPF sees too - leaving them out made an edit in such a font save in
                    // a substitute with no warning.
                    var files = new List<string>();
                    foreach (var dir in new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Microsoft", "Windows", "Fonts"),
                    })
                    {
                        try { if (Directory.Exists(dir)) files.AddRange(Directory.EnumerateFiles(dir)); }
                        catch { }
                    }
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is not (".ttf" or ".ttc" or ".otf")) continue;
                        try
                        {
                            byte[] data = File.ReadAllBytes(file);
                            // PdfSharpCore can only embed TrueType outlines; a CFF-flavoured
                            // OpenType font ("OTTO") is written under the wrong font type. Leave
                            // it out so the family falls back to a font that embeds correctly.
                            if (data.Length >= 4 && data[0] == (byte)'O' && data[1] == (byte)'T'
                                && data[2] == (byte)'T' && data[3] == (byte)'O') continue;
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

        /// <summary>Return embeddable bytes for one face. A single-face file is returned whole.
        /// A .ttc collection is rebuilt as a standalone font holding just that face: PdfSharpCore
        /// throws "TrueType collection fonts are not yet supported" on the collection itself, and
        /// that failure used to surface mid-save, after the original text was already covered.</summary>
        private static byte[] ExtractFace(string path, int face)
        {
            byte[] data = File.ReadAllBytes(path);
            return TrueTypeCollection.IsCollection(data)
                ? TrueTypeCollection.ExtractFace(data, face) ?? Array.Empty<byte>()
                : data;
        }

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
