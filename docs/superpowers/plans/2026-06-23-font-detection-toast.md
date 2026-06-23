# Font-Detection Toast + Style Preservation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a user edits existing PDF text, preserve Bold/Italic styling and warn (via an auto-dismissing toast) when the original font is not installed.

**Architecture:** Extract the font name/style/availability logic into a pure, unit-tested `Services/FontResolver.cs`. Wire it into the existing text-edit selection path in `MainWindow.xaml.cs` so the resolved family + style flow into the edit `TextBox`, the `TextEditAnnotation` model, and the save-time redraw. Add a reusable `ShowToast` overlay to surface the "font not installed" warning.

**Tech Stack:** C# / .NET Framework 4.8, WPF, PdfPig (text/font metadata), PdfSharpCore (`XFont`/`XFontStyle` redraw on save), xUnit (tests).

## Global Constraints

- Targets `net48`; build requires the .NET 8 SDK. `dotnet` may not be on PATH — use `~/.dotnet/dotnet.exe`.
- `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion=latest`. Use collection expressions `[]`, target-typed `new`, switch expressions to match house style.
- I/O / parsing wrapped in defensive `try { } catch { }` that swallow and fall back — never let exceptions reach the user mid-edit.
- Tests live in `Scalpel.Tests` (xUnit) and **link source files directly** via `<Compile Include="..\Services\...">` — add a link entry for every new tested source file.
- New string keys MUST be added to **all six** locale files: `Strings/en-US.xaml`, `es.xaml`, `zh-TW.xaml`, `zh-CN.xaml`, `bn.xaml`, `tr-TR.xaml`. A missing key blanks the `DynamicResource` in that language.
- The Tabler icon font is a 39-glyph subset — **do not reference a new `Ico_*` glyph** (it won't render without re-subsetting). The toast uses text only, no icon.
- Build gotcha: if a `dotnet build --no-restore` fails `NETSDK1047` after a prior publish, re-run **with** restore (drop `--no-restore`).

---

## File Structure

- **Create** `Services/FontResolver.cs` — pure logic: normalize raw PDF font name → `ResolvedFont(DisplayName, FamilyName, IsBold, IsItalic, IsInstalled)`. No WPF dependency.
- **Create** `Scalpel.Tests/FontResolverTests.cs` — the normalization table + installed-detection tests.
- **Modify** `Scalpel.Tests/Scalpel.Tests.csproj` — link `FontResolver.cs`.
- **Modify** `Models/EditingTypes.cs` — add `IsBold`/`IsItalic` to `TextEditAnnotation`.
- **Modify** `MainWindow.xaml.cs` — selection path (use `FontResolver`, apply style, raise toast), `TextEditContext` (carry style), annotation creation, save redraw (`XFontStyle`), and a new `ShowToast`/`HideToast`/`ToastCopyBtn_Click` + `AvailableFontFamilies()` helper.
- **Modify** `MainWindow.xaml` — add the toast overlay element.
- **Modify** all six `Strings/*.xaml` — add `Str_FontMissing_Body` and `Str_FontMissing_CopyName`.

---

## Task 1: `FontResolver` service (pure logic, TDD)

**Files:**
- Create: `Services/FontResolver.cs`
- Test: `Scalpel.Tests/FontResolverTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj:43` (add link after the `Catalog.cs` line)

**Interfaces:**
- Produces: `record ResolvedFont(string DisplayName, string FamilyName, bool IsBold, bool IsItalic, bool IsInstalled)` and `static ResolvedFont FontResolver.Resolve(string? rawPdfFontName, IReadOnlyCollection<string> availableFamilies)` — both in namespace `Scalpel.Services`.

- [ ] **Step 1: Add the test-project source link**

In `Scalpel.Tests/Scalpel.Tests.csproj`, add after line 30 (`SignatureStore.cs` link), inside the same `<ItemGroup>`:

```xml
    <Compile Include="..\Services\FontResolver.cs" Link="Services\FontResolver.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/FontResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class FontResolverTests
    {
        // A representative set of installed families for deterministic IsInstalled checks.
        private static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase)
        {
            "Times New Roman", "Arial", "Helvetica", "Segoe UI"
        };

        [Theory]
        // raw,                         expDisplay,        expBold, expItalic, expInstalled
        [InlineData("ABCDEF+Minion-BoldItalic", "Minion",          true,  true,  false)]
        [InlineData("TimesNewRomanPSMT",        "Times New Roman", false, false, true)]
        [InlineData("TimesNewRomanPS-BoldMT",   "Times New Roman", true,  false, true)]
        [InlineData("Arial,BoldItalic",         "Arial",           true,  true,  true)]
        [InlineData("Helvetica-Oblique",        "Helvetica",       false, true,  true)]
        [InlineData("ArialMT",                  "Arial",           false, false, true)]
        public void Resolve_ParsesNameStyleAndAvailability(
            string raw, string expDisplay, bool expBold, bool expItalic, bool expInstalled)
        {
            var r = FontResolver.Resolve(raw, Installed);
            Assert.Equal(expDisplay, r.DisplayName);
            Assert.Equal(expBold, r.IsBold);
            Assert.Equal(expItalic, r.IsItalic);
            Assert.Equal(expInstalled, r.IsInstalled);
        }

        [Fact]
        public void Resolve_MissingFont_FallsBackFamilyToSegoeUI_ButKeepsDisplayName()
        {
            var r = FontResolver.Resolve("ABCDEF+Minion-BoldItalic", Installed);
            Assert.Equal("Minion", r.DisplayName);
            Assert.Equal("Segoe UI", r.FamilyName);   // substitute used for actual drawing
            Assert.False(r.IsInstalled);
        }

        [Fact]
        public void Resolve_InstalledFont_FamilyMatchesAvailableCasing()
        {
            var r = FontResolver.Resolve("ArialMT", Installed);
            Assert.Equal("Arial", r.FamilyName);
            Assert.True(r.IsInstalled);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_EmptyOrNull_ReturnsSafeDefault(string? raw)
        {
            var r = FontResolver.Resolve(raw, Installed);
            Assert.Equal("Segoe UI", r.DisplayName);
            Assert.Equal("Segoe UI", r.FamilyName);
            Assert.False(r.IsBold);
            Assert.False(r.IsItalic);
            Assert.True(r.IsInstalled);  // no spurious toast for unknown fonts
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontResolverTests"`
Expected: FAIL to compile — `FontResolver` / `ResolvedFont` do not exist.

- [ ] **Step 4: Implement `FontResolver`**

Create `Services/FontResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>Resolved font info for a run of existing PDF text.</summary>
    public sealed record ResolvedFont(
        string DisplayName,   // cleaned, human-facing name (toast)
        string FamilyName,    // family to apply/draw with; substitute if not installed
        bool IsBold,
        bool IsItalic,
        bool IsInstalled);

    /// <summary>
    /// Normalizes raw PDF font names (PostScript-style, subset-prefixed) into a usable
    /// family name + style flags, and reports whether the family is installed.
    /// Pure and defensive: never throws; unknown input returns a safe default.
    /// </summary>
    public static class FontResolver
    {
        private const string Fallback = "Segoe UI";
        private static readonly string[] BoldTokens   = { "bold", "black", "heavy", "semibold", "demibold" };
        private static readonly string[] ItalicTokens = { "italic", "oblique" };
        private static readonly string[] PsSuffixes   = { "psmt", "mt", "ps" }; // longest first

        public static ResolvedFont Resolve(string? rawPdfFontName, IReadOnlyCollection<string> availableFamilies)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPdfFontName))
                    return new ResolvedFont(Fallback, Fallback, false, false, true);

                string s = rawPdfFontName!.Trim();

                // 1. Strip subset prefix "ABCDEF+" (always 6 upper letters, but be lenient).
                int plus = s.IndexOf('+');
                if (plus >= 0 && plus <= 7) s = s[(plus + 1)..];

                // 2. Split family from style part on ',' or '-'.
                string stylePart = "";
                int sep = s.IndexOfAny(new[] { ',', '-' });
                if (sep >= 0)
                {
                    stylePart = s[(sep + 1)..];
                    s = s[..sep];
                }

                // 3. Detect style across the whole original (handles glued tokens too).
                string lowerAll = (stylePart + " " + s).ToLowerInvariant();
                bool isBold   = BoldTokens.Any(lowerAll.Contains);
                bool isItalic = ItalicTokens.Any(lowerAll.Contains);

                // 4. Drop a trailing PostScript suffix (PSMT/MT/PS) from the family token.
                string fam = s.Trim();
                foreach (var suf in PsSuffixes)
                {
                    if (fam.Length > suf.Length && fam.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                    {
                        fam = fam[..^suf.Length];
                        break;
                    }
                }

                // 5. Spacify CamelCase ("TimesNewRoman" -> "Times New Roman") so it can match
                //    WPF family names. Best-effort; leaves already-spaced or single words intact.
                string display = Spacify(fam.Trim());
                if (string.IsNullOrWhiteSpace(display))
                    return new ResolvedFont(Fallback, Fallback, isBold, isItalic, true);

                // 6. Availability: case-insensitive match against the supplied set.
                string? match = availableFamilies?.FirstOrDefault(
                    f => string.Equals(f, display, StringComparison.OrdinalIgnoreCase));
                bool installed = match is not null;
                string family = installed ? match! : Fallback;

                return new ResolvedFont(display, family, isBold, isItalic, installed);
            }
            catch
            {
                return new ResolvedFont(Fallback, Fallback, false, false, true);
            }
        }

        private static string Spacify(string name)
        {
            if (name.Length == 0 || name.Contains(' ')) return name;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~FontResolverTests"`
Expected: PASS (all theory cases + facts green).

- [ ] **Step 6: Commit**

```bash
git add Services/FontResolver.cs Scalpel.Tests/FontResolverTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat: add FontResolver service with unit tests"
```

---

## Task 2: Style preservation end-to-end

Wire `FontResolver` into the text-edit selection path so Bold/Italic survive editing, replacing the inline name-cleaning block.

**Files:**
- Modify: `Models/EditingTypes.cs:67-75` (`TextEditAnnotation`)
- Modify: `MainWindow.xaml.cs:6418-6438` (selection: resolve + style), `:6444-6467` (TextBox + `TextEditContext`), `:6505-6515` (`TextEditContext` fields), `:6581-6590` (annotation creation), `:8039` (save redraw)
- Modify: `MainWindow.xaml.cs` (add `AvailableFontFamilies()` helper near `SetStatus`)

**Interfaces:**
- Consumes: `FontResolver.Resolve(...)` and `ResolvedFont` from Task 1.
- Produces: `TextEditAnnotation.IsBold`/`IsItalic` (bool), `TextEditContext.IsBold`/`IsItalic` (bool), `MainWindow.AvailableFontFamilies()` → `IReadOnlyCollection<string>`.

- [ ] **Step 1: Add style fields to the model**

In `Models/EditingTypes.cs`, inside `TextEditAnnotation` (after line 74 `FontName`):

```csharp
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
```

- [ ] **Step 2: Add style fields to `TextEditContext`**

In `MainWindow.xaml.cs`, inside `private class TextEditContext` (after line 6512 `FontName`):

```csharp
            public bool IsBold { get; set; }
            public bool IsItalic { get; set; }
```

- [ ] **Step 3: Add the available-families helper**

In `MainWindow.xaml.cs`, immediately after the `SetStatus` method (after line 6:1886) add:

```csharp
        private static IReadOnlyCollection<string>? _availableFamiliesCache;
        /// <summary>System + bundled font families, cached; used for FontResolver availability checks.</summary>
        private static IReadOnlyCollection<string> AvailableFontFamilies()
        {
            if (_availableFamiliesCache is not null) return _availableFamiliesCache;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
                {
                    if (!string.IsNullOrWhiteSpace(ff.Source)) set.Add(ff.Source);
                    foreach (var n in ff.FamilyNames.Values) set.Add(n);
                }
            }
            catch { /* minimal fallback below */ }
            set.Add("Segoe UI");
            _availableFamiliesCache = set;
            return set;
        }
```

(Note: place it in the `MainWindow` partial class body; the `6:` prefix above is the read offset, not a literal.)

- [ ] **Step 4: Replace the inline cleaning block with FontResolver**

In `MainWindow.xaml.cs`, replace the block at lines **6418-6438** (from `// Try to get font name from letter` through the closing `}` of `if (!string.IsNullOrEmpty(rawFont))`) with:

```csharp
                        // Resolve raw PdfPig font name -> family + style + availability.
                        string? rawFont = null;
                        try { rawFont = letter.FontName; } catch { }
                        if (string.IsNullOrEmpty(rawFont))
                        {
                            try { rawFont = firstWord.FontName; } catch { }
                        }
                        var resolved = Scalpel.Services.FontResolver.Resolve(rawFont, AvailableFontFamilies());
                        fontName = resolved.FamilyName;
                        isBold = resolved.IsBold;
                        isItalic = resolved.IsItalic;
                        if (!resolved.IsInstalled)
                            ShowToast(string.Format(Loc("Str_FontMissing_Body"), resolved.DisplayName), resolved.DisplayName);
```

Then declare the two style locals next to `fontName` — change line **6408** from:

```csharp
                string fontName = "Segoe UI"; // fallback
```
to:
```csharp
                string fontName = "Segoe UI"; // fallback
                bool isBold = false, isItalic = false;
```

(`ShowToast` is added in Task 3; it is a no-op stub until then — to keep this task compiling on its own, add a temporary stub at the end of Task 2 Step 5. See note.)

- [ ] **Step 5: Apply style to the edit TextBox and TextEditContext**

In `MainWindow.xaml.cs`, in the `new TextBox { ... }` initializer (around lines 6451-6452), after `FontFamily = new FontFamily(fontName),` add:

```csharp
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal,
```

And in the `Tag = new TextEditContext { ... }` initializer (after `FontName = fontName` at line 6465), add:

```csharp
                        ,IsBold = isBold,
                        IsItalic = isItalic
```

(Adjust the trailing comma so the object initializer stays valid — `FontName = fontName, IsBold = isBold, IsItalic = isItalic`.)

Temporary stub so this task builds independently (remove in Task 3): add near `SetStatus`:

```csharp
        // TEMP stub — replaced by the real toast in Task 3.
        private void ShowToast(string message, string? copyText = null) => SetStatus(message);
```

- [ ] **Step 6: Carry style into the committed annotation**

In `MainWindow.xaml.cs`, in the `new TextEditAnnotation { ... }` at lines 6581-6590, after `FontName = ctx.FontName` (line 6589) add:

```csharp
                    ,IsBold = ctx.IsBold,
                    IsItalic = ctx.IsItalic
```

(Again keep the initializer comma-valid: `FontName = ctx.FontName, IsBold = ctx.IsBold, IsItalic = ctx.IsItalic`.)

- [ ] **Step 7: Apply style at save-time redraw**

In `MainWindow.xaml.cs`, replace line **8039**:

```csharp
                            var editFont = new XFont(tea.FontName, tea.FontSize * sy);
```
with:
```csharp
                            var editStyle = tea.IsBold && tea.IsItalic ? XFontStyle.BoldItalic
                                          : tea.IsBold ? XFontStyle.Bold
                                          : tea.IsItalic ? XFontStyle.Italic
                                          : XFontStyle.Regular;
                            var editFont = new XFont(tea.FontName, tea.FontSize * sy, editStyle);
```

(`XFontStyle` is the PdfSharpCore type already used in `Services/SampleDocument.cs:18`. The `XGraphics`/`XFont` `using` is already present in this file.)

- [ ] **Step 8: Build and run the full test suite**

Run: `~/.dotnet/dotnet.exe build` then `~/.dotnet/dotnet.exe test`
Expected: build succeeds; all tests pass (no new tests here — this is integration; `FontResolverTests` still green).
If `NETSDK1047` appears, re-run `~/.dotnet/dotnet.exe build` (with restore).

- [ ] **Step 9: Manual verification (documented)**

Open a PDF containing bold and italic text, click a bold line, confirm the edit box shows bold; save; reopen and confirm the redrawn text is still bold. Repeat for italic. Record result in the PR description.

- [ ] **Step 10: Commit**

```bash
git add Models/EditingTypes.cs MainWindow.xaml.cs
git commit -m "feat: preserve bold/italic when editing existing PDF text"
```

---

## Task 3: Font-missing toast (overlay UI + localization)

Replace the temporary `ShowToast` stub with a real auto-dismissing overlay card with a copy-name action, and localize its strings.

**Files:**
- Modify: `MainWindow.xaml` (add toast overlay as a root-grid sibling, after the `AboutOverlay` block ~line 1154)
- Modify: `MainWindow.xaml.cs` (replace `ShowToast` stub with real impl + `HideToast` + `ToastCopyBtn_Click`)
- Modify: all six `Strings/*.xaml`

**Interfaces:**
- Consumes: `ShowToast(string message, string? copyText)` call site from Task 2.
- Produces: named XAML elements `ToastHost` (Grid), `ToastText` (TextBlock), `ToastCopyBtn` (Button); handler `ToastCopyBtn_Click`.

- [ ] **Step 1: Add the localized strings to all six files**

Add these two keys to `Strings/en-US.xaml`, `es.xaml`, `zh-TW.xaml`, `zh-CN.xaml`, `bn.xaml`, `tr-TR.xaml` (near the `Str_Ready` entry). English text is used in every file as a placeholder where a translation isn't supplied; the key must exist in all six:

`en-US.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">"{0}" isn't installed — a substitute font will be used.</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">Copy name</sys:String>
```

`es.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">«{0}» no está instalada: se usará una fuente alternativa.</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">Copiar nombre</sys:String>
```

`tr-TR.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">"{0}" yüklü değil — yerine başka bir yazı tipi kullanılacak.</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">Adı kopyala</sys:String>
```

`zh-TW.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">未安裝「{0}」字型，將使用替代字型。</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">複製名稱</sys:String>
```

`zh-CN.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">未安装"{0}"字体，将使用替代字体。</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">复制名称</sys:String>
```

`bn.xaml`:
```xml
    <sys:String x:Key="Str_FontMissing_Body">"{0}" ইনস্টল করা নেই — পরিবর্তে একটি বিকল্প ফন্ট ব্যবহার করা হবে।</sys:String>
    <sys:String x:Key="Str_FontMissing_CopyName">নাম কপি করুন</sys:String>
```

- [ ] **Step 2: Add the toast overlay to MainWindow.xaml**

In `MainWindow.xaml`, add as a sibling of the other overlays (immediately after the `AboutOverlay` closing `</Grid>` near line ~1154; it must be a direct child of the root `<Grid>` opened at line 436):

```xml
        <!-- Font-missing toast -->
        <Grid x:Name="ToastHost"
              Grid.RowSpan="5"
              Panel.ZIndex="10000"
              Visibility="Collapsed"
              Background="{x:Null}">
            <Border Style="{StaticResource StudioOverlayCard}"
                    HorizontalAlignment="Center" VerticalAlignment="Top"
                    Margin="0,16,0,0" Padding="16,10"
                    MaxWidth="520">
                <StackPanel Orientation="Horizontal">
                    <TextBlock x:Name="ToastText"
                               TextWrapping="Wrap"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource TextPrimary}"
                               FontFamily="{DynamicResource FontUI}" FontSize="{DynamicResource FsBody}"/>
                    <Button x:Name="ToastCopyBtn"
                            Content="{DynamicResource Str_FontMissing_CopyName}"
                            Click="ToastCopyBtn_Click"
                            Margin="12,0,0,0"
                            VerticalAlignment="Center"
                            Style="{StaticResource StudioIconButton}"
                            Foreground="{DynamicResource Accent}"
                            FontFamily="{DynamicResource FontUI}" FontSize="{DynamicResource FsBody}"/>
                </StackPanel>
            </Border>
        </Grid>
```

(If `StudioIconButton` styling looks wrong for a text button, fall back to `StudioToolButton` — both exist in `_Shared.xaml`. The card and text are the essential parts.)

- [ ] **Step 3: Replace the ShowToast stub with the real implementation**

In `MainWindow.xaml.cs`, remove the temporary stub added in Task 2 and add:

```csharp
        private System.Windows.Threading.DispatcherTimer? _toastTimer;

        /// <summary>Shows a transient toast banner; auto-dismisses after ~4s. Never throws.</summary>
        private void ShowToast(string message, string? copyText = null)
        {
            try
            {
                ToastText.Text = message;
                ToastCopyBtn.Visibility = string.IsNullOrEmpty(copyText) ? Visibility.Collapsed : Visibility.Visible;
                ToastCopyBtn.Tag = copyText;
                ToastHost.Opacity = 1;
                ToastHost.Visibility = Visibility.Visible;

                _toastTimer?.Stop();
                _toastTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4)
                };
                _toastTimer.Tick += (_, __) => { _toastTimer?.Stop(); HideToast(); };
                _toastTimer.Start();
            }
            catch { /* a missing toast must never break editing */ }
        }

        private void HideToast()
        {
            try
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, __) => ToastHost.Visibility = Visibility.Collapsed;
                ToastHost.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch { ToastHost.Visibility = Visibility.Collapsed; }
        }

        private void ToastCopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ToastCopyBtn.Tag is string s && !string.IsNullOrEmpty(s))
                    Clipboard.SetText(s);
            }
            catch { }
        }
```

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet.exe build`
Expected: succeeds. (If `NETSDK1047`, re-run with restore.)

- [ ] **Step 5: Run the full test suite**

Run: `~/.dotnet/dotnet.exe test`
Expected: all tests pass (`FontResolverTests` green; no regressions).

- [ ] **Step 6: Manual verification (documented)**

Open a PDF whose text uses a font you do NOT have installed (e.g. a document using a commercial font). Click that text. Confirm: a toast appears top-center naming the font and stating a substitute will be used; the "Copy name" button copies the font name to the clipboard; the toast fades out after ~4s. Open a PDF using an installed font (e.g. Arial) and confirm NO toast appears. Record results in the PR.

- [ ] **Step 7: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs Strings/en-US.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml
git commit -m "feat: toast warning when edited text's font is not installed"
```

---

## Self-Review

**Spec coverage:**
- Warn when font not installed → Task 3 toast, raised in Task 2 Step 4. ✓
- Warn-only, never block → editing proceeds regardless of `IsInstalled`. ✓
- Name the font + copy action → `Str_FontMissing_Body` `{0}` + `ToastCopyBtn`. ✓
- Preserve Bold/Italic (preview + save) → Task 2 Steps 5 (TextBox), 7 (redraw). ✓
- Testable `FontResolver` with injected availability → Task 1. ✓
- Localized in all six files → Task 3 Step 1. ✓
- Overlay-only (not mirrored to status bar) → real `ShowToast` uses `ToastHost`, not `SetStatus`. ✓ (the Task 2 stub mirrors to status only as a temporary build aid, removed in Task 3).
- No new Tabler glyph → toast is text-only. ✓

**Type consistency:** `ResolvedFont(DisplayName, FamilyName, IsBold, IsItalic, IsInstalled)` used identically in Task 1 (def) and Task 2 (consume). `TextEditAnnotation.IsBold/IsItalic` and `TextEditContext.IsBold/IsItalic` named consistently across Steps. `ShowToast(string, string?)` signature matches between the Task 2 call site/stub and the Task 3 implementation. `XFontStyle` matches existing usage.

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The only "stub" is an explicit, named temporary in Task 2 removed in Task 3 (documented), so each task builds independently.
