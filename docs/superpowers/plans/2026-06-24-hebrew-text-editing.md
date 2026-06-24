# Hebrew Text Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edited and new Hebrew text burns into the PDF in correct visual (bidi) order, right-aligned, using a Hebrew-capable font (original font when it covers Hebrew, else bundled Noto), with Hebrew search verified.

**Architecture:** Two pure testable services — `BidiReorder` (logical→visual run-based reorder) and `TrueTypeCmap` (Hebrew glyph-coverage check) — plus a bundled Noto Sans Hebrew registered through the existing Spec #2 `PdfFontResolver`. A shared `DrawTextRun` helper in `MainWindow` applies reorder + font-choice + right-align at the two burn-in sites. The Latin/LTR path is unchanged.

**Tech Stack:** C# / .NET Framework 4.8, WPF, PdfSharpCore (`XGraphics`/`XFont`), PdfPig (search), xUnit.

## Global Constraints

- Targets `net48`; build requires .NET 8 SDK. `dotnet` may not be on PATH — use `~/.dotnet/dotnet.exe` for build/test.
- `Nullable` + `ImplicitUsings` enabled, `LangVersion=latest`. Use collection expressions `[]`, target-typed `new`, switch expressions.
- Defensive `try { } catch { }` that swallows and falls back — `BidiReorder`/`TrueTypeCmap` never throw (safe defaults: `ToVisual` returns input unchanged; `CoversCodepoint` returns false). A failure must degrade to existing behavior, never break a save.
- All multi-byte values in TrueType files are big-endian.
- Tests live in `Scalpel.Tests` (xUnit), link source via `<Compile Include="..\Services\...">`. Use `\uXXXX` escapes for all Hebrew literals in test code (never paste raw RTL into source — it corrupts visually and is ambiguous).
- Bundled Noto: SIL OFL 1.1, family name "Noto Sans Hebrew", registered via `PdfFontResolver.Instance.RegisterBundledFont`.
- RTL drawn text is RIGHT-ALIGNED to the bounds' right edge when bounds width is known (edits); new text (no stored width) is left-anchored at its placement X but still reordered.
- Hebrew codepoint used for coverage tests: U+05D0 (ALEF).
- Build gotcha: `NETSDK1047` after a prior publish → re-run build WITH restore. MSB3027/MSB3021 pdfium.dll copy error = a running Scalpel.exe locks it (not a code error) — report, don't "fix".

## Known PdfFontResolver internals (from Spec #2, for Task 3)

`PdfFontResolver` (sealed singleton, `Instance`) has: `_bundled` (ConcurrentDictionary faceKey→bytes), `_systemIndex` (Dictionary faceKey→(path,face), lazily built by `EnsureIndex()`), `_lock`, `FaceKey(family,bold,italic)` = `"family|b|i"` lowercased, `ExtractFace(path,face)` reads whole file bytes, `EnsureIndex()`. Task 3 adds `TryGetExactFontBytes`.

---

## File Structure

- **Create** `Services/BidiReorder.cs` — `ContainsRtl`, `ToVisual`.
- **Create** `Services/TrueTypeCmap.cs` — `CoversCodepoint`.
- **Create** `Resources/Fonts/NotoSansHebrew-Regular.ttf` (downloaded).
- **Create** tests: `BidiReorderTests.cs`, `TrueTypeCmapTests.cs`.
- **Modify** `Services/PdfFontResolver.cs` — add `TryGetExactFontBytes`.
- **Modify** `App.xaml.cs` — register Noto in `RegisterPdfFonts`.
- **Modify** `MainWindow.xaml.cs` — `DrawTextRun` + `FontHasHebrew` helpers; wire `:8069` (TextAnnotation) and `:8108` (TextEditAnnotation); edit-box FlowDirection/preview font.
- **Modify** `Scalpel.csproj` — font `<Resource>`.
- **Modify** `Scalpel.Tests/Scalpel.Tests.csproj` — link new sources.
- Test additions for embedding (Task 4) and search (Task 7).

---

## Task 1: `BidiReorder` service (pure, TDD)

**Files:**
- Create: `Services/BidiReorder.cs`
- Test: `Scalpel.Tests/BidiReorderTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj`

**Interfaces:**
- Produces: `Scalpel.Services.BidiReorder.ContainsRtl(string?) -> bool` and `Scalpel.Services.BidiReorder.ToVisual(string?) -> string`.

- [ ] **Step 1: Link the source in the test csproj**

In `Scalpel.Tests/Scalpel.Tests.csproj`, after the `TrueTypeName.cs` link line, add:

```xml
    <Compile Include="..\Services\BidiReorder.cs" Link="Services\BidiReorder.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/BidiReorderTests.cs`:

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class BidiReorderTests
    {
        // shalom = ש ל ו ם  (logical)
        private const string Shalom = "שלום";
        // visual (reversed): ם ו ל ש
        private const string ShalomVisual = "םולש";

        [Theory]
        [InlineData("hello", false)]
        [InlineData("", false)]
        [InlineData("שלום", true)]
        [InlineData("abc של", true)]
        public void ContainsRtl_Detects(string s, bool expected)
            => Assert.Equal(expected, BidiReorder.ContainsRtl(s));

        [Fact]
        public void ToVisual_PureLatin_Unchanged()
            => Assert.Equal("hello world", BidiReorder.ToVisual("hello world"));

        [Fact]
        public void ToVisual_PureHebrew_Reversed()
            => Assert.Equal(ShalomVisual, BidiReorder.ToVisual(Shalom));

        [Fact]
        public void ToVisual_HebrewThenNumber_NumberStaysLtrOnLeft()
        {
            // logical: "shalom 123"  → visual: "123 " + reversed shalom
            string logical = Shalom + " 123";
            string expected = "123 " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_HebrewThenLatinWord_LatinForwardOnLeft()
        {
            // logical: "shalom world" → visual: "world " + reversed shalom
            string logical = Shalom + " world";
            string expected = "world " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_Empty_ReturnsEmpty()
            => Assert.Equal("", BidiReorder.ToVisual(""));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~BidiReorderTests"`
Expected: FAIL to compile — `BidiReorder` does not exist.

- [ ] **Step 4: Implement `BidiReorder`**

Create `Services/BidiReorder.cs`:

```csharp
using System.Collections.Generic;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal run-based bidi reorderer: converts a logical-order string to the visual
    /// (left-to-right glyph) order PdfSharpCore's DrawString needs. Base direction is
    /// RTL when any Hebrew is present. Not a complete UBA — nested embeddings and
    /// directional marks are approximated; covers Hebrew with embedded numbers / Latin
    /// words / punctuation. Pure and defensive: never throws.
    /// </summary>
    public static class BidiReorder
    {
        public static bool ContainsRtl(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s!) if (IsRtl(c)) return true;
            return false;
        }

        public static string ToVisual(string? logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical ?? "";
            try
            {
                if (!ContainsRtl(logical)) return logical!;
                int n = logical!.Length;

                // Raw class: 'R' Hebrew, 'L' strong-LTR letter, 'E' digit, 'N' neutral.
                char[] raw = new char[n];
                for (int i = 0; i < n; i++)
                {
                    char c = logical[i];
                    if (IsRtl(c)) raw[i] = 'R';
                    else if (char.IsDigit(c)) raw[i] = 'E';
                    else if (char.IsLetter(c)) raw[i] = 'L';
                    else raw[i] = 'N';
                }

                // Resolved direction: 'R' or 'L' (digits order LTR → 'L'). Neutrals adopt
                // a shared neighbour direction, else base (RTL).
                char[] res = new char[n];
                for (int i = 0; i < n; i++)
                {
                    if (raw[i] == 'R') res[i] = 'R';
                    else if (raw[i] == 'L' || raw[i] == 'E') res[i] = 'L';
                    else
                    {
                        char left = NearestStrong(raw, i, -1);
                        char right = NearestStrong(raw, i, +1);
                        res[i] = (left != '\0' && left == right) ? left : 'R';
                    }
                }

                // Build maximal same-direction runs in logical order.
                var runs = new List<(char Dir, int Start, int Len)>();
                int s = 0;
                while (s < n)
                {
                    int e = s + 1;
                    while (e < n && res[e] == res[s]) e++;
                    runs.Add((res[s], s, e - s));
                    s = e;
                }

                // Base RTL: emit runs right-to-left; reverse chars in R runs, keep L runs.
                var sb = new StringBuilder(n);
                for (int r = runs.Count - 1; r >= 0; r--)
                {
                    var run = runs[r];
                    if (run.Dir == 'R')
                        for (int i = run.Start + run.Len - 1; i >= run.Start; i--) sb.Append(logical[i]);
                    else
                        for (int i = run.Start; i < run.Start + run.Len; i++) sb.Append(logical[i]);
                }
                return sb.ToString();
            }
            catch { return logical!; }
        }

        private static char NearestStrong(char[] raw, int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < raw.Length; i += dir)
            {
                if (raw[i] == 'R') return 'R';
                if (raw[i] == 'L' || raw[i] == 'E') return 'L';
            }
            return '\0';
        }

        private static bool IsRtl(char c)
            => (c >= '֐' && c <= '׿') || (c >= 'יִ' && c <= 'ﭏ');
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~BidiReorderTests"`
Expected: PASS (all theory + facts).

- [ ] **Step 6: Commit**

```bash
git add Services/BidiReorder.cs Scalpel.Tests/BidiReorderTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: add BidiReorder (logical->visual) service"
```

---

## Task 2: `TrueTypeCmap` coverage check (pure, TDD)

**Files:**
- Create: `Services/TrueTypeCmap.cs`
- Test: `Scalpel.Tests/TrueTypeCmapTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj`

**Interfaces:**
- Produces: `Scalpel.Services.TrueTypeCmap.CoversCodepoint(byte[] data, int codepoint, int faceIndex = 0) -> bool`.

- [ ] **Step 1: Link the source in the test csproj**

In `Scalpel.Tests/Scalpel.Tests.csproj`, after the `BidiReorder.cs` link, add:

```xml
    <Compile Include="..\Services\TrueTypeCmap.cs" Link="Services\TrueTypeCmap.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/TrueTypeCmapTests.cs`:

```csharp
using System;
using System.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TrueTypeCmapTests
    {
        private const int Alef = 0x05D0;
        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }

        [Fact]
        public void SegoeUi_CoversHebrewAlef()
        {
            string p = Path.Combine(FontsDir, "segoeui.ttf");
            Assert.True(File.Exists(p), $"expected {p} on Windows");
            Assert.True(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), Alef));
        }

        [Fact]
        public void SegoeUi_CoversLatinA()
        {
            string p = Path.Combine(FontsDir, "segoeui.ttf");
            Assert.True(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), 'A'));
        }

        [Fact]
        public void Geist_DoesNotCoverHebrew()
        {
            string p = Path.Combine(RepoRoot(), "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(p), $"expected bundled font at {p}");
            Assert.False(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), Alef));
        }

        [Fact]
        public void Garbage_ReturnsFalse()
            => Assert.False(TrueTypeCmap.CoversCodepoint(new byte[] { 1, 2, 3, 4 }, Alef));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TrueTypeCmapTests"`
Expected: FAIL to compile — `TrueTypeCmap` does not exist.

- [ ] **Step 4: Implement `TrueTypeCmap`**

Create `Services/TrueTypeCmap.cs`:

```csharp
using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal TrueType/OpenType 'cmap' reader: does the font map a codepoint to a real
    /// (non-zero) glyph? Supports subtable formats 4 (BMP), 6 (trimmed), and 12 (full).
    /// Handles .ttc via faceIndex. Pure and defensive: returns false on malformed input.
    /// </summary>
    public static class TrueTypeCmap
    {
        public static bool CoversCodepoint(byte[] data, int codepoint, int faceIndex = 0)
        {
            try
            {
                int baseOffset = 0;
                if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t' &&
                    data[2] == (byte)'c' && data[3] == (byte)'f')
                {
                    uint numFonts = ReadU32(data, 8);
                    if (faceIndex < 0 || faceIndex >= numFonts) faceIndex = 0;
                    baseOffset = (int)ReadU32(data, 12 + faceIndex * 4);
                }

                int cmap = FindTable(data, baseOffset);
                if (cmap < 0) return false;

                ushort numTables = ReadU16(data, cmap + 2);
                int best = -1, bestScore = -1;
                for (int i = 0; i < numTables; i++)
                {
                    int rec = cmap + 4 + i * 8;
                    ushort plat = ReadU16(data, rec);
                    ushort enc = ReadU16(data, rec + 2);
                    uint sub = ReadU32(data, rec + 4);
                    int score = Score(plat, enc);
                    if (score > bestScore) { bestScore = score; best = cmap + (int)sub; }
                }
                if (best < 0) return false;

                ushort format = ReadU16(data, best);
                return format switch
                {
                    4 => CoversFormat4(data, best, codepoint),
                    6 => CoversFormat6(data, best, codepoint),
                    12 => CoversFormat12(data, best, codepoint),
                    _ => false
                };
            }
            catch { return false; }
        }

        private static int Score(ushort plat, ushort enc)
        {
            if (plat == 3 && enc == 10) return 5; // Windows UCS-4
            if (plat == 3 && enc == 1) return 4;  // Windows BMP
            if (plat == 0) return 3;              // Unicode
            if (plat == 3 && enc == 0) return 1;  // Symbol
            return 0;
        }

        private static int FindTable(byte[] d, int baseOffset)
        {
            ushort numTables = ReadU16(d, baseOffset + 4);
            int dir = baseOffset + 12;
            for (int i = 0; i < numTables; i++)
            {
                int rec = dir + i * 16;
                if (d[rec] == (byte)'c' && d[rec + 1] == (byte)'m' &&
                    d[rec + 2] == (byte)'a' && d[rec + 3] == (byte)'p')
                    return (int)ReadU32(d, rec + 8);
            }
            return -1;
        }

        private static bool CoversFormat4(byte[] d, int off, int cp)
        {
            if (cp > 0xFFFF) return false;
            ushort segX2 = ReadU16(d, off + 6);
            int endCodes = off + 14;
            int startCodes = endCodes + segX2 + 2; // +2 reservedPad
            int idDeltas = startCodes + segX2;
            int idRangeOffsets = idDeltas + segX2;
            int segCount = segX2 / 2;
            for (int i = 0; i < segCount; i++)
            {
                ushort end = ReadU16(d, endCodes + i * 2);
                if (cp <= end)
                {
                    ushort start = ReadU16(d, startCodes + i * 2);
                    if (cp < start) return false;
                    short idDelta = (short)ReadU16(d, idDeltas + i * 2);
                    ushort idRangeOffset = ReadU16(d, idRangeOffsets + i * 2);
                    int glyph;
                    if (idRangeOffset == 0) glyph = (cp + idDelta) & 0xFFFF;
                    else
                    {
                        int gi = idRangeOffsets + i * 2 + idRangeOffset + (cp - start) * 2;
                        if (gi < 0 || gi + 1 >= d.Length) return false;
                        ushort g = ReadU16(d, gi);
                        if (g == 0) return false;
                        glyph = (g + idDelta) & 0xFFFF;
                    }
                    return glyph != 0;
                }
            }
            return false;
        }

        private static bool CoversFormat6(byte[] d, int off, int cp)
        {
            ushort first = ReadU16(d, off + 6);
            ushort count = ReadU16(d, off + 8);
            if (cp < first || cp >= first + count) return false;
            return ReadU16(d, off + 10 + (cp - first) * 2) != 0;
        }

        private static bool CoversFormat12(byte[] d, int off, int cp)
        {
            uint nGroups = ReadU32(d, off + 12);
            int g = off + 16;
            for (uint i = 0; i < nGroups; i++)
            {
                uint start = ReadU32(d, g);
                uint end = ReadU32(d, g + 4);
                uint startGid = ReadU32(d, g + 8);
                if ((uint)cp >= start && (uint)cp <= end)
                    return startGid + ((uint)cp - start) != 0;
                g += 12;
            }
            return false;
        }

        private static ushort ReadU16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
        private static uint ReadU32(byte[] d, int o) =>
            (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TrueTypeCmapTests"`
Expected: PASS. If `segoeui.ttf` uses a format this reader doesn't handle and a Hebrew check fails, note the actual subtable format from the file and confirm format 4/12 is selected; adjust `Score` if a better Unicode subtable should win.

- [ ] **Step 6: Commit**

```bash
git add Services/TrueTypeCmap.cs Scalpel.Tests/TrueTypeCmapTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: add TrueType cmap glyph-coverage check"
```

---

## Task 3: `PdfFontResolver.TryGetExactFontBytes`

**Files:**
- Modify: `Services/PdfFontResolver.cs`
- Test: `Scalpel.Tests/PdfFontResolverTests.cs` (add cases to the existing class)

**Interfaces:**
- Produces: `bool PdfFontResolver.TryGetExactFontBytes(string family, bool bold, bool italic, out byte[] bytes)` — true + real bytes only for an EXACT bundled/system face (no Arial fallback).

- [ ] **Step 1: Add the failing tests**

In `Scalpel.Tests/PdfFontResolverTests.cs`, add inside the class:

```csharp
        [Fact]
        public void TryGetExactFontBytes_KnownSystemFamily_ReturnsBytes()
        {
            bool ok = PdfFontResolver.Instance.TryGetExactFontBytes("Arial", false, false, out var bytes);
            Assert.True(ok);
            Assert.True(bytes.Length > 1000);
        }

        [Fact]
        public void TryGetExactFontBytes_UnknownFamily_ReturnsFalse()
        {
            bool ok = PdfFontResolver.Instance.TryGetExactFontBytes("NoSuchFontXYZ123", false, false, out var bytes);
            Assert.False(ok);
            Assert.Empty(bytes);
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~PdfFontResolverTests"`
Expected: FAIL to compile — `TryGetExactFontBytes` does not exist.

- [ ] **Step 3: Implement the method**

In `Services/PdfFontResolver.cs`, add this public method after `GetFont` (before the `// ---- internals ----` line):

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~PdfFontResolverTests"`
Expected: PASS (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add Services/PdfFontResolver.cs Scalpel.Tests/PdfFontResolverTests.cs
git commit -m "feat: PdfFontResolver.TryGetExactFontBytes (no fallback)"
```

---

## Task 4: Bundle + register Noto Sans Hebrew

**Files:**
- Create: `Resources/Fonts/NotoSansHebrew-Regular.ttf`
- Modify: `Scalpel.csproj`, `App.xaml.cs`
- Test: `Scalpel.Tests/FontEmbeddingTests.cs` (add a Hebrew case)

**Interfaces:**
- Consumes: `TrueTypeCmap.CoversCodepoint` (Task 2), `RegisterBundledFont` / `HasEmbeddedFontProgram` (Spec #2).
- Produces: a bundled, registered "Noto Sans Hebrew" face.

- [ ] **Step 1: Download the font**

Run (from repo root):

```bash
curl -sL -o Resources/Fonts/NotoSansHebrew-Regular.ttf \
  "https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSansHebrew/hinted/ttf/NotoSansHebrew-Regular.ttf"
```

Verify it's a real static TTF (~26 KB), the family is "Noto Sans Hebrew", and it covers Hebrew, by adding a temporary check OR trusting the Task 4 Step 4 test. Quick sanity: `ls -l Resources/Fonts/NotoSansHebrew-Regular.ttf` shows a non-trivial size (>20000 bytes). If the URL 404s, fall back to:
`https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoSansHebrew/NotoSansHebrew-Regular.ttf`
and report which URL worked.

- [ ] **Step 2: Add the font as a Resource**

In `Scalpel.csproj`, in the `<ItemGroup>` containing the other `<Resource Include="Resources\Fonts\...">` lines (after `Geist-SemiBold.ttf`), add:

```xml
    <Resource Include="Resources\Fonts\NotoSansHebrew-Regular.ttf" />
```

- [ ] **Step 3: Register it at startup**

In `App.xaml.cs`, inside `RegisterPdfFonts()`, after the existing Geist `foreach` loop and BEFORE the `GlobalFontSettings.FontResolver = ...` assignment, add:

```csharp
                try
                {
                    var hUri = new Uri("pack://application:,,,/Resources/Fonts/NotoSansHebrew-Regular.ttf");
                    var hInfo = GetResourceStream(hUri);
                    if (hInfo?.Stream is not null)
                    {
                        using var hsrc = hInfo.Stream;
                        using var hms = new System.IO.MemoryStream();
                        hsrc.CopyTo(hms);
                        Scalpel.Services.PdfFontResolver.Instance
                            .RegisterBundledFont("Noto Sans Hebrew", hms.ToArray(), bold: false, italic: false);
                    }
                }
                catch { /* skip if the Hebrew font resource is missing */ }
```

- [ ] **Step 4: Add the failing embedding + coverage test**

In `Scalpel.Tests/FontEmbeddingTests.cs`, add inside the class:

```csharp
        [Fact]
        public void NotoHebrew_FromRepo_CoversAlef_AndEmbeds()
        {
            EnsureResolver();
            string noto = Path.Combine(RepoRoot(), "Resources", "Fonts", "NotoSansHebrew-Regular.ttf");
            Assert.True(File.Exists(noto), $"expected bundled Hebrew font at {noto}");
            byte[] bytes = File.ReadAllBytes(noto);
            Assert.True(Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, 0x05D0),
                "Noto Sans Hebrew must cover ALEF");

            const string fam = "ScalpelHebrewTestFont";
            PdfFontResolver.Instance.RegisterBundledFont(fam, bytes, false, false);
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_he_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    // visual order is irrelevant to embedding; draw Hebrew glyphs
                    gfx.DrawString("םולש", new XFont(fam, 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path), "Hebrew font should embed");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
```

- [ ] **Step 5: Run tests + build**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontEmbeddingTests"` (passes), then `~/.dotnet/dotnet.exe build` (App.xaml.cs compiles, 0 errors).
Expected: PASS / 0 errors. (`RepoRoot` already exists in `FontEmbeddingTests` from Spec #2.)

- [ ] **Step 6: Commit**

```bash
git add Resources/Fonts/NotoSansHebrew-Regular.ttf Scalpel.csproj App.xaml.cs Scalpel.Tests/FontEmbeddingTests.cs
git commit -m "feat: bundle + register Noto Sans Hebrew; prove it embeds"
```

---

## Task 5: `DrawTextRun` helper + wire both burn-in sites

**Files:**
- Modify: `MainWindow.xaml.cs` (add helpers; change `:8069` TextAnnotation and `:8108` TextEditAnnotation)

**Interfaces:**
- Consumes: `BidiReorder` (T1), `TrueTypeCmap` (T2), `PdfFontResolver.TryGetExactFontBytes` (T3), bundled "Noto Sans Hebrew" (T4).

- [ ] **Step 1: Add the helpers**

In `MainWindow.xaml.cs`, add these private members to the `MainWindow` partial class (place near the burn-in/save region; e.g. just above the method containing the `foreach (var annot in annots)` switch):

```csharp
        /// <summary>True if <paramref name="family"/> (exact face) actually has Hebrew glyphs.</summary>
        private static bool FontHasHebrew(string family, bool bold, bool italic)
        {
            if (Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(family, bold, italic, out var bytes))
                return Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, 0x05D0);
            return false;
        }

        /// <summary>Draw one line of text, handling RTL: reorder to visual order, pick a
        /// Hebrew-capable font (the candidate if it covers Hebrew, else bundled Noto), and
        /// right-align to <paramref name="rightX"/> when it exceeds <paramref name="leftX"/>
        /// (edits with known bounds); otherwise left-align at leftX. LTR text is unchanged.</summary>
        private static void DrawTextRun(XGraphics gfx, string text, string candidateFamily,
            double fontSizePx, XFontStyle style, XBrush brush,
            double leftX, double rightX, double baselineY)
        {
            if (!Scalpel.Services.BidiReorder.ContainsRtl(text))
            {
                gfx.DrawString(text, new XFont(candidateFamily, fontSizePx, style), brush, leftX, baselineY);
                return;
            }
            bool bold = style == XFontStyle.Bold || style == XFontStyle.BoldItalic;
            bool italic = style == XFontStyle.Italic || style == XFontStyle.BoldItalic;
            string family = FontHasHebrew(candidateFamily, bold, italic) ? candidateFamily : "Noto Sans Hebrew";
            var font = new XFont(family, fontSizePx, style);
            string visual = Scalpel.Services.BidiReorder.ToVisual(text);
            double width = gfx.MeasureString(visual, font).Width;
            double x = rightX > leftX ? rightX - width : leftX;
            gfx.DrawString(visual, font, brush, x, baselineY);
        }
```

- [ ] **Step 2: Wire the TextAnnotation (new text) site**

In `MainWindow.xaml.cs`, replace the `case TextAnnotation ta:` body (lines 8069-8082) with:

```csharp
                        case TextAnnotation ta:
                            var lines = ta.Content.Split('\n');
                            double lineH = ta.FontSize * sy * 1.2;
                            double ty = ta.Position.Y * sy + ta.FontSize * sy;
                            var taColor = ta.GetColor();
                            var taBrush = new XSolidBrush(XColor.FromArgb(taColor.A, taColor.R, taColor.G, taColor.B));
                            double taLeft = ta.Position.X * sx;
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrEmpty(line))
                                    DrawTextRun(gfx, line, "Geist", ta.FontSize * sy, XFontStyle.Regular,
                                        taBrush, taLeft, taLeft, ty); // rightX==leftX → left-anchored
                                ty += lineH;
                            }
                            break;
```

- [ ] **Step 3: Wire the TextEditAnnotation (edit) site**

In `MainWindow.xaml.cs`, replace the `case TextEditAnnotation tea:` body (lines 8108-8122) with:

```csharp
                        case TextEditAnnotation tea:
                            // White-out original text area
                            var whiteRect = new XSolidBrush(XColors.White);
                            gfx.DrawRectangle(whiteRect,
                                (tea.OriginalBounds.X - 2) * sx, (tea.OriginalBounds.Y - 2) * sy,
                                (tea.OriginalBounds.Width + 4) * sx, (tea.OriginalBounds.Height + 4) * sy);
                            var editStyle = tea.IsBold && tea.IsItalic ? XFontStyle.BoldItalic
                                          : tea.IsBold ? XFontStyle.Bold
                                          : tea.IsItalic ? XFontStyle.Italic
                                          : XFontStyle.Regular;
                            double etyB = tea.Position.Y * sy + tea.FontSize * sy;
                            double eLeft = tea.OriginalBounds.X * sx;
                            double eRight = (tea.OriginalBounds.X + tea.OriginalBounds.Width) * sx;
                            DrawTextRun(gfx, tea.NewContent, tea.FontName, tea.FontSize * sy, editStyle,
                                XBrushes.Black, eLeft, eRight, etyB);
                            break;
```

- [ ] **Step 4: Build + full suite**

Run: `~/.dotnet/dotnet.exe build` (0 errors), then `~/.dotnet/dotnet.exe test` (all green).
Expected: build 0 errors; suite green. If `NETSDK1047`, re-run build with restore.

- [ ] **Step 5: Manual verification (documented)**

In the app: edit an existing Hebrew line → confirm it reads correctly (visual order), is right-aligned in its box, and keeps the original font when that font has Hebrew (else Noto). Add a new text annotation with Hebrew → reads correctly + embeds. Type Hebrew with a number ("שלום 123") → number on the left, Hebrew on the right. Confirm a Latin annotation is visually unchanged from before. Record in the PR.

- [ ] **Step 6: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: bidi-reorder + Hebrew font + right-align for drawn text"
```

---

## Task 6: WPF edit-box entry polish (existing Hebrew)

**Files:**
- Modify: `MainWindow.xaml.cs` (the edit `TextBox` built for existing-text editing, ~`:6444`)

**Interfaces:**
- Consumes: `BidiReorder.ContainsRtl` (T1).

- [ ] **Step 1: Set FlowDirection + Hebrew preview font when editing Hebrew**

In `MainWindow.xaml.cs`, locate the `new TextBox { ... }` built when a user clicks existing text to edit (the one whose `Text = lineText` and `Tag = new TextEditContext { ... }`, around line 6444). Immediately AFTER that TextBox is created and added (after `_activeTextBox = tb;` / before focus), add:

```csharp
                if (Scalpel.Services.BidiReorder.ContainsRtl(lineText))
                {
                    tb.FlowDirection = FlowDirection.RightToLeft;
                    if (!FontHasHebrew(fontName, isBold, isItalic))
                        tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew");
                }
```

(Use the actual local variable names present at that site — confirm `tb`, `lineText`, `fontName`, `isBold`, `isItalic` exist there from Spec #1; if a name differs, adapt. `FontHasHebrew` is the Task 5 helper.)

- [ ] **Step 2: Build + full suite**

Run: `~/.dotnet/dotnet.exe build` then `~/.dotnet/dotnet.exe test`
Expected: 0 errors; suite green.

- [ ] **Step 3: Manual verification (documented)**

Click an existing Hebrew line to edit → the edit box is right-aligned (RTL) and shows Hebrew glyphs (not boxes). A Latin line is unaffected (LTR). Record in the PR.

- [ ] **Step 4: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: RTL flow direction + Hebrew preview font in the edit box"
```

---

## Task 7: Hebrew search verification test

**Files:**
- Test: `Scalpel.Tests/SearchServiceTests.cs` (add a case)

**Interfaces:**
- Consumes: `SearchService` (existing). Consumes the registered Noto via the embedding resolver.

- [ ] **Step 1: Confirm the SearchService public method**

Read `Services/SearchService.cs` and note the exact public search method signature (name, params: file path + query, return type). The test below assumes a method that takes a file path and a query string and returns a collection of match boxes/results; ADAPT the call to the real signature.

- [ ] **Step 2: Add the failing test**

In `Scalpel.Tests/SearchServiceTests.cs`, add (adapting `SearchService.<Method>` to the real API found in Step 1):

```csharp
        [Fact]
        public void Search_FindsHebrewWord_InLogicalOrderPdf()
        {
            // Build a PDF that stores a Hebrew word in logical order (no bidi reorder),
            // which is how real Hebrew PDFs store text and what PdfPig extracts.
            FontEmbeddingTestsEnsureResolver();
            string word = "שלום"; // shalom (logical)
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"scalpel_hesearch_{System.Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharpCore.Pdf.PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                    gfx.DrawString(word, new PdfSharpCore.Drawing.XFont("Noto Sans Hebrew", 20),
                        PdfSharpCore.Drawing.XBrushes.Black, new PdfSharpCore.Drawing.XPoint(72, 72));
                    doc.Save(path);
                }
                // ADAPT to the real SearchService API confirmed in Step 1:
                var results = new Scalpel.Services.SearchService().Search(path, word);
                Assert.NotEmpty(results);
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        // Idempotent global resolver setup, mirroring FontEmbeddingTests.EnsureResolver,
        // so this test can register/use the Noto font headlessly.
        private static void FontEmbeddingTestsEnsureResolver()
        {
            if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is null)
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = Scalpel.Services.PdfFontResolver.Instance;
            // ensure Noto registered for this headless test
            string noto = System.IO.Path.Combine(RepoRootForSearch(), "Resources", "Fonts", "NotoSansHebrew-Regular.ttf");
            if (System.IO.File.Exists(noto))
                Scalpel.Services.PdfFontResolver.Instance.RegisterBundledFont(
                    "Noto Sans Hebrew", System.IO.File.ReadAllBytes(noto), false, false);
        }

        private static string RepoRootForSearch()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
```

Mark the test class `[Collection("FontResolver")]` if it isn't already (it mutates the global resolver). If `SearchServiceTests` is in a different collection, move this single test into a class that is in the `"FontResolver"` collection to avoid the global-state race.

- [ ] **Step 3: Run + adapt**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~SearchServiceTests"`
Expected: PASS. If the Hebrew word is not found, PdfPig may extract the text differently than drawn (e.g. via the ToUnicode map) — investigate what `page.GetWords()` returns for the saved PDF and either adjust the query normalization or document the finding. If search genuinely cannot match drawn Hebrew, record it as the known limitation from the spec (search targets real logical-order PDFs; not text we draw) and convert the test to assert the documented behavior, noting it clearly.

- [ ] **Step 4: Commit**

```bash
git add Scalpel.Tests/SearchServiceTests.cs
git commit -m "test: verify Hebrew search on a logical-order PDF"
```

---

## Self-Review

**Spec coverage:**
- BidiReorder (logical→visual, run-based) → Task 1. ✓
- Hebrew glyph-coverage check → Task 2. ✓
- `TryGetExactFontBytes` (original-font-first, no Arial fallback) → Task 3. ✓
- Bundle + register Noto Sans Hebrew → Task 4. ✓
- Drawn-text integration (reorder + font choice + right-align), both burn-in sites, Latin unchanged → Task 5. ✓
- WPF entry polish (edit box RTL + Hebrew preview font) → Task 6. ✓
- Search verification + documented limitation → Task 7. ✓
- Embedding guarantee for Hebrew → Task 4 Step 4. ✓
- Never-throw pure services → Tasks 1/2 catch + safe defaults, tested with garbage input. ✓
- Right-align edits / left-anchor new text → Task 5 `DrawTextRun` (`rightX > leftX`). ✓

**Type consistency:** `BidiReorder.ContainsRtl/ToVisual`, `TrueTypeCmap.CoversCodepoint(byte[],int,int)`, `PdfFontResolver.TryGetExactFontBytes(string,bool,bool,out byte[])`, `FontHasHebrew(string,bool,bool)`, `DrawTextRun(XGraphics,string,string,double,XFontStyle,XBrush,double,double,double)`, bundled family literal `"Noto Sans Hebrew"`, ALEF `0x05D0` — consistent across tasks.

**Placeholder scan:** No TBD/TODO; complete code in every code step. The two genuine unknowns — the exact `SearchService` API (Task 7 Step 1) and whether PdfPig matches drawn Hebrew (Task 7 Step 3) — are framed as verify-and-adapt gates with a documented fallback, not deferred work. The Latin path in Task 5 is preserved (LTR branch draws exactly as before, just via the helper).

**Risk notes for the executor:**
- Task 5 changes the hot save/burn-in path. The LTR branch of `DrawTextRun` must be behavior-identical to the old `DrawString` calls (same font family, same left x, same baseline) — verify Latin output is unchanged in manual QA.
- Tests that touch `GlobalFontSettings` must stay in the `[Collection("FontResolver")]` collection (Spec #2 convention) — applies to the Task 7 test.
- Noto download (Task 4 Step 1) needs network; if offline, the task is blocked — report it.
