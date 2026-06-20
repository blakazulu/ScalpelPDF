# Publishing Scalpel to the Microsoft Store

This guide covers packaging Scalpel as an **MSIX** and submitting it to the Microsoft Store, plus how to build and test the package locally. It is additive: the existing portable EXE, winget, and Chocolatey channels are unchanged. The single-file `Scalpel.exe` produced by `dotnet publish` is the exact binary that goes inside the package.

> Not legal advice. Scalpel is **GPLv3**; see [§5 Licensing](#5-licensing-gplv3-on-the-store) — it must be handled deliberately on the Store.

---

## 1. How the app behaves when packaged

The app detects MSIX at runtime via `App.IsPackaged()` (`GetCurrentPackageFullName`). In packaged mode it **suppresses its self-installer**, because the package and the OS own those concerns:

| Concern | Portable / winget / choco | MSIX / Store |
|---|---|---|
| Install / uninstall | In-app installer → `%LOCALAPPDATA%\Programs`, Add/Remove Programs | OS installs/removes the package |
| `.pdf` association | `RegisterFileHandler()` writes `HKCU\Software\Classes` | Declared in `AppxManifest.xml` (`windows.fileTypeAssociation`) |
| "Install" portable badge | Shown when run outside install dir | Hidden (`IsPortable()` returns `false`) |
| Settings | `HKCU\Software\Scalpel\Settings` | Same call — registry is **virtualized** per-package and persists |
| Signatures / temp / logs | `%LOCALAPPDATA%\Scalpel\…` | Same call — redirected to the package's local store |
| Opening a `.pdf` | file path as `argv[1]` | Same — full-trust file association passes `argv[1]` |

No code path needs the app to know its package family; the existing command-line open logic and registry/AppData calls work as-is under MSIX virtualization.

---

## 2. Build a package locally (sideload test)

Prereqs: **.NET 8 SDK** and the **Windows 10/11 SDK** ("Signing Tools" component, which provides `makeappx`, `makepri`, `signtool`).

```powershell
# From the repo root — produces a self-signed .msix for local testing:
pwsh -File packaging\build-msix.ps1 -SelfSign
```

This publishes `Scalpel.exe`, stages the layout, generates `resources.pri`, packs `packaging\out\Scalpel_<version>_x64.msix`, and signs it with a generated dev cert.

**Install it (one-time cert trust, then the package):**

```powershell
# Elevated PowerShell — trust the dev cert once:
Import-Certificate -FilePath packaging\out\scalpel-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Then (normal PowerShell):
Add-AppxPackage packaging\out\Scalpel_<version>_x64.msix
# …or just double-click the .msix to use the App Installer UI.
```

Launch from the Start menu, confirm the **portable badge is gone**, set it as the default PDF handler via Windows Settings, and double-click a `.pdf` to confirm file association works.

**Uninstall the test package:**

```powershell
Get-AppxPackage *Scalpel* | Remove-AppxPackage
```

---

## 3. One-time Store account setup

1. Create a **Microsoft Partner Center** developer account (one-time fee) at <https://partner.microsoft.com>.
2. In Partner Center → **Apps and games** → **New product** → **MSIX/PWA app**, reserve the name **Scalpel** (or your chosen name).
3. Open the product → **Product management → Product identity**. Copy these three values exactly:
   - **Package/Identity/Name** (e.g. `1234Publisher.Scalpel`)
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-1234-1234-1234-1234567890AB`)
   - **Publisher display name**

These replace the dev defaults in the manifest. You do **not** create your own signing cert for the Store — the Store re-signs the package with the identity above.

---

## 4. Build the submission package

```powershell
pwsh -File packaging\build-msix.ps1 -NoSign `
     -IdentityName        "1234Publisher.Scalpel" `
     -Publisher           "CN=ABCD1234-1234-1234-1234-1234567890AB" `
     -PublisherDisplayName "Your Publisher Display Name"
```

`-NoSign` leaves the package unsigned; upload `packaging\out\Scalpel_<version>_x64.msix` in Partner Center under the submission's **Packages** step. Then fill in Store listing (description, screenshots — reuse `screenshots/`), age rating, pricing (Free), and markets, and submit for certification.

> Bump `<Version>` in `Scalpel.csproj` for each submission — the Store rejects a re-used version. The script derives the 4-part package version from it.

---

## 5. Licensing (GPLv3 on the Store)

Scalpel is GPLv3. The Microsoft Store's **Standard Application License Terms** can conflict with the GPL (the GPL forbids adding redistribution restrictions). Resolve it by supplying **your own license terms**:

- In the submission's **Properties / Store listing**, set the app's license terms to the **GPLv3** (link to `LICENSE` / the project's license URL) so the standard terms don't add incompatible restrictions.
- All bundled third-party libraries are permissively licensed (PdfSharpCore — MIT, PdfPig — Apache-2.0, Docnet/PDFium — MIT/BSD-3, Costura.Fody/CommunityToolkit/PolySharp — MIT), so they add no Store conflict.
- As sole copyright holder you retain full flexibility; the GPL binds redistributors, not you. If you accept outside contributions, keep them GPLv3-compatible.

Microsoft's OSS policy also forbids charging for freely-available OSS you didn't author — not an issue here (you're the author and it's Free).

---

## 6. What is NOT solved by packaging

- **Authenticode signing of the loose EXE** (`release.ps1`) still applies to the portable/winget/choco channels. The Store channel is signed by the Store and doesn't use `release.ps1`'s signing.
- **Auto-update**: Store handles updates for the Store channel; the in-app downgrade guard/installer only governs the portable channel.
- **`pdfium.dll` integrity check** still runs in packaged mode (it validates the Costura-embedded resource inside the same EXE) — keep `BuildInfo.PdfiumSha256` correct for signed builds.

---

## 7. File map

```
packaging/
├─ AppxManifest.xml      Package manifest with {tokens} substituted at build time
├─ generate-assets.ps1   Regenerates Assets/ logos from Resources\scalpel.ico
├─ build-msix.ps1        Publish → stage → makepri → makeappx → sign
├─ Assets/               Store tile/logo PNGs (generated)
└─ out/                  Build output: layout/, .msix, dev cert (git-ignored)
```
