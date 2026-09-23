---
name: release
description: Use when the user wants to publish a new Scalpel version, release, ship, "build the installer", build the MSIX/Store package, "update the store", cut a GitHub Release, or deploy the website/version.json update signal. Covers all three channels (portable EXE, Inno installer, Microsoft Store).
---

# Release Scalpel

Wraps the canonical runbook **`docs/RELEASING.md`** — read it if anything here is unclear or seems stale; it is the source of truth (Store deep-dive: `docs/STORE-PUBLISHING.md`). Run the steps below **in order**. Order matters: the website deploy is the "update available" signal for existing users, so it goes live **LAST**, after the downloads exist.

## Hard rules (non-negotiable)

- **NEVER use the `msstore` CLI.** It crashes/hangs on this machine (proven 2026-07-01). The Store upload is **manual, in the browser**, at Partner Center. (`docs/RELEASING.md` §5 still mentions an `msstore reconfigure --tenantId …` workaround — treat that as superseded; do not run it.)
- **Never deploy the website / `version.json` before the GitHub Release assets exist** — it flips the in-app update banner for all users.
- **`Scalpel.exe` must not be running** during any build (it locks `pdfium.dll` → `MSB3027`/`MSB3021`).
- `dotnet` may not be on PATH — use `~/.dotnet/dotnet.exe`.

## Step 1 — Pre-flight

```powershell
git status --short          # clean tree, on main
~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj   # expect "Passed!"
```

## Step 2 — Set the release version (5 places)

The pre-push hook auto-bumps only the **4th (revision)** component on every push to main. A real release moves **Major.Minor.Build by hand**:

1. `Scalpel.csproj` — **all three tags**: `<Version>`, `<AssemblyVersion>`, `<FileVersion>` → `X.Y.Z.0` (verify all three read the same value).
2. `website/public/version.json` — `"version": "X.Y.Z"` + refresh `notes[]`.
3. `installer/Scalpel.iss` — `#define AppVersion "X.Y.Z"`.
4. `installer/build-installer.ps1` — the `$Version = "…"` fallback.
5. `Services/Changelog.cs` — prepend a `Release("X.Y.Z", …)` (in-app What's New; required for user-facing changes).

Commit. (Do not push yet — pushing deploys the website; that's the last step.)

## Step 3 — Build the portable EXE

```powershell
~/.dotnet/dotnet.exe publish -c Release
```

→ `bin\Release\net48\publish\Scalpel.exe` (single-file, ~8 MB, no loose `pdfium.dll` beside it) **plus** `Scalpel-<ver>-src.zip` (GPLv3 source bundle, produced automatically by the `BundleSource` target). There is no separate "portable zip" — the portable artifact IS the single EXE; the zip is the source bundle. Verify: `(Get-Item bin\Release\net48\publish\Scalpel.exe).VersionInfo.ProductVersion`.

Shipping posture: portable + installer are **unsigned** (deliberate; `release.ps1` signing is not part of a normal release — see RELEASING.md §6).

## Step 4 — Build the Inno Setup installer

```powershell
pwsh -File installer\build-installer.ps1
```

→ `installer\out\Scalpel-Setup.exe` (wraps the EXE from Step 3 — always rebuild after Step 3).

**Re-run this after Step 5 too:** the MSIX build re-publishes `Scalpel.exe` into the same folder, so an installer built before it wraps a different (non-identical) EXE than the one you upload in Step 6.

**ISCC.exe lives at the NON-standard per-user path** (rediscovered painfully twice — do not go hunting in Program Files first):

```
C:\Users\Liraz\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```

The script auto-locates it (`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` first, then Program Files, then PATH). If all miss: `winget install JRSoftware.InnoSetup` installs to the per-user path.

## Step 5 — Build the Store MSIX

```powershell
pwsh -File packaging\build-msix.ps1 -Store
```

→ `packaging\out\Scalpel_X.Y.Z.0_x64.msix` — **UNSIGNED, on purpose**; the Store signs it. `-Store` bakes in the Partner Center identity (`LirazShakaAmir.ScalpelPDF` / `CN=8B3919EF-…` / PublisherDisplayName `Liraz Shaka Amir`) and forces the `.0` revision.

- **Verify the identity still matches Partner Center → Product identity before every submission** (a single-character `PublisherDisplayName` mismatch rejects the upload).
- Known blocker: a stray untracked `docs/github-origin/` breaks this build's publish-profile pass (`CS0579`/`CS0246`) — move it aside, build, restore (RELEASING.md §7).

## Step 6 — Publish the GitHub Release (serves portable + installer)

The website hosts **no binaries** — `scalpel-pdf.netlify.app/download` and `/installer` are Netlify redirects to `releases/latest/download/<asset>`, so publishing = attaching assets to a GitHub Release. Asset names **must** be exactly `Scalpel.exe` and `Scalpel-Setup.exe`.

```powershell
gh release create vX.Y.Z `
    "bin\Release\net48\publish\Scalpel.exe" `
    "installer\out\Scalpel-Setup.exe" `
    "bin\Release\net48\publish\Scalpel-<full-ver>-src.zip" `
    --title "Scalpel X.Y.Z" --notes "…release notes…"
```

## Step 7 — STOP: manual Microsoft Store upload (browser, human-driven)

Do NOT automate this. Present this checklist and stop:

- [ ] Open **https://partner.microsoft.com/dashboard** → Apps and games → **Scalpel PDF** → start a new submission.
- [ ] **Packages**: upload `packaging\out\Scalpel_X.Y.Z.0_x64.msix` (the Store signs it; never self-sign).
- [ ] Identity sanity-check if anything account-related changed: https://partner.microsoft.com/dashboard/products/9N9HN8XW4LF3/identity
- [ ] **Store listing**: update the description / "What's new in this version" for this release; screenshots come from `store-assets/screenshots/` (only re-upload if they changed); keep license terms set to **GPLv3** (custom terms, not the Standard Application License Terms — `docs/STORE-PUBLISHING.md` §5).
- [ ] Submit for certification.

Live listing (for verification after certification): **https://apps.microsoft.com/detail/9n9hn8xw4lf3**

## Step 8 — Push to main / deploy the website (LAST)

Netlify auto-deploys the site from GitHub `main` (base dir `website/`), so the push is the website deploy — it takes `website/public/version.json` live and flips the in-app "update available" banner. Only do this once the GitHub Release (and ideally the Store submission) are done.

```powershell
git push    # pre-push hook bumps the revision, commits it, and ABORTS — this is by design
git push    # re-run; the second push goes through with the bump commit
```

## Quick reference

| Artifact | Command | Output |
|---|---|---|
| Portable EXE + src zip | `~/.dotnet/dotnet.exe publish -c Release` | `bin\Release\net48\publish\` |
| Installer | `pwsh -File installer\build-installer.ps1` | `installer\out\Scalpel-Setup.exe` |
| Store MSIX | `pwsh -File packaging\build-msix.ps1 -Store` | `packaging\out\Scalpel_<ver>.0_x64.msix` |
| Publish downloads | `gh release create vX.Y.Z …` | GitHub Release (latest) |
| Store upload | manual in browser (Step 7) | Partner Center submission |
| Website / update signal | `git push` (twice) — LAST | Netlify auto-deploy |

## Common mistakes

- Uploading to the Store with the `msstore` CLI → it hangs/crashes here; browser only.
- Forgetting `website/public/version.json` → the update check silently never fires (the #1 release footgun).
- Renaming a release asset → the `/download` / `/installer` redirects 404.
- Building the installer before re-publishing the EXE → it wraps the stale binary.
- Treating the first `git push` abort as an error → it's the bump hook; just push again.
