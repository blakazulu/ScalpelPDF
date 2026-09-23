# Releasing Scalpel — the full runbook (portable · installer · Store)

The single source of truth for shipping a new version. Covers all three distribution
channels, how versioning flows, the exact build commands, the publish order, and every
gotcha we've hit. Keep this current.

> Deep-dive for the Store specifically (manifest, GPLv3 handling, sideload testing):
> [`STORE-PUBLISHING.md`](STORE-PUBLISHING.md). This runbook is the umbrella over all channels.

---

## 0. The three channels at a glance

| Channel | Artifact | Built by | Hosted on | Signed? |
|---|---|---|---|---|
| **Portable** | `Scalpel.exe` (single-file, ~8 MB) | `dotnet publish -c Release` | GitHub Release asset | **No** (see §6) |
| **Installer** | `Scalpel-Setup.exe` (~8.5 MB) | `installer\build-installer.ps1` (Inno Setup) | GitHub Release asset | **No** (wraps the same EXE) |
| **Store** | `Scalpel_<ver>.0_x64.msix` | `packaging\build-msix.ps1 -Store` | Partner Center | **Store signs it** (upload unsigned) |

**How users actually download (don't skip this — it explains the publish order):**

- The **website does not host binaries.** `website/netlify.toml` redirects:
  - `https://scalpel-pdf.netlify.app/download`  → `github.com/blakazulu/ScalpelPDF/releases/latest/download/Scalpel.exe`
  - `https://scalpel-pdf.netlify.app/installer` → `.../releases/latest/download/Scalpel-Setup.exe`
  - So the portable + installer downloads come from the **latest GitHub Release**. Publishing = creating/attaching assets to a GitHub Release.
- The **in-app update check** reads `https://scalpel-pdf.netlify.app/version.json` (served by Netlify from `website/public/version.json`). It compares the `version` field to the running app and shows the "update available" banner. Store builds are pointed at `storeUrl`, portable + installer at `siteUrl` (see `Services/UpdateService.cs` → `ResolveUrl`).

---

## 1. Versioning — two sources of truth, both must read the same `X.Y.Z`

There are **not** three version numbers to maintain. Everything derives from two places:

| File | Field | Drives |
|---|---|---|
| `Scalpel.csproj` | `<Version>` / `<AssemblyVersion>` / `<FileVersion>` | the portable EXE, the installer EXE (wraps it), and the Store MSIX version |
| `website/public/version.json` | `"version"` | the in-app update-check signal for **all** channels |

Belt-and-suspenders defaults that should match (but are normally overridden at build from the EXE/csproj):
`installer/Scalpel.iss` (`#define AppVersion`) and `installer/build-installer.ps1` (`$Version` fallback).

### The pre-push auto-bump hook

`build/hooks/pre-push` runs `build/hooks/bump-version.ps1` on every `git push`, which:

1. Increments the **4th component (revision)** of all three `<…Version>` tags in `Scalpel.csproj`
   (e.g. `1.9.0.1` → `1.9.0.2`), commits it as `Bump version to X.Y.Z.N [skip-bump]`, and
   **aborts the first push** asking you to re-run.
2. So a normal push is: `git push` → "bumped, re-run" → `git push` again. The second push includes the bump commit.

**Implication:** the *revision* (`.N`) advances automatically on every push. To move **Major.Minor.Build**
(e.g. `1.8.x` → `1.9.0`), edit `Scalpel.csproj` `<Version>` by hand to `1.9.0.0` and bump the
three other places below. The Store always uses `Major.Minor.Build.0` (revision forced to `.0` by the MSIX script).

### What to edit when starting a new Major.Minor.Build (e.g. → 1.9.0)

1. `Scalpel.csproj` — `<Version>`/`<AssemblyVersion>`/`<FileVersion>` → `1.9.0.0`
2. `website/public/version.json` — `"version": "1.9.0"` + refresh the `notes[]` (user-facing one-liners)
3. `installer/Scalpel.iss` — `#define AppVersion "1.9.0"`
4. `installer/build-installer.ps1` — `$Version = "1.9.0"` fallback
5. `Services/Changelog.cs` — prepend a new `Release("1.9.0", …)` (the in-app "What's New" popup; **required** for any user-facing change)

> ⚠️ **Never ship without updating `version.json`** — if it lags, the in-app update check silently
> never fires (or points users at a version that isn't out). This is the #1 release footgun.

---

## 2. Prerequisites / toolchain

| Need | For | Where it is on this machine |
|---|---|---|
| **.NET 8 SDK** | all builds (targets net48, builds with .NET 8) | `dotnet` may not be on PATH → use `~/.dotnet/dotnet.exe` |
| **Inno Setup 6** | the installer | **per-user**: `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` (NOT Program Files) — installed via `winget install JRSoftware.InnoSetup` |
| **Windows 10/11 SDK** | the MSIX (`makeappx`, `makepri`, `signtool`) | `C:\Program Files (x86)\Windows Kits\10\bin\<ver>\x64\` (look under the **versioned** subdir; the bare `x86` bin lacks them) |
| **Partner Center** account | Store submission | <https://partner.microsoft.com> → Apps and games → Scalpel PDF |
| **`gh` CLI** (or GitHub web) | creating the GitHub Release + uploading assets | `gh release create …` |

---

## 3. The release runbook (in order)

> **Order matters.** Build everything first, publish the GitHub Release + Store, and deploy
> the website (`version.json`) **LAST** — deploying it flips the in-app "update available" banner,
> so existing users must not be told to update before the downloads actually exist.

### Step A — Pre-flight

```powershell
# Clean tree, all tests green, on main
git status --short
~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj      # expect "Passed!"
```

Make sure **`Scalpel.exe` is not running** (it locks `pdfium.dll` and breaks the build copy — see §6).

### Step B — Bump the version

Edit the 5 places in §1 for the new `Major.Minor.Build`. Commit. (The push hook will tack on the revision.)

### Step C — Build the portable EXE

```powershell
~/.dotnet/dotnet.exe publish -c Release
# → bin\Release\net48\publish\Scalpel.exe   (single-file Costura bundle, pdfium embedded)
# → also produces Scalpel-<ver>-src.zip (GPLv3 source bundle) in the same folder
```

Verify: the EXE is ~8 MB, there's **no loose `pdfium.dll`** next to it, and
`(Get-Item …\Scalpel.exe).VersionInfo.ProductVersion` shows the new version.

> **Official alternative:** `release.ps1` does publish **+ sign + write the real pdfium SHA256 into
> `BuildInfo.cs` + SHA256SUMS.txt**. See §6 for whether to sign. Run `release.ps1 -SkipSign` to get
> the full pipeline output without signing (leaves the integrity check disabled, which is the current
> shipping posture).

### Step D — Build the installer

```powershell
pwsh -File installer\build-installer.ps1
# → installer\out\Scalpel-Setup.exe   (Inno wraps the published EXE from Step C)
```

It auto-locates ISCC (per-user path), derives the version from `Scalpel.csproj`, and wraps
`bin\Release\net48\publish\Scalpel.exe`. **Rebuild this after Step C** so it wraps the new EXE.

### Step E — Build the Store MSIX

```powershell
pwsh -File packaging\build-msix.ps1 -Store
# → packaging\out\Scalpel_<Major.Minor.Build>.0_x64.msix   (UNSIGNED — the Store signs it)
```

`-Store` bakes in this app's real Partner Center identity (see §5) and forces the Store-legal
`…​.0` version. **Verify the identity values still match Partner Center before every submission (§5).**

> Known blocker: this re-publishes via a publish profile whose WPF temp-project compile globs the
> whole repo, so a stray copy of old sources under `docs/github-origin/` breaks it — see §6.

### Step F — Publish (in this order)

1. **GitHub Release** (serves portable + installer):
   ```powershell
   gh release create v1.9.0 `
       "bin\Release\net48\publish\Scalpel.exe" `
       "installer\out\Scalpel-Setup.exe" `
       "bin\Release\net48\publish\Scalpel-1.9.0.2-src.zip" `
       --title "Scalpel 1.9.0" --notes "…release notes…"
   ```
   The asset names **must** stay `Scalpel.exe` and `Scalpel-Setup.exe` — the Netlify
   `/download` and `/installer` redirects point at `releases/latest/download/<those exact names>`.

2. **Microsoft Store**: upload `packaging\out\Scalpel_*.msix` in Partner Center → Scalpel PDF →
   new submission → Packages. The Store signs it. (Full submission steps incl. GPLv3 notes: `STORE-PUBLISHING.md`.)

3. **Website / `version.json`** — **LAST.** Deploy the Netlify site so the new `version.json` goes
   live (Netlify auto-deploys on push to the repo's website build, or trigger a deploy). This is the
   moment existing users get the "update available" banner — so only do it once the GitHub Release
   (and ideally the Store) are live.

---

## 4. Quick cheat-sheet

```powershell
# 0. don't have Scalpel.exe open
~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj
# 1. bump §1 files, commit
# 2. build all three
~/.dotnet/dotnet.exe publish -c Release
pwsh -File installer\build-installer.ps1
pwsh -File packaging\build-msix.ps1 -Store
# 3. publish: gh release (Scalpel.exe + Scalpel-Setup.exe + src.zip) → Partner Center → website LAST
git push   # twice (pre-push hook bumps revision on the first)
```

Artifacts land at:
- `bin\Release\net48\publish\Scalpel.exe` + `Scalpel-<ver>-src.zip`
- `installer\out\Scalpel-Setup.exe`
- `packaging\out\Scalpel_<ver>.0_x64.msix`

---

## 5. Store identity — verify EVERY submission

The MSIX manifest must declare the app's identity **exactly** as Partner Center expects, or the
upload is rejected. `packaging\build-msix.ps1 -Store` hard-codes these:

| Manifest field | Value | Stability |
|---|---|---|
| `Package/Identity/Name` | `LirazShakaAmir.ScalpelPDF` | permanent (reserved app name) |
| `Package/Identity/Publisher` | `CN=8B3919EF-5B9D-4935-A322-FC9435A969F6` | permanent (your Publisher ID GUID) |
| `Package/Properties/PublisherDisplayName` | `Liraz Shaka Amir` | **changes if you rename your publisher display name** |
| `DisplayName` | `Scalpel PDF` | the app's Store name |

**Microsoft Entra tenant ID (for the `msstore` CLI):** `b4ae6828-e087-4c5b-9c32-84984c567fae`

The Partner Center account had no auto-discoverable Entra tenant association, so `msstore reconfigure`
hangs on "Retrieving Organization Id..." unless the tenant is passed explicitly:

```powershell
msstore reconfigure --tenantId b4ae6828-e087-4c5b-9c32-84984c567fae
```

**Where to verify (do this if anything about your account/publisher changed):**
Partner Center → Apps and games → **Scalpel PDF** → Product management → **Product identity**
(`https://partner.microsoft.com/dashboard/products/9N9HN8XW4LF3/identity`). It lists all three
package values verbatim. Copy them into the `-Store` block of `build-msix.ps1` if they differ.

> **Lesson learned (1.9.0):** renaming the publisher display name from `LirazShakaAmir` to
> `Liraz Shaka Amir` (with spaces) changed **only** `PublisherDisplayName`. The Identity Name and the
> `CN=…` Publisher GUID did **not** change (they never do on a rename). A single-character mismatch
> rejects the upload — always confirm against the Product Identity page.

You can also read what the **last accepted** package declared by extracting its manifest:
```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[IO.Compression.ZipFile]::OpenRead("packaging\out\Scalpel_<ver>_x64.msix")
($z.Entries|?{$_.FullName -eq 'AppxManifest.xml'}) | %{ [IO.Compression.ZipFileExtensions]::ExtractToFile($_, "$env:TEMP\m.xml", $true) }; $z.Dispose()
[xml](gc "$env:TEMP\m.xml") | %{ $_.Package.Identity; $_.Package.Properties.PublisherDisplayName }
```

---

## 6. Signing — current posture & the integrity check

- **Portable + installer ship UNSIGNED.** This is deliberate (commit: *"drop inaccurate 'signed'
  claim — web builds are unsigned"*). A plain `dotnet publish` EXE is exactly what goes in the
  GitHub Release and inside the installer. Users may see a SmartScreen prompt; that's expected for
  the free/portable channel.
- **Store** packages are **signed by the Store** at ingestion — you upload them unsigned (`-Store`
  uses `-NoSign`). Never self-sign a Store submission.
- **`release.ps1`** *can* sign the EXE (its docs assume SimplySign Desktop + a CA cert) and, when it
  does a real signed run, it **writes the real `pdfium.dll` SHA256 into `BuildInfo.cs`** so the
  startup integrity check (`CheckPdfiumIntegrity`) is active. With `-SkipSign` (or a plain
  `dotnet publish`), `BuildInfo.cs` stays **all-zeros → the integrity check is disabled**. Since the
  shipping builds are unsigned, the integrity check is currently off for them — acceptable, but know
  that's the trade-off. `release.ps1` is also **interactive** (prompts for SimplySign) and won't run
  headless.
- The only code-signing cert in the dev store is a self-signed `CN=KillerPDF Dev` — **not** a
  distribution cert; don't use it to "sign" releases.

---

## 7. Gotchas / troubleshooting

| Symptom | Cause & fix |
|---|---|
| **MSIX publish fails with `CS0579 Duplicate 'ThemeInfo'` / `CS0246 Xunit/PdfSharp/Tesseract not found`** | A stray **untracked** `docs/github-origin/` holds a full copy of upstream KillerPDF (v1.8.1 as of 2026-08-30: ~640 files incl. `engine/`, `KillerPDF.Tests/`, `Packaging/KillerLauncher/` and several `.csproj`s). The `-Store` build's publish-profile WPF markup pass regenerates a `*_wpftmp.csproj` whose default compile glob sweeps the whole repo and pulls those files in. (A plain `dotnet publish -c Release` did **not** trip on it this session — the publish-profile markup pass globs differently — but treat it as fragile either way.) **Fix:** temporarily move it aside during the MSIX build (e.g. rename `docs/github-origin` → `docs/_github-origin.bak`), build, then restore. It's untracked, so git won't notice. Don't delete it without checking what it is. `Scalpel.csproj` now carries `<Compile/Page/Resource/EmbeddedResource/None Remove="docs\**" />`, which keeps a plain `dotnet build` clean; if the `-Store` markup pass still trips, move the folder aside as above. |
| **Installer wraps a different `Scalpel.exe` than the one in the release** | `build-msix.ps1` re-publishes into the same `bin\Release
et48\publish\` folder, overwriting the EXE from Step C (same source and version, but not byte-identical). Build the installer **after** the MSIX (or re-run `installeruild-installer.ps1` after it), so the installer, the release asset and the MSIX all carry the same EXE. Found in 2.2.1. |
| **Build copy fails `MSB3027`/`MSB3021` on `pdfium.dll`** | A running `Scalpel.exe` is locking it. Close the app and rebuild — **not** a code error. |
| **`NETSDK1047` "no target for net48/win7-x64" on `dotnet build`** | A prior `dotnet publish` pinned the `win-x64` RID. Re-run the build **with** restore (drop `--no-restore`). |
| **In-app "update available" never appears after release** | `website/public/version.json` `version` wasn't bumped/deployed, or the website wasn't redeployed. It's the update signal — deploy it (last). |
| **Store rejects the upload (identity mismatch)** | `PublisherDisplayName`/Identity/Publisher in the MSIX don't match Partner Center. Re-verify on the Product Identity page (§5). |
| **`/download` or `/installer` link 404s** | The GitHub Release asset isn't named exactly `Scalpel.exe` / `Scalpel-Setup.exe`, or there's no published (non-draft) "latest" release. |
| **`dotnet` not found** | Use `~/.dotnet/dotnet.exe`. |
| **`pwsh` flaky / `EPERM uv_spawn`** | Stray background `pwsh` processes; `taskkill /F /IM pwsh.exe` and retry, or use Windows `powershell.exe`. |

---

## 8. Related docs

- [`STORE-PUBLISHING.md`](STORE-PUBLISHING.md) — MSIX/Store deep-dive: packaged-mode behavior, manifest, GPLv3-on-Store licensing, sideload testing.
- [`MS-STORE-REQUIREMENTS.md`](MS-STORE-REQUIREMENTS.md), [`STORE-LISTING-COPY.md`](STORE-LISTING-COPY.md) — Store listing content.
- `release.ps1` — the publish+sign+hash pipeline (read its header comment for parameters).
- `build/hooks/bump-version.ps1`, `build/hooks/pre-push` — the auto-bump mechanism.
- `Services/UpdateService.cs` — how the in-app update check parses `version.json` and resolves the per-channel URL.
