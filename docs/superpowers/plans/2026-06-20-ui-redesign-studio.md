# KillerPDF "Studio" UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace KillerPDF's single-row icon-soup toolbar with a task-based mode-tab layout (View/Edit/Pages/Sign) and apply the "Studio" visual language (Geist font, Tabler icons, amber accent) across all six themes, overlays, and dialogs.

**Architecture:** WPF monolith — `MainWindow.xaml` (~1318 lines) + `MainWindow.xaml.cs` (~9200 lines, one `partial class`). The redesign is concentrated in XAML structure, a new shared style dictionary, rewritten theme dictionaries, two bundled fonts, and **additive** code-behind for mode switching. Existing Click/Checked handlers are reused verbatim — controls are regrouped, not rewritten. PDF pipelines, installer, integrity check, and persistence are untouched.

**Tech Stack:** .NET Framework 4.8 (net48), WPF, C# (Nullable + ImplicitUsings, LangVersion=latest), Costura single-file publish. Build needs .NET 8+ SDK.

**Design spec:** `docs/superpowers/specs/2026-06-20-ui-redesign-studio-design.md` (authoritative for tokens, type scale, control mapping).

## Global Constraints

- Target framework **net48**, x64. Build with `dotnet build`; if `NETSDK1047` appears after a prior publish, re-run **with restore** (drop `--no-restore`).
- C# style: collection expressions `[]`, target-typed `new`, `switch` expressions, `Nullable` enabled. Match surrounding code.
- I/O / PDF parsing stays wrapped in swallowing `try { } catch { }` with fallbacks — never let exceptions reach the user mid-edit.
- **Theme contract:** color brushes live ONLY in `Themes/*.xaml` (merged dict index 0), referenced via `DynamicResource`. `ThemeManager.LoadDict` iterates the *new* dict's keys for in-place per-key update — so **every key must exist in all six theme files** or it silently won't update on switch. Never structurally add/remove merged dict 0/1.
- **Locale contract:** strings live ONLY in `Strings/*.xaml` (merged dict index 1, replaced wholesale). **Every string key must exist in all six locale files** (en-US, es, zh-TW, zh-CN, bn, tr-TR) or a `DynamicResource` lookup blanks out in that language.
- Merged dictionary indices: **[0] = theme, [1] = strings** are load-bearing (`ThemeManager` uses `merged[0]`, `LocaleManager` uses `merged[1]`). Any new app-level dictionary must be added at index ≥ 2, or as direct (non-merged) entries on the root `Application.Resources` dictionary.
- Keep the `Segoe UI Variable, Segoe UI` / `Segoe MDL2 Assets` fallback path so the app is never unusable if font embedding misbehaves.
- Destructive actions (`DangerRed`) stay red in every theme.
- After each task: `dotnet build` succeeds AND `dotnet test` stays green before committing.

---

## File Structure

**New files:**
- `Resources/Fonts/Geist-Regular.ttf`, `Geist-Medium.ttf`, `Geist-SemiBold.ttf` — bundled UI font.
- `Resources/Fonts/tabler-icons.ttf` — bundled icon font.
- `Themes/_Shared.xaml` — non-color styles (buttons, mode tabs, pills, swatches, scrollbar, context menu, overlay card) + `FontUI`/`FontIcon` `FontFamily` resources + `Ico_*` glyph string map. Merged at index ≥ 2.

**Modified files:**
- `KillerPDF.csproj` — add font `Resource` items.
- `App.xaml` — merge `_Shared.xaml` at index 2.
- `Themes/Dark.xaml`, `Light.xaml`, `HighContrast.xaml`, `Blood.xaml`, `Greed.xaml`, `Cyanotic.xaml` — full Studio token sets.
- `MainWindow.xaml` — toolbar region (Grid.Row 1) restructured into tab strip + persistent groups + mode panels + contextual host; title/sidebar/status/overlays restyled.
- `MainWindow.xaml.cs` — add `AppMode` enum + `SetMode`; restyle code-behind-built bars (`SettingsBar`/`CropConfirmBar`/`SearchBar`); move view-mode wiring to View tab.
- `Strings/*.xaml` (×6) — new mode/label keys; remove View-Mode-in-Settings keys (repurpose for View tab).

**Current layout anchors (verified):**
- `MainWindow.xaml`: Grid.Row 0 = title bar (`544`), Row 1 = toolbar (`562`), Row 2 = content/sidebar/preview (`619`), Row 3 = status bar (`916`). Settings overlay `973`, Shortcut overlay `1116`, About overlay `1213`, Grain overlay `1304`.
- Tool buttons (Row 1): `ToolSelectBtn` `585`, `ToolTextBtn` `586`, `ToolHighlightBtn` `587`, `ToolDrawBtn` `588`, `ToolCropBtn` `589`, `ToolImageBtn` `590`, `ToolSignatureBtn` `592`. Zoom: `ZoomOutBtn` `596`, `ZoomBox` `598`, `ZoomInBtn` `612`.
- Settings-overlay view-mode radios (`1095`–`1110`): `ViewContinuousRadio`/`ViewSingleRadio`/`ViewTwoPageRadio`/`ViewGridRadio` → handlers `ViewSingleRadio_Checked` (cb `351`), `ViewContinuousRadio_Checked` (`352`), `ViewTwoPageRadio_Checked` (`353`), `ViewGridRadio_Checked` (`354`), all calling `SetViewMode` (cb `8758`).
- Tool handlers (cb `3179`–`3185`): `ToolSelect_Click`, `ToolText_Click`, `ToolHighlight_Click`, `ToolDraw_Click`, `ToolImage_Click`, `ToolCrop_Click`, `ToolSignature_Click`.
- File/page handlers (XAML `567`–`595`): `New_Click`, `Open_Click`, `CloseFile_Click` (btn `CloseFileBtn`), `Save_Click` (btn `SaveAsBtn`), `SaveFlattened_Click`, `Print_Click`, `Merge_Click`, `Split_Click`, `Delete_Click`, `MoveUp_Click`, `MoveDown_Click`, `Undo_Click`, `ClearAnnotations_Click`.
- Code-behind-built bars: `SettingsBar` (tool settings, cb ~`3883`–`4173`), `CropConfirmBar` (cb ~`2980`–`5359`), `SearchBar` (cb ~`5742`–`5852`). These are constructed in C# — restyle in C#, not XAML.

---

## Phase 0 — Foundations

### Task 1: Bundle Geist + Tabler fonts

**Files:**
- Create: `Resources/Fonts/Geist-Regular.ttf`, `Geist-Medium.ttf`, `Geist-SemiBold.ttf`, `tabler-icons.ttf`
- Modify: `KillerPDF.csproj`
- Create: `Themes/_Shared.xaml`
- Modify: `App.xaml`

**Interfaces:**
- Produces: `FontFamily` resources `FontUI` and `FontIcon`; `ResourceDictionary` `Themes/_Shared.xaml` merged at app index 2.

- [x] **Step 1: Fonts are already downloaded** (done during planning, committed). `Resources/Fonts/` contains `Geist-Regular.ttf` (~126 KB), `Geist-Medium.ttf` (~127 KB), `Geist-SemiBold.ttf` (~128 KB), and `tabler-icons.ttf` (**12 KB — already subset** to the 36 glyphs in Task 2's map via `fontTools.subset`, GSUB/GPOS dropped). If you re-subset, the unicode list is in the Task 2 map. Just verify the four files exist.

- [x] **Step 2: Family names confirmed.** Geist resolves as a single WPF family **`Geist`** with weights 400/500/600 (Medium/SemiBold carry typographic-family nameID 16 = `Geist`), so `#Geist` + `FontWeight="Medium"/"SemiBold"` works. Tabler family name is **`tabler-icons`**. The `pack` URIs below use these exact names.

- [ ] **Step 3: Add fonts as Resource items.** In `KillerPDF.csproj`, inside an `<ItemGroup>`:

```xml
<ItemGroup>
  <Resource Include="Resources\Fonts\Geist-Regular.ttf" />
  <Resource Include="Resources\Fonts\Geist-Medium.ttf" />
  <Resource Include="Resources\Fonts\Geist-SemiBold.ttf" />
  <Resource Include="Resources\Fonts\tabler-icons.ttf" />
</ItemGroup>
```

- [ ] **Step 4: Create `Themes/_Shared.xaml`** with the font families (styles added in later tasks):

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <!-- Bundled fonts. The "#FamilyName" suffix must match the TTF's internal family name. -->
    <FontFamily x:Key="FontUI">pack://application:,,,/Resources/Fonts/#Geist, Segoe UI Variable, Segoe UI</FontFamily>
    <FontFamily x:Key="FontIcon">pack://application:,,,/Resources/Fonts/#tabler-icons, Segoe MDL2 Assets</FontFamily>

    <!-- Type scale (Studio) -->
    <sys:Double x:Key="FsDialogTitle">16</sys:Double>
    <sys:Double x:Key="FsTab">13</sys:Double>
    <sys:Double x:Key="FsButton">12.5</sys:Double>
    <sys:Double x:Key="FsBody">13</sys:Double>
    <sys:Double x:Key="FsContext">11.5</sys:Double>
    <sys:Double x:Key="FsSidebarLabel">10</sys:Double>
    <sys:Double x:Key="FsStatus">10.5</sys:Double>

</ResourceDictionary>
```

- [ ] **Step 5: Merge `_Shared.xaml` at index 2** in `App.xaml` (after theme [0] and strings [1]):

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Themes/Dark.xaml"/>
    <ResourceDictionary Source="Strings/en-US.xaml"/>
    <ResourceDictionary Source="Themes/_Shared.xaml"/>
</ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 6: Verify fonts load.** Temporarily set the title bar app-name `TextBlock` (find it in `MainWindow.xaml` Row 0, ~`544`–`556`) to `FontFamily="{StaticResource FontUI}"` and one chrome button to `FontFamily="{StaticResource FontIcon}" Content="&#xea0f;"` (Tabler "file" glyph). Run `dotnet build`; then run the app (use the `run` skill) and confirm Geist text renders and the Tabler glyph (not a box) shows. Revert the temporary edits.

- [ ] **Step 7: Run build + tests.** Run: `dotnet build` → succeeds. Run: `dotnet test` → green.

- [ ] **Step 8: Commit.**

```bash
git add Resources/Fonts KillerPDF.csproj Themes/_Shared.xaml App.xaml
git commit -m "Bundle Geist + Tabler fonts and _Shared resource dictionary"
```

---

### Task 2: Tabler icon glyph map

**Files:**
- Modify: `Themes/_Shared.xaml`

**Interfaces:**
- Produces: `x:String` resources keyed `Ico_<Name>` holding Tabler codepoints, used by XAML (`Content="{StaticResource Ico_Save}"`) and code-behind (`(string)FindResource("Ico_Save")`).

- [x] **Step 1: Codepoints verified** against `tabler-icons.css` 3.7.0 during planning. The values in Step 2 are the real, verified codepoints and exactly match the glyphs kept in the subset `tabler-icons.ttf`. Do not change them without re-subsetting.

- [ ] **Step 2: Add the glyph map to `_Shared.xaml`** (verified Tabler 3.7.0 codepoints — these are the 36 glyphs the subset font contains):

```xml
<!-- Icon glyph map (Tabler 3.7.0, verified). These 36 codepoints == the subset tabler-icons.ttf. -->
<sys:String x:Key="Ico_New">&#xeaa0;</sys:String>          <!-- file-plus -->
<sys:String x:Key="Ico_Open">&#xeaad;</sys:String>         <!-- folder -->
<sys:String x:Key="Ico_Close">&#xeaa3;</sys:String>        <!-- file-x -->
<sys:String x:Key="Ico_Save">&#xeb62;</sys:String>         <!-- device-floppy -->
<sys:String x:Key="Ico_Flatten">&#xeb2d;</sys:String>      <!-- stack -->
<sys:String x:Key="Ico_Print">&#xeb0e;</sys:String>        <!-- printer -->
<sys:String x:Key="Ico_Merge">&#xedaf;</sys:String>        <!-- arrows-join -->
<sys:String x:Key="Ico_Extract">&#xede9;</sys:String>      <!-- file-export -->
<sys:String x:Key="Ico_InsertPage">&#xeaa0;</sys:String>   <!-- file-plus -->
<sys:String x:Key="Ico_Delete">&#xeb41;</sys:String>       <!-- trash -->
<sys:String x:Key="Ico_MoveUp">&#xea25;</sys:String>       <!-- arrow-up -->
<sys:String x:Key="Ico_MoveDown">&#xea16;</sys:String>     <!-- arrow-down -->
<sys:String x:Key="Ico_Rotate">&#xeb15;</sys:String>       <!-- rotate-clockwise -->
<sys:String x:Key="Ico_Select">&#xf265;</sys:String>       <!-- pointer -->
<sys:String x:Key="Ico_Text">&#xebc5;</sys:String>        <!-- typography -->
<sys:String x:Key="Ico_Highlight">&#xef3f;</sys:String>    <!-- highlight -->
<sys:String x:Key="Ico_Draw">&#xeb04;</sys:String>        <!-- pencil -->
<sys:String x:Key="Ico_Image">&#xeb0a;</sys:String>       <!-- photo -->
<sys:String x:Key="Ico_Crop">&#xea85;</sys:String>        <!-- crop -->
<sys:String x:Key="Ico_Signature">&#xeee0;</sys:String>   <!-- signature -->
<sys:String x:Key="Ico_FillForm">&#xee8f;</sys:String>    <!-- forms -->
<sys:String x:Key="Ico_Undo">&#xeb77;</sys:String>        <!-- arrow-back-up -->
<sys:String x:Key="Ico_Clear">&#xeb8b;</sys:String>       <!-- eraser -->
<sys:String x:Key="Ico_ZoomIn">&#xeb56;</sys:String>      <!-- zoom-in -->
<sys:String x:Key="Ico_ZoomOut">&#xeb57;</sys:String>     <!-- zoom-out -->
<sys:String x:Key="Ico_Search">&#xeb1c;</sys:String>      <!-- search -->
<sys:String x:Key="Ico_Settings">&#xeb20;</sys:String>    <!-- settings -->
<sys:String x:Key="Ico_Shortcuts">&#xebd6;</sys:String>   <!-- keyboard -->
<sys:String x:Key="Ico_View">&#xea9a;</sys:String>        <!-- eye -->
<sys:String x:Key="Ico_Single">&#xeaa4;</sys:String>      <!-- file -->
<sys:String x:Key="Ico_Continuous">&#xead8;</sys:String>  <!-- layout-rows -->
<sys:String x:Key="Ico_TwoPage">&#xeb83;</sys:String>     <!-- columns -->
<sys:String x:Key="Ico_Grid">&#xedba;</sys:String>       <!-- layout-grid -->
<sys:String x:Key="Ico_Fit">&#xea28;</sys:String>        <!-- arrows-maximize -->
<sys:String x:Key="Ico_Chevron">&#xea5f;</sys:String>    <!-- chevron-down -->
<sys:String x:Key="Ico_Min">&#xeaf2;</sys:String>        <!-- minus -->
<sys:String x:Key="Ico_Max">&#xeb2c;</sys:String>        <!-- square -->
<sys:String x:Key="Ico_WinClose">&#xeaa3;</sys:String>   <!-- file-x (reuse) -->
```

- [ ] **Step 3: Build + tests.** Run: `dotnet build` → succeeds. `dotnet test` → green.

- [ ] **Step 4: Commit.**

```bash
git add Themes/_Shared.xaml
git commit -m "Add Tabler icon glyph map to _Shared resources"
```

---

### Task 3: Rewrite the six theme dictionaries with Studio tokens

**Files:**
- Modify: `Themes/Dark.xaml`, `Light.xaml`, `HighContrast.xaml`, `Blood.xaml`, `Greed.xaml`, `Cyanotic.xaml`

**Interfaces:**
- Produces: the full token key set (existing keys + new: `BgControl`, `AccentText`, `AccentBorder`) defined identically-named in all six files. Consumed by every `DynamicResource` in MainWindow + _Shared styles.

- [ ] **Step 1: Define the canonical key list.** The full set every theme MUST define: `BgCanvas`, `BgDark`, `BgPanel`, `BgSidebar`, `BgModal`, `BgHover`, `BgControl`, `BorderDim`, `Accent`, `AccentText`, `AccentDim`, `AccentBorder`, `SelectionAccent`, `AccentLogo`, `TextPrimary`, `TextSecondary`, `TextDim`, `TextFooter`, `DangerRed`, `BgDragHandle`, `BgScrollThumb`, `BgOverlay`, `GrainOpacity`, plus the four `SystemColors.*` highlight brush keys already present in `Dark.xaml` (`28`–`32`). Keep all keys currently in `Dark.xaml` so nothing that references them breaks.

- [ ] **Step 2: Write `Themes/Dark.xaml` (Studio Dark, default).** Replace the brush values:

```xml
<SolidColorBrush x:Key="BgCanvas"      Color="#0A0B0E" po:Freeze="True"/>
<SolidColorBrush x:Key="BgDark"        Color="#14161A" po:Freeze="True"/>
<SolidColorBrush x:Key="BgPanel"       Color="#181B21" po:Freeze="True"/>
<SolidColorBrush x:Key="BgSidebar"     Color="#0D0F12" po:Freeze="True"/>
<SolidColorBrush x:Key="BgModal"       Color="#14161A" po:Freeze="True"/>
<SolidColorBrush x:Key="BgHover"       Color="#2A2E36" po:Freeze="True"/>
<SolidColorBrush x:Key="BgControl"     Color="#23272F" po:Freeze="True"/>
<SolidColorBrush x:Key="BorderDim"     Color="#20242B" po:Freeze="True"/>
<SolidColorBrush x:Key="Accent"        Color="#F2A93B" po:Freeze="True"/>
<SolidColorBrush x:Key="AccentText"    Color="#F6C170" po:Freeze="True"/>
<SolidColorBrush x:Key="AccentDim"     Color="#36280D" po:Freeze="True"/>
<SolidColorBrush x:Key="AccentBorder"  Color="#B07515" po:Freeze="True"/>
<SolidColorBrush x:Key="SelectionAccent" Color="#F2A93B" po:Freeze="True"/>
<SolidColorBrush x:Key="AccentLogo"    Color="#F2A93B" po:Freeze="True"/>
<SolidColorBrush x:Key="TextPrimary"   Color="#E7E9EE" po:Freeze="True"/>
<SolidColorBrush x:Key="TextSecondary" Color="#7C818C" po:Freeze="True"/>
<SolidColorBrush x:Key="TextDim"       Color="#5B616C" po:Freeze="True"/>
<SolidColorBrush x:Key="TextFooter"    Color="#5B616C" po:Freeze="True"/>
<SolidColorBrush x:Key="DangerRed"     Color="#EF4444" po:Freeze="True"/>
<SolidColorBrush x:Key="BgDragHandle"  Color="#3A3F47" po:Freeze="True"/>
<SolidColorBrush x:Key="BgScrollThumb" Color="#5BF2A93B" po:Freeze="True"/>
<SolidColorBrush x:Key="BgOverlay"     Color="#BB000000" po:Freeze="True"/>
<sys:Double x:Key="GrainOpacity">0.12</sys:Double>
<!-- keep the four SystemColors.* highlight brushes; retune to accent-dim/accent: -->
<SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"     Color="#36280D" po:Freeze="True"/>
<SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#F6C170" po:Freeze="True"/>
<SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#36280D" po:Freeze="True"/>
<SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#F6C170" po:Freeze="True"/>
```

- [ ] **Step 3: Write the other five themes** with the SAME keys, these palettes:

**Light** (`BgCanvas #DFE3E8`, `BgDark #F6F7F9`, `BgPanel #FDFDFE`, `BgSidebar #ECEEF1`, `BgModal #FFFFFF`, `BgHover #E4E8EC`, `BgControl #EEF0F3`, `BorderDim #E0E3E8`, `Accent #F2A93B`, `AccentText #9A6B14`, `AccentDim #FBEAD0`, `AccentBorder #E0B36A`, `SelectionAccent #F2A93B`, `AccentLogo #C07F12`, `TextPrimary #1A1D22`, `TextSecondary #525A64`, `TextDim #9AA1AB`, `TextFooter #9AA1AB`, `DangerRed #DC2626`, `BgDragHandle #C4CAD2`, `BgScrollThumb #66C07F12`, `BgOverlay #66000000`, `GrainOpacity 0.05`; SystemColors highlight = `#FBEAD0`/`#9A6B14`).

**HighContrast** (`BgCanvas #000000`, `BgDark #000000`, `BgPanel #0A0A0A`, `BgSidebar #000000`, `BgModal #000000`, `BgHover #1F1F1F`, `BgControl #141414`, `BorderDim #FFFFFF`, `Accent #FFB000`, `AccentText #FFB000`, `AccentDim #2A1D00`, `AccentBorder #FFB000`, `SelectionAccent #FFB000`, `AccentLogo #FFB000`, `TextPrimary #FFFFFF`, `TextSecondary #E0E0E0`, `TextDim #BFBFBF`, `TextFooter #BFBFBF`, `DangerRed #FF5555`, `BgDragHandle #FFFFFF`, `BgScrollThumb #99FFB000`, `BgOverlay #DD000000`, `GrainOpacity 0`; highlight = `#2A1D00`/`#FFB000`).

**Blood** (Studio dark bgs as in Dark, but `Accent #EF4444`, `AccentText #F87171`, `AccentDim #3A1414`, `AccentBorder #7F1D1D`, `SelectionAccent #EF4444`, `AccentLogo #EF4444`, `BgScrollThumb #5BEF4444`; highlight = `#3A1414`/`#F87171`).

**Greed** (Studio dark bgs, `Accent #22C55E`, `AccentText #4ADE80`, `AccentDim #0D2C1B`, `AccentBorder #166534`, `SelectionAccent #22C55E`, `AccentLogo #22C55E`, `BgScrollThumb #5B22C55E`; highlight = `#0D2C1B`/`#4ADE80`).

**Cyanotic** (Studio dark bgs, `Accent #22D3EE`, `AccentText #67E8F9`, `AccentDim #0C2A30`, `AccentBorder #155E75`, `SelectionAccent #22D3EE`, `AccentLogo #22D3EE`, `BgScrollThumb #5B22D3EE`; highlight = `#0C2A30`/`#67E8F9`).

- [ ] **Step 4: Verify key parity.** Run this check — all six files must list the identical key set:

```bash
cd "C:/Code/Personal/KillerPDF" && for f in Themes/Dark Themes/Light Themes/HighContrast Themes/Blood Themes/Greed Themes/Cyanotic; do echo "== $f =="; grep -oE 'x:Key="[^"]+"' "$f.xaml" | sort | md5sum; done
```

Expected: every theme prints the **same md5sum** (identical key sets). If any differs, add the missing keys.

- [ ] **Step 5: Build + run + tests.** `dotnet build` → succeeds. Run the app, open Settings, switch through **all six themes** — confirm no crash and no `ResourceReferenceKeyNotFoundException` (watch the debug output / crash dialog). `dotnet test` → green.

- [ ] **Step 6: Commit.**

```bash
git add Themes/Dark.xaml Themes/Light.xaml Themes/HighContrast.xaml Themes/Blood.xaml Themes/Greed.xaml Themes/Cyanotic.xaml
git commit -m "Rewrite six theme dictionaries with Studio tokens"
```

---

### Task 4: Shared Studio control styles

**Files:**
- Modify: `Themes/_Shared.xaml`
- Modify: `MainWindow.xaml` (remove the now-duplicated old styles from `Window.Resources` once `_Shared` versions exist — lines `22`–`60` `ToolbarButton`/`Accent`/`Danger`)

**Interfaces:**
- Produces XAML styles (keys): `StudioToolButton` (icon-over-label or icon+label Button), `StudioPrimaryButton` (Save, accent fill), `StudioDangerButton`, `StudioModeTab` (ToggleButton with accent active state + hairline underline), `StudioIconButton` (icon-only, for Search/Settings/chrome), `StudioPill` (zoom/value), `StudioSwatch` (color swatch ToggleButton), `StudioOverlayCard` (Border style for overlays), and restyled implicit `ScrollBar` + `ContextMenu`. All consume `FontUI`/`FontIcon`/`Fs*` + color tokens via `DynamicResource`.

- [ ] **Step 1: Add `StudioToolButton`** (the standard toolbar button — Tabler icon + Geist label, horizontal):

```xml
<Style x:Key="StudioToolButton" TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource BgControl}"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
    <Setter Property="FontFamily" Value="{DynamicResource FontUI}"/>
    <Setter Property="FontSize" Value="{DynamicResource FsButton}"/>
    <Setter Property="Height" Value="34"/>
    <Setter Property="Padding" Value="11,0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="8"
                        Padding="{TemplateBinding Padding}" ToolTip="{TemplateBinding ToolTip}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource BgHover}"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.4"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

Buttons using it supply content as a horizontal `StackPanel` of an icon `TextBlock` (`FontFamily={DynamicResource FontIcon}`, `FontSize=18`) + a label `TextBlock` (`FontFamily={DynamicResource FontUI}`), e.g.:

```xml
<Button Style="{StaticResource StudioToolButton}" Click="Open_Click" ToolTip="{DynamicResource Str_TT_Open}">
  <StackPanel Orientation="Horizontal">
    <TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Open}" Margin="0,0,7,0"/>
    <TextBlock Text="{DynamicResource Str_Lbl_Open}" VerticalAlignment="Center"/>
  </StackPanel>
</Button>
```

- [ ] **Step 2: Add `StudioPrimaryButton`** (Save) — same as `StudioToolButton` but `Background={DynamicResource AccentDim}`, `Foreground={DynamicResource AccentText}`, and a `BorderBrush={DynamicResource AccentBorder}` (BorderThickness 1) on `Bd`. Add `StudioDangerButton` — `Foreground={DynamicResource DangerRed}`.

- [ ] **Step 3: Add `StudioModeTab`** (ToggleButton). Inactive: transparent bg, `TextSecondary`. Hover: `BgHover`. Checked: `AccentDim` bg, `AccentText` fg, `AccentBorder` 1px, and a 2px bottom `Border` (the signature underline) in `Accent`:

```xml
<Style x:Key="StudioModeTab" TargetType="ToggleButton">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
    <Setter Property="FontFamily" Value="{DynamicResource FontUI}"/>
    <Setter Property="FontSize" Value="{DynamicResource FsTab}"/>
    <Setter Property="FontWeight" Value="Medium"/>
    <Setter Property="Height" Value="32"/>
    <Setter Property="Padding" Value="15,0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ToggleButton">
                <Grid>
                    <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="8"
                            BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <Border x:Name="Underline" Height="2" VerticalAlignment="Bottom" CornerRadius="1"
                            Background="Transparent" Margin="6,0"/>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource BgHover}"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentDim}"/>
                        <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource AccentBorder}"/>
                        <Setter TargetName="Underline" Property="Background" Value="{DynamicResource Accent}"/>
                        <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 4: Add `StudioIconButton`** (icon-only, 34×32, transparent→`BgHover`, `FontIcon` 18, tooltip required) and `StudioToolToggle` (a ToggleButton variant of `StudioToolButton` for the active editing tool — checked state = `AccentDim`/`AccentBorder`/`AccentText`, reusing the trigger pattern from Step 3 minus the underline).

- [ ] **Step 5: Add `StudioPill`** (zoom value box / small value chips: `BgControl`, radius 7, `FontUI`, padding `10,4`) and `StudioSwatch` (a 16×16 ToggleButton, radius 6, `BorderBrush=Accent`/thickness 2 when checked).

- [ ] **Step 6: Add `StudioOverlayCard`** (Border style: `Background={DynamicResource BgModal}`, `BorderBrush={DynamicResource BorderDim}`, `BorderThickness=1`, `CornerRadius=12`, drop shadow effect) for the overlay/dialog surfaces.

- [ ] **Step 7: Move the implicit `ScrollBar` and `ContextMenu` styles** (currently `MainWindow.xaml` `146`–`250`) into `_Shared.xaml`, retargeted to tokens (thumb `BgScrollThumb`→`Accent` on hover; menu `BgPanel`/`BorderDim`/`FontUI`). Delete them from `MainWindow.xaml` once moved. Leave the ComboBox/zoom styles (`251`–`411`) in `MainWindow.xaml` for now (restyled in Task 9).

- [ ] **Step 8: Build.** `dotnet build` → succeeds (styles compile even before they're used). `dotnet test` → green.

- [ ] **Step 9: Commit.**

```bash
git add Themes/_Shared.xaml MainWindow.xaml
git commit -m "Add shared Studio control styles (buttons, mode tabs, pills, overlay card)"
```

---

## Phase 1 — Structure

### Task 5: Mode enum + SetMode + tab strip

**Files:**
- Modify: `MainWindow.xaml` (insert a new tab-strip row; renumber/adjust `Grid.RowDefinitions` at `536`–`541`)
- Modify: `MainWindow.xaml.cs`
- Modify: `Strings/*.xaml` (×6) — add `Str_Mode_View/Edit/Pages/Sign`

**Interfaces:**
- Produces: `enum AppMode { View, Edit, Pages, Sign }`; `private AppMode _mode`; `void SetMode(AppMode mode)`; named tabs `ModeViewTab`, `ModeEditTab`, `ModePagesTab`, `ModeSignTab`; named mode panels `ModePanelView/Edit/Pages/Sign` (created in Task 6). Consumed by Task 6/7.

- [ ] **Step 1: Add a tab-strip row.** The window grid (`536`) currently has 4 rows (title/toolbar/content/status). Insert a NEW row between title (0) and toolbar so rows become: 0 title, 1 **tab strip**, 2 toolbar, 3 content, 4 status. Update `Grid.RowDefinitions` to 5 rows and bump every existing `Grid.Row="1"`→`2`, `"2"`→`3`, `"3"`→`4`, and `Grid.RowSpan="4"`→`"5"` on the three overlays (`974`, `1117`, `1214`) and grain (`1304`).

- [ ] **Step 2: Add the tab-strip XAML** at the new Grid.Row 1:

```xml
<Border Grid.Row="1" Background="{DynamicResource BgSidebar}" BorderBrush="{DynamicResource BorderDim}" BorderThickness="0,0,0,1">
    <DockPanel LastChildFill="False" Margin="8,4">
        <ToggleButton x:Name="ModeViewTab"  Style="{StaticResource StudioModeTab}" Content="{DynamicResource Str_Mode_View}"  Checked="ModeTab_Checked" Tag="View"/>
        <ToggleButton x:Name="ModeEditTab"  Style="{StaticResource StudioModeTab}" Content="{DynamicResource Str_Mode_Edit}"  Checked="ModeTab_Checked" Tag="Edit"  Margin="2,0,0,0"/>
        <ToggleButton x:Name="ModePagesTab" Style="{StaticResource StudioModeTab}" Content="{DynamicResource Str_Mode_Pages}" Checked="ModeTab_Checked" Tag="Pages" Margin="2,0,0,0"/>
        <ToggleButton x:Name="ModeSignTab"  Style="{StaticResource StudioModeTab}" Content="{DynamicResource Str_Mode_Sign}"  Checked="ModeTab_Checked" Tag="Sign"  Margin="2,0,0,0"/>
        <Button DockPanel.Dock="Right" Style="{StaticResource StudioIconButton}" Click="SettingsBtn_Click"
                Content="{StaticResource Ico_Settings}" FontFamily="{DynamicResource FontIcon}" ToolTip="{DynamicResource Str_TT_Settings}"/>
        <Button DockPanel.Dock="Right" Style="{StaticResource StudioIconButton}" Click="OpenSearch_Click"
                Content="{StaticResource Ico_Search}" FontFamily="{DynamicResource FontIcon}" ToolTip="{DynamicResource Str_TT_Search}" Margin="0,0,4,0"/>
    </DockPanel>
</Border>
```

(If there is no existing `SettingsBtn_Click`/search-open handler with these exact names, use the actual ones — `SettingsBtn_Click` exists per cb; for search, find the Ctrl+F handler and add a thin `OpenSearch_Click` wrapper that calls it.)

- [ ] **Step 3: Add the enum + fields + SetMode** to `MainWindow.xaml.cs` (near other UI-state fields):

```csharp
private enum AppMode { View, Edit, Pages, Sign }
private AppMode _mode = AppMode.View;
private bool _suppressModeEvents;

private void ModeTab_Checked(object sender, RoutedEventArgs e)
{
    if (_suppressModeEvents) return;
    if (sender is System.Windows.Controls.Primitives.ToggleButton tb && tb.Tag is string s
        && Enum.TryParse<AppMode>(s, out var m))
        SetMode(m);
}

private void SetMode(AppMode mode)
{
    _mode = mode;
    _suppressModeEvents = true;
    ModeViewTab.IsChecked  = mode == AppMode.View;
    ModeEditTab.IsChecked  = mode == AppMode.Edit;
    ModePagesTab.IsChecked = mode == AppMode.Pages;
    ModeSignTab.IsChecked  = mode == AppMode.Sign;
    _suppressModeEvents = false;

    // Panel visibility wired in Task 6 (ModePanelView/Edit/Pages/Sign).
    if (ModePanelView  != null) ModePanelView.Visibility  = mode == AppMode.View  ? Visibility.Visible : Visibility.Collapsed;
    if (ModePanelEdit  != null) ModePanelEdit.Visibility  = mode == AppMode.Edit  ? Visibility.Visible : Visibility.Collapsed;
    if (ModePanelPages != null) ModePanelPages.Visibility = mode == AppMode.Pages ? Visibility.Visible : Visibility.Collapsed;
    if (ModePanelSign  != null) ModePanelSign.Visibility  = mode == AppMode.Sign  ? Visibility.Visible : Visibility.Collapsed;
}
```

- [ ] **Step 4: Initialize the default mode.** In the constructor after `InitializeComponent()` (or in the existing `Loaded`/`ContentRendered` handler), call `SetMode(AppMode.View);`.

- [ ] **Step 5: Add mode strings to all six locale files.** In each `Strings/<locale>.xaml` add (translated appropriately; English shown):

```xml
<sys:String x:Key="Str_Mode_View">View</sys:String>
<sys:String x:Key="Str_Mode_Edit">Edit</sys:String>
<sys:String x:Key="Str_Mode_Pages">Pages</sys:String>
<sys:String x:Key="Str_Mode_Sign">Sign</sys:String>
```

(Check the file's root namespace prefix for `System` — match the existing `sys:`/`s:` alias used in that file.)

- [ ] **Step 6: Build + run.** `dotnet build` → succeeds. Run; confirm the tab strip renders, tabs are mutually exclusive (clicking one unchecks others), and the active tab shows the amber underline. Panels don't exist yet — that's Task 6. `dotnet test` → green.

- [ ] **Step 7: Commit.**

```bash
git add MainWindow.xaml MainWindow.xaml.cs Strings/*.xaml
git commit -m "Add AppMode state, SetMode, and mode tab strip"
```

---

### Task 6: Rebuild the toolbar into persistent groups + mode panels

**Files:**
- Modify: `MainWindow.xaml` (toolbar at new Grid.Row 2 — old `562` region)
- Modify: `Strings/*.xaml` (×6) — add `Str_Lbl_*` labels for buttons that now show text

**Interfaces:**
- Consumes: `SetMode`, `StudioToolButton/PrimaryButton/DangerButton/IconButton/ToolToggle/Pill` from Tasks 4–5.
- Produces: named panels `ModePanelView`, `ModePanelEdit`, `ModePanelPages`, `ModePanelSign`; preserved button names `ToolSelectBtn`…`ToolSignatureBtn`, `ZoomOutBtn`, `ZoomBox`, `ZoomInBtn`, `CloseFileBtn`, `SaveAsBtn`.

- [ ] **Step 1: Replace the toolbar Border content.** The toolbar (now Grid.Row 2) becomes a `DockPanel`: a persistent **File group** (left), persistent **Zoom group** (right), and a center `Grid` hosting the four mode panels (only one visible). Keep every `x:Name` and `Click=` exactly as today.

```xml
<Border Grid.Row="2" Background="{DynamicResource BgPanel}" BorderBrush="{DynamicResource BorderDim}" BorderThickness="0,0,0,1" Padding="8,6">
  <DockPanel LastChildFill="False">

    <!-- Persistent: File group -->
    <StackPanel DockPanel.Dock="Left" Orientation="Horizontal">
      <Button Style="{StaticResource StudioToolButton}" Click="Open_Click" ToolTip="{DynamicResource Str_TT_Open}">
        <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Open}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Open}" VerticalAlignment="Center"/></StackPanel>
      </Button>
      <Button x:Name="SaveAsBtn" Style="{StaticResource StudioPrimaryButton}" Click="Save_Click" ToolTip="{DynamicResource Str_TT_Save}" Margin="6,0,0,0">
        <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Save}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Save}" VerticalAlignment="Center"/></StackPanel>
      </Button>
      <Button Style="{StaticResource StudioToolButton}" Click="Print_Click" ToolTip="{DynamicResource Str_TT_Print}" Margin="6,0,0,0">
        <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Print}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Print}" VerticalAlignment="Center"/></StackPanel>
      </Button>
      <!-- File ▾ overflow: New / Close / Save Flattened -->
      <Button x:Name="FileMenuBtn" Style="{StaticResource StudioIconButton}" Click="FileMenu_Click"
              Content="{StaticResource Ico_Chevron}" FontFamily="{DynamicResource FontIcon}" ToolTip="{DynamicResource Str_TT_FileMenu}" Margin="6,0,0,0">
        <Button.ContextMenu>
          <ContextMenu>
            <MenuItem Header="{DynamicResource Str_Lbl_New}" Click="New_Click"/>
            <MenuItem x:Name="CloseFileMenuItem" Header="{DynamicResource Str_Lbl_CloseFile}" Click="CloseFile_Click" IsEnabled="False"/>
            <MenuItem Header="{DynamicResource Str_Lbl_SaveFlattened}" Click="SaveFlattened_Click"/>
          </ContextMenu>
        </Button.ContextMenu>
      </Button>
      <Border Width="1" Background="{DynamicResource BorderDim}" Margin="10,3"/>
    </StackPanel>

    <!-- Persistent: Zoom group (right) -->
    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
      <Button x:Name="ZoomOutBtn" Style="{StaticResource StudioIconButton}" Click="ZoomOut_Click" Content="{StaticResource Ico_ZoomOut}" FontFamily="{DynamicResource FontIcon}" ToolTip="{DynamicResource Str_TT_ZoomOut}"/>
      <!-- ZoomBox ComboBox moved here verbatim from old line 598 (keep x:Name, width, handlers) -->
      <Button x:Name="ZoomInBtn" Style="{StaticResource StudioIconButton}" Click="ZoomIn_Click" Content="{StaticResource Ico_ZoomIn}" FontFamily="{DynamicResource FontIcon}" ToolTip="{DynamicResource Str_TT_ZoomIn}"/>
    </StackPanel>

    <!-- Mode panels (only one visible at a time) -->
    <Grid Margin="10,0,0,0">
      <StackPanel x:Name="ModePanelView" Orientation="Horizontal" Visibility="Collapsed"/>   <!-- filled in Task 7 -->
      <StackPanel x:Name="ModePanelEdit" Orientation="Horizontal"/>                            <!-- filled below -->
      <StackPanel x:Name="ModePanelPages" Orientation="Horizontal" Visibility="Collapsed"/>
      <StackPanel x:Name="ModePanelSign" Orientation="Horizontal" Visibility="Collapsed"/>
    </Grid>

  </DockPanel>
</Border>
```

- [ ] **Step 2: Fill `ModePanelEdit`** by MOVING the existing tool buttons (`ToolSelectBtn`…`ToolImageBtn`, `ToolCropBtn`), `Undo`, and `ClearAnnotations` from the old toolbar into it. Convert each to `StudioToolToggle` (tools) / `StudioToolButton` (Undo) / `StudioDangerButton` (Clear), keeping `x:Name` and `Click`. Example for Select + Text (repeat the pattern for Highlight/Draw/Image/Crop with their `Ico_*` + `Str_Lbl_*`):

```xml
<ToggleButton x:Name="ToolSelectBtn" Style="{StaticResource StudioToolToggle}" Click="ToolSelect_Click" ToolTip="{DynamicResource Str_TT_SelectTool}">
  <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Select}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Select}" VerticalAlignment="Center"/></StackPanel>
</ToggleButton>
<ToggleButton x:Name="ToolTextBtn" Style="{StaticResource StudioToolToggle}" Click="ToolText_Click" ToolTip="{DynamicResource Str_TT_TextTool}" Margin="4,0,0,0">
  <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Text}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Text}" VerticalAlignment="Center"/></StackPanel>
</ToggleButton>
```

NOTE: the tool buttons are currently `Button` (cb sets active state by other means). If converting `Button`→`ToggleButton` affects `ToolSelect_Click` logic, instead keep them as `Button` with `StudioToolButton` and let the existing active-tool styling code run — do whichever keeps the existing handler working. Verify by exercising each tool.

- [ ] **Step 3: Fill `ModePanelPages`** with `StudioToolButton`s wired to existing handlers: Merge (`Merge_Click`, `Ico_Merge`), Extract (`Split_Click`, `Ico_Extract`), Insert blank (use the existing insert-blank handler — find it; if only in the thumbnail context menu, call the same method), Delete (`Delete_Click`, `Ico_Delete`, `StudioDangerButton`), Move up (`MoveUp_Click`), Move down (`MoveDown_Click`), Rotate (existing rotate handler).

- [ ] **Step 4: Fill `ModePanelSign`** with Signature (`ToolSignature_Click`, `Ico_Signature` — keeps the existing signature popup) and Fill form (existing forms handler if present; otherwise omit and note it).

- [ ] **Step 5: Move `ZoomBox`** (the ComboBox at old `598`–`611`) verbatim into the Zoom group placeholder, preserving `x:Name="ZoomBox"`, width, and all handlers.

- [ ] **Step 6: Add the new label strings** to all six locale files: `Str_Lbl_Open/Save/Print/New/CloseFile/SaveFlattened/Select/Text/Highlight/Draw/Image/Crop/Merge/Extract/InsertPage/Delete/MoveUp/MoveDown/Rotate/Signature/FillForm/Fit/Single/Continuous/TwoPage/Grid` and tooltips `Str_TT_Save/Settings/Search/FileMenu` if missing (reuse existing `Str_TT_*` where they already exist — many do). English values are the words themselves; translate per locale.

- [ ] **Step 7: Build + run + verify each mode.** `dotnet build` → succeeds. Run; switch View/Edit/Pages/Sign and confirm each panel shows its buttons; exercise one action per mode (e.g. Edit→Text places text; Pages→Move Up reorders; Save works). `dotnet test` → green.

- [ ] **Step 8: Commit.**

```bash
git add MainWindow.xaml Strings/*.xaml
git commit -m "Rebuild toolbar into persistent File/Zoom groups + Edit/Pages/Sign mode panels"
```

---

### Task 7: View tab — move view-mode selection out of Settings

**Files:**
- Modify: `MainWindow.xaml` (`ModePanelView`; remove VIEW MODE group from Settings overlay `1090`–`1112`)
- Modify: `MainWindow.xaml.cs` (reuse `SetViewMode`; keep or thin-wrap the four `View*Radio_Checked` handlers)
- Modify: `Strings/*.xaml` (×6) — remove/repurpose the Settings "View Mode" group strings

**Interfaces:**
- Consumes: `SetViewMode` (cb `8758`), existing view handlers.
- Produces: `ModePanelView` populated; `ViewMode` reflected by toggled buttons.

- [ ] **Step 1: Populate `ModePanelView`** with `StudioToolToggle` buttons (mutually exclusive via code, like the tools): Single, Continuous, Two-page, Grid, Fit, plus a `StudioToolButton` Rotate. Wire each to a handler that calls `SetViewMode(...)` with the right mode — reuse the existing `ViewSingleRadio_Checked`/etc. bodies by extracting their core into the click handlers, or have the new buttons call small wrappers that invoke `SetViewMode`.

```xml
<ToggleButton x:Name="ViewSingleBtn" Style="{StaticResource StudioToolToggle}" Click="ViewSingle_Click" ToolTip="{DynamicResource Str_TT_ViewSingle}">
  <StackPanel Orientation="Horizontal"><TextBlock FontFamily="{DynamicResource FontIcon}" FontSize="18" Text="{StaticResource Ico_Single}" Margin="0,0,7,0"/><TextBlock Text="{DynamicResource Str_Lbl_Single}" VerticalAlignment="Center"/></StackPanel>
</ToggleButton>
<!-- Continuous (Ico_Continuous), TwoPage (Ico_TwoPage), Grid (Ico_Grid), Fit (Ico_Fit), Rotate (Ico_Rotate) similarly -->
```

- [ ] **Step 2: Add the click handlers** in code-behind (calling the existing `SetViewMode`; check its signature/enum and pass the matching value):

```csharp
private void ViewSingle_Click(object sender, RoutedEventArgs e)     => ApplyViewModeFromTab(/* Single */);
private void ViewContinuous_Click(object sender, RoutedEventArgs e) => ApplyViewModeFromTab(/* Continuous */);
private void ViewTwoPage_Click(object sender, RoutedEventArgs e)    => ApplyViewModeFromTab(/* TwoPage */);
private void ViewGrid_Click(object sender, RoutedEventArgs e)       => ApplyViewModeFromTab(/* Grid */);
```

where `ApplyViewModeFromTab` calls `SetViewMode(<enum>)` and updates the toggled state of the five view buttons (uncheck siblings). Match `SetViewMode`'s actual parameter type (read cb `8758`).

- [ ] **Step 3: Remove the VIEW MODE group** from the Settings overlay (`MainWindow.xaml` ~`1090`–`1112`, the four `View*Radio` radios and their group header). Delete the now-unused `View*Radio_Checked` handlers ONLY if nothing else references them; otherwise leave them and have the new click handlers reuse their logic. Settings overlay now contains THEME + LANGUAGE only.

- [ ] **Step 4: Remove/repurpose Settings "View Mode" strings** in all six locale files (the group header + radio labels). If the labels are reused for the View tab (`Str_Lbl_Single` etc. from Task 6), keep those and only remove the now-orphaned group-header key. Ensure no `DynamicResource` in XAML references a removed key.

- [ ] **Step 5: Build + run + verify.** `dotnet build` → succeeds. Run; in View tab switch Single/Continuous/Two/Grid and confirm the page layout changes exactly as the old Settings radios did; confirm Settings overlay no longer shows View Mode. Confirm the Edit-tool-in-Continuous guidance message still appears. `dotnet test` → green.

- [ ] **Step 6: Commit.**

```bash
git add MainWindow.xaml MainWindow.xaml.cs Strings/*.xaml
git commit -m "Move view-mode selection from Settings to the View tab"
```

---

### Task 8: Restyle the code-behind contextual bars

**Files:**
- Modify: `MainWindow.xaml.cs` (`SettingsBar` ~`3883`–`4173`, `CropConfirmBar` ~`2980`–`5359`, `SearchBar` ~`5742`–`5852`)

**Interfaces:**
- Consumes: token brushes via `(Brush)FindResource("...")` / `DynamicResource`, `FontUI`/`FontIcon`, `Ico_*`.
- Produces: restyled bars; show/hide behavior unchanged.

- [ ] **Step 1: Read the three bar builders** to see how brushes/fonts are currently set (hardcoded `Color`/`Brush`? `FindResource`?). Identify each place a color, font, corner radius, or Segoe MDL2 glyph is assigned.

- [ ] **Step 2: Replace hardcoded colors with token lookups.** Where a bar sets `Background`/`Foreground`/`BorderBrush`, use `(Brush)FindResource("BgPanel")`, `"BgControl"`, `"TextPrimary"`, `"Accent"`, `"BorderDim"`, `"DangerRed"` etc. so the bars follow the theme. (If they already use `FindResource`, just confirm the keys still exist — they do.)

- [ ] **Step 3: Apply Geist + Tabler.** Set `FontFamily = (FontFamily)FindResource("FontUI")` on text controls; for any glyph buttons set `FontFamily = (FontFamily)FindResource("FontIcon")` and `Content = (string)FindResource("Ico_<Name>")`. Apply `CornerRadius = new CornerRadius(8)` to bar borders/buttons to match Studio geometry.

- [ ] **Step 4: Reposition under the new toolbar.** The contextual bar should sit directly under the toolbar (new Grid.Row 2). If the bar is absolutely positioned (Canvas/Popup with a computed Y), update the offset to account for the inserted tab-strip row (title 32 + tab 40 + toolbar 48 ≈ top of contextual area). Verify visually.

- [ ] **Step 5: Build + run + verify.** `dotnet build` → succeeds. Run; activate Text/Highlight/Draw/Crop and confirm each contextual bar appears, is styled (Geist text, amber active swatch, Tabler glyphs), and functions (change size/color/opacity, apply/cancel crop). Open Search (Ctrl+F) and confirm it's styled and finds/navigates matches. Switch a theme with a bar open and confirm colors follow. `dotnet test` → green.

- [ ] **Step 6: Commit.**

```bash
git add MainWindow.xaml.cs
git commit -m "Restyle contextual tool/crop/search bars to Studio language"
```

---

## Phase 2 — Polish

### Task 9: Restyle title bar, sidebar, status bar, and the zoom ComboBox

**Files:**
- Modify: `MainWindow.xaml` (title `544`, sidebar `628`–`813`, status `916`, ComboBox/zoom styles `251`–`411`, page-list/sidebar-tab/treeview/text-button styles `413`–`535`)

**Interfaces:**
- Consumes: tokens, `FontUI`/`FontIcon`, `Ico_*`, `Fs*`.

- [ ] **Step 1: Title bar.** Set the app-name `TextBlock` to `FontFamily="{DynamicResource FontUI}"`; replace the three chrome glyph `Content` values (`&#xE921;`/`&#xE922;`/`&#xE8BB;`) with `{StaticResource Ico_Min}`/`Ico_Max`/`Ico_WinClose` and `FontFamily="{DynamicResource FontIcon}"`. Show the filename next to the app name (bind/update from the existing title-update code). Apply the amber app-dot if desired.

- [ ] **Step 2: Sidebar.** Restyle PAGES/OUTLINES sub-tab buttons (`450`–`476`) and the page-jump box + "of N" with `FontUI`, `FsSidebarLabel` for section labels (uppercase, letter-spacing via `TextBlock`), tokens for colors. Restyle the collapse toggle and the bottom Shortcuts/Settings icon buttons to `StudioIconButton` with `Ico_Shortcuts`/`Ico_Settings`. Retarget the TreeView style (`477`–`492`) to tokens + `FontUI`.

- [ ] **Step 3: Status bar.** `FontFamily="{DynamicResource FontUI}"`, `FontSize="{DynamicResource FsStatus}"`, tabular numerals for the page indicator; tokens for colors. Keep the PORTABLE badge + Install button (gated by existing logic) and the version-label→About click.

- [ ] **Step 4: Zoom ComboBox.** Retarget the ComboBox template (`251`–`411`) brushes to tokens (`BgControl`/`BgHover`/`BorderDim`/`TextPrimary`/`Accent`), `FontUI`, radius 7, and the chevron to `Ico_Chevron`+`FontIcon`. Keep `IsEditable` behavior and all bindings.

- [ ] **Step 5: Build + run + verify.** `dotnet build` → succeeds. Run; confirm title/sidebar/status all render in Geist with Tabler glyphs and correct theme colors; zoom dropdown opens, presets + Fit Width/Fit Page work; sidebar collapse, page jump, thumbnail select/reorder, outline tab all work. Switch all six themes and confirm consistency. `dotnet test` → green.

- [ ] **Step 6: Commit.**

```bash
git add MainWindow.xaml
git commit -m "Restyle title bar, sidebar, status bar, and zoom combo to Studio"
```

---

### Task 10: Restyle overlays, dialogs, Print Preview, and Signature window

**Files:**
- Modify: `MainWindow.xaml` (Settings overlay `973`, Shortcut overlay `1116`, About overlay `1213`; radio style `525`)
- Modify: the Print Preview + Signature draw windows (find their `.xaml`/`.cs` — likely `PrintPreviewWindow.xaml`, `SignatureWindow.xaml` or built in code-behind)
- Modify: `MainWindow.xaml.cs` for any code-built dialogs (confirm/discard/overwrite message dialogs)

**Interfaces:**
- Consumes: `StudioOverlayCard`, `StudioToolButton/PrimaryButton/DangerButton`, tokens, fonts, `Fs*`.

- [ ] **Step 1: Settings/Shortcuts/About overlays.** Wrap each overlay's content panel in `StudioOverlayCard`; titles use `FsDialogTitle`+`FontUI`; close buttons use `StudioIconButton`+`Ico_WinClose`. Restyle the settings radio style (`525`–`534`) to Studio (accent dot when checked, `FontUI`). Restyle the Shortcuts table (`FontUI`, token colors) and About (Geist, tabular numerals for the hashes/version).

- [ ] **Step 2: Print Preview.** Locate the print preview UI (`grep -rln "PrintPreview\|Print Preview" *.xaml *.cs`). Retarget its controls to tokens, `FontUI`, `StudioToolButton`/`StudioPrimaryButton`, radius 8. Keep printer/orientation/copies/range/live-preview logic intact.

- [ ] **Step 3: Signature draw window.** Locate it (`grep -rln "Signature" *.xaml`). Restyle Clear / Save Signature / close to Studio buttons + fonts + tokens.

- [ ] **Step 4: Confirmation dialogs.** For code-built confirm dialogs (discard unsaved, delete pages, overwrite-on-save) ensure destructive buttons use `DangerRed`; if they use the OS `MessageBox`, leave them (out of scope) OR note them. Restyle any custom in-app dialogs to match.

- [ ] **Step 5: Build + run + verify.** `dotnet build` → succeeds. Run; open Settings, Shortcuts (Ctrl+?), About (version label), Print Preview (Ctrl+P), and the Signature window (Sign → Create) — confirm all styled consistently and functional. `dotnet test` → green.

- [ ] **Step 6: Commit.**

```bash
git add -A
git commit -m "Restyle overlays, dialogs, Print Preview, and Signature window to Studio"
```

---

### Task 11: Sweep remaining Segoe MDL2 glyphs + apply FontUI globally

**Files:**
- Modify: `MainWindow.xaml`, `MainWindow.xaml.cs`, any other `.xaml`

**Interfaces:** none new — cleanup task.

- [ ] **Step 1: Find leftover Segoe references.** Run:

```bash
cd "C:/Code/Personal/KillerPDF" && grep -rn "Segoe MDL2 Assets\|Segoe UI" --include=*.xaml --include=*.cs . | grep -v "_Shared.xaml"
```

- [ ] **Step 2: Replace each** remaining `FontFamily="Segoe MDL2 Assets"` with `{DynamicResource FontIcon}` (and convert its `&#x....;` content to the matching `Ico_*` resource), and each hardcoded `FontFamily="Segoe UI"` with `{DynamicResource FontUI}`. Set the `Window`-level default `FontFamily="{DynamicResource FontUI}"` on `MainWindow` (`1`–`15`) so all text inherits Geist.

- [ ] **Step 3: Grep for stray hardcoded hex colors** in XAML that should be tokens:

```bash
grep -rn 'Color="#\|Background="#\|Foreground="#' --include=*.xaml . | grep -v "Themes/"
```

Convert any chrome colors to `DynamicResource` tokens (leave intentional fixed colors like the 10 annotation swatches).

- [ ] **Step 4: Build + run + verify.** `dotnet build` → succeeds. Run; scan every screen for boxed/missing glyphs or non-Geist text. Switch themes; confirm nothing is stuck on a hardcoded color. `dotnet test` → green.

- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "Sweep remaining Segoe glyphs/fonts and hardcoded colors to Studio tokens"
```

---

## Phase 3 — Verify

### Task 12: Full verification matrix + docs update

**Files:**
- Modify: `docs/UI-REFERENCE.md` (reflect mode tabs + Studio), `CLAUDE.md` (note the new layout/fonts/icons if architecture-relevant)

- [ ] **Step 1: Build + test green.** `dotnet build` → succeeds. `dotnet test` → all pass.

- [ ] **Step 2: Manual matrix** (use the `run`/`verify` skill). Confirm:
  - Open a PDF; default mode is View.
  - All four tabs switch the toolbar; persistent File/Zoom/Search/Settings always present.
  - One tool per mode works: View (each layout), Edit (Text/Highlight/Draw/Image/Crop + Undo/Clear + contextual bars), Pages (Merge/Extract/Insert/Delete/Move/Rotate), Sign (place signature, fill form).
  - All six themes switch live with NO `ResourceReferenceKeyNotFoundException` and no stuck colors.
  - At least three languages switch with NO blank labels (verify mode tabs + new labels render in each).
  - Geist text + Tabler glyphs render everywhere; tabular numerals align in zoom/page/coords.
  - Overlays (Settings/Shortcuts/About), Search, Print Preview, Signature window all styled + functional.
  - Keyboard focus visible; Light/HighContrast contrast acceptable.

- [ ] **Step 3: Release-publish font check.** Run `dotnet publish -c Release`; launch the published single-file EXE from `bin/Release/net48/publish/` and confirm Geist + Tabler render (Costura-embedded fonts load). If not, document the fallback behavior.

- [ ] **Step 4: Update docs.** Revise `docs/UI-REFERENCE.md` to describe the mode-tab layout, persistent groups, and Studio styling. Add a short note to `CLAUDE.md` under Architecture about `_Shared.xaml`, bundled Geist/Tabler, the token key set, and `AppMode`/`SetMode`.

- [ ] **Step 5: Commit.**

```bash
git add docs/UI-REFERENCE.md CLAUDE.md
git commit -m "Update UI reference and CLAUDE.md for Studio redesign; verify build/publish"
```

---

## Self-Review (completed by plan author)

**Spec coverage:** §2 tokens → Tasks 1–4; §2.2 type → Task 1; §2.3 icons → Task 2; §2.4 geometry → Task 4; §2.5 signature (tab underline + page shadow) → Task 4 (`StudioModeTab`); §3 layout/persistent/mode panels → Tasks 5–7; §3.3 contextual bar → Task 8; §4 overlays/dialogs → Task 10; §5 implementation (fonts/styles/themes/restructure/localization) → Tasks 1–11; §6 testing → Task 12; §7 risks (key/string parity, font embed) → Tasks 3.4, 5.5, 6.6, 12.3. All covered.

**Placeholder scan:** Icon codepoints in Task 2 and the `SetViewMode` enum value in Task 7 are explicitly flagged as "verify against the real source" rather than left vague — these require the engineer to read the actual font CSS / method signature, which is the correct action, not a plan gap. No "TBD/handle edge cases/similar to" placeholders remain.

**Type consistency:** `AppMode` enum, `SetMode`, `ModeTab_Checked`, panel names `ModePanelView/Edit/Pages/Sign`, and preserved control names (`ToolSelectBtn`…, `ZoomBox`, `SaveAsBtn`, `CloseFileBtn`) are used identically across Tasks 5–9. Token key names match the spec §2.1 and Task 3 list throughout.

**Adaptation note:** This is a UI redesign in a WPF monolith; classic TDD (failing unit test first) does not apply to XAML/visual changes. Each task's "test cycle" is `dotnet build` success + `dotnet test` staying green + a specific manual verification via the `run`/`verify` skill, with frequent commits — the appropriate discipline for this codebase.
