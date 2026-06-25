# Microsoft Store Publishing — Full Requirements Reference (2025–2026)

Everything required to publish a Windows app to the Microsoft Store via **Partner Center**
(<https://partner.microsoft.com>): the developer account, the package/code requirements, the
certification policies, the listing assets, and the submission workflow.

This is the **research/reference** companion to [`STORE-PUBLISHING.md`](STORE-PUBLISHING.md),
which is the concrete how-to for *this repo's* `packaging/build-msix.ps1` pipeline. Read this to
understand *what Microsoft demands*; read that to *do the build*.

> Scope: written for a **free, local-only, no-telemetry, GPLv3 desktop app** (i.e. Scalpel),
> packaged as **MSIX**. Sources are official Microsoft Learn pages unless noted. Items marked
> ⚠️ are version-dependent or were inferred — verify in the live Partner Center UI at submission.

---

## 0. TL;DR — the critical path for Scalpel

1. **Register a developer account — now $0.** The old $19 (individual) / $99 (company) fees are
   waived in the new onboarding flow. **You must start at <https://storedeveloper.microsoft.com>**
   or you get the legacy *paid* flow. Individual = ID + selfie, near-instant. Free app ⇒ **no
   payout/tax setup**.
2. **Reserve the name** "Scalpel" via *Apps and games → New product → MSIX or PWA app*. Held 3 months.
3. **Fix the version number.** MSIX `Identity/@Version` **must end in `.0`** (e.g. `1.5.1.0`).
   Scalpel's `1.5.1.16` scheme would **fail WACK/Store validation** — keep the build counter in
   `BuildInfo.cs`, not the package version. *(This is the single most likely hard blocker.)*
4. **Build the unsigned MSIX** with the Partner-Center identity tokens (`build-msix.ps1 -NoSign …`).
   The Store re-signs it — **no code-signing cert needed** for the MSIX path.
5. **Publish a privacy policy URL.** Win32/packaged apps are expected to supply one *even with zero
   telemetry* — a short "collects/transmits nothing" page suffices. Missing-but-required = cert failure.
6. **Set the license to GPLv3** in the listing's license-terms field so the Store's standard EULA
   doesn't add GPL-incompatible restrictions. Source zip is already bundled by `bundle-source.ps1`.
7. **Complete the IARC age-rating questionnaire** (answer "no IAP", no objectionable content → Everyone/3+).
8. **Fill the listing** (description, ≥1 screenshot, category = *Utilities + tools* → *PDF editor*),
   set price **Free** + markets, **Submit for certification**. Typically a few hours, up to 3 business days.

---

## 1. Developer account (Partner Center)

### Account types

| | **Individual** | **Company** |
|---|---|---|
| Publishes under | Your own name | A registered legal business entity |
| For | Hobby / personal / non-commercial | Business/trade/profession; orgs, teams |
| Sign-in | Personal Microsoft account (MSA) **only** | MSA **or** Microsoft Entra ID (work) |
| Verification | Government ID + selfie | Business + employment verification (+ due diligence) |
| Can publish desktop apps? | Yes | Yes |

- **You cannot convert Individual → Company** — it's a brand-new account.
- The dividing line is whether distribution is "in relation to your business, trade, or profession."
  A hobby project ⇒ Individual; a company-branded/commercial product ⇒ Company.
- For Scalpel (personal/hobby) **Individual** is the natural choice. Use Company only if you want to
  publish under a business identity (e.g. "Liraz Amir" as a registered entity).

### Fees — now zero (major 2025–2026 change)

- **Individual:** the $19 one-time fee is **waived** in the new flow (live ~Sept 2025).
- **Company:** the $99 one-time fee is **waived** in the new flow (announced May 2026).
- ⚠️ **Free vs paid hinges on the entry URL, not a setting.** Start at
  <https://storedeveloper.microsoft.com>. Entering via Partner Center / Xbox / Visual Studio
  directly may still show the **legacy paid** registration.

### Identity verification

- **Individual:** government-issued ID + selfie on mobile; auto-fills your profile. Near-instant;
  allow a few minutes for the Apps & Games workspace to appear.
- **Company:** three tracked steps — (1) **mandatory due diligence** (blocking), (2) **business
  verification** via a **9-digit D-U-N-S number** (fast, recommended) *or* official business
  documents (manual review), (3) **employment/contact verification** via a **work email on your
  org's domain** (personal Gmail/Yahoo not accepted). Automated checks: seconds; manual/document
  review: **2–5 business days**. Up to **3 appeals** per verification type; editing key fields
  (name/address/domain) **restarts** verification.
- **EU DSA note (companies):** EU trader verification (DUNS or recent business doc + full contact
  info) is required to keep products available in **EU markets**; folded into company verification.
  Individual accounts need no DSA action.

### Payout / tax — **not needed for a free app**

- Microsoft states plainly: *"If you only plan to list free offers, you don't need to set up a
  payout account or fill out any tax forms."* You can add them later if you ever monetize.
- (If you ever sell: tax profile first, then payout profile; tax validation up to 48h.)

### Publisher display name

- Public name on your listings; **identity-bound** (derived from verified ID / legal name), not
  free text — especially for company accounts. Must be unique and not infringe trademarks.

### Reserve the app name

- *Apps and games* (<https://aka.ms/submitwindowsapp>) → **New product** → **MSIX or PWA app**
  (separate **MSI or EXE app** option exists for raw installers) → type name → **Check
  availability** → **Reserve product name**.
- Reservation is held **up to 3 months**; unused ⇒ released. You can reserve multiple names.
- A name can show as "taken" in the Store yet be unreservable (someone reserved without submitting).

**Sources:** [open-a-developer-account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account) ·
[account-types-locations-and-fees](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/account-types-locations-and-fees) ·
[whats-new-individual-developer](https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-individual-developer) ·
[whats-new-company-developer](https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-company-developer) ·
[store-business-verification-reqs](https://learn.microsoft.com/en-us/windows/apps/publish/store-business-verification-reqs) ·
[set-up-your-payout-account](https://learn.microsoft.com/en-us/partner-center/account-settings/set-up-your-payout-account) ·
[reserve-your-apps-name (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name)

---

## 2. Package & code requirements (MSIX)

### Architecture & format

- One package = one architecture: **x86 / x64 / ARM / ARM64** (or `neutral`). A **single x64
  package is sufficient** for an x64-only app like Scalpel (it bundles native `pdfium.dll`).
- Upload `.msix`/`.appx` (single) or `.msixbundle`/`.appxbundle` (multi-arch/resource). VS's
  `.msixupload` wrapper is recommended but **not required** — a `makeappx`-built `.msix` is
  accepted (which is what `build-msix.ps1` produces). Block-map hashes use SHA2-256.
- **Size limit:** 25 GB per package and per bundle (Scalpel's ~6 MB is a non-issue).

### `AppxManifest.xml` required elements

- **`<Identity>`** — `Name`, `Publisher`, `Version`, `ProcessorArchitecture`. `Name`/`Publisher`
  are **case-sensitive** and must **exactly match** the Partner Center reserved identity (the repo
  substitutes these via `{token}` placeholders).
- **`<Properties>`** — `DisplayName`, `PublisherDisplayName`, `Logo`.
- **`<Dependencies><TargetDeviceFamily>`** — `Name="Windows.Desktop"`, `MinVersion`,
  `MaxVersionTested`. `MaxVersionTested` is **not a cap** (higher OS still installs).
  E.g. `MinVersion="10.0.17763.0"` (Win10 1809).
- **`<Capabilities>`** — see below.
- **`<Applications><Application>`** — entry point with `uap10:TrustLevel="mediumIL"` (full-trust
  desktop) and the `.pdf` file-type associations.

### ⚠️ Version numbering — the common trip-up

- Quad format `Major.Minor.Build.Revision`. **Major ≠ 0**; others 0–65535.
- **The 4th part (Revision) is reserved for the Store and MUST be `0`** in your build. WACK's
  "App count" test enforces this.
- **Scalpel's `1.5.1.16` would fail** — the MSIX `Identity/@Version` must end in `.0`
  (e.g. `1.5.1.0`). Keep the build counter outside the package version.
- The Store always serves the **highest** applicable version; bump it every submission.

### Capabilities

- **`runFullTrust`** (restricted/`rescap` namespace) is the **normal, expected** declaration for
  every packaged Win32 desktop app — approved as a matter of course. `makeappx` errors if it's
  needed and missing.
- **Other restricted (`rescap`) capabilities** require per-capability justification on the
  *Submission options* page and **add certification time**; some (`screenDuplication`,
  `userPrincipalName`, `walletSystem`, etc.) are almost never approved.
- **`documentsLibrary` / `enterpriseAuthentication` / `sharedUserCertificates`** are "special use" —
  **company accounts only** + extra review. A file-picker does **not** need `documentsLibrary`.
- **Keep Scalpel's capability set minimal — essentially just `runFullTrust`.** Broad capabilities
  also auto-flag the privacy-policy requirement (§3).

### Code signing — the Store does it for you (MSIX)

- **You do NOT need a CA cert for the MSIX path.** After certification the **Store re-signs** the
  package with a Microsoft certificate. No `.pfx`/token/HSM, no SmartScreen warnings.
- **Self-signed** signing is only for **sideload/test** (the repo's `build-msix.ps1 -SelfSign`).
- **Azure Trusted Signing** (~$9.99/mo) is for distribution **outside** the Store — not needed for
  the MSIX→Store path.

### Windows App Certification Kit (WACK)

- Run **locally before submitting** (`appcert.exe` / "Windows App Certification Kit" GUI from the
  Windows SDK). The Store **also runs its own** certification (online WACK + manual review) —
  unavoidable and gating.
- Tests: launch/stability (no crash), platform-version-launch, **app-count (revision must be 0)**,
  manifest compliance, **BinScope security** (ASLR/DEP/SafeSEH, AppContainer bit on all
  exe/native DLLs, no writable+executable sections), supported-API (**debug builds fail**),
  release-config, capabilities, no stray `.pfx`/`.snk` in the package, MAX_PATH.
- **Scalpel-specific watch-items:** (1) version `.0`; (2) build **Release**, not Debug;
  (3) bundled `pdfium.dll` must pass BinScope's ASLR/DEP/AppContainer checks — run WACK to confirm;
  (4) don't stage signing artifacts into the package layout.

### .NET Framework 4.8 note

- The Store officially supports WPF/WinForms as MSIX. The self-contained vs framework-dependent
  question is **moot for `net48`** — .NET Framework 4.8 is an in-box OS component on all supported
  Windows 10/11, so there's no runtime to bundle. ⚠️ (Inferred from how 4.8 ships; Microsoft has
  no 4.8-specific MSIX doc.)

**Sources:** [app-package-requirements (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements) ·
[device-architecture](https://learn.microsoft.com/en-us/windows/msix/package/device-architecture) ·
[element-targetdevicefamily](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-targetdevicefamily) ·
[app-capability-declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) ·
[code-signing-options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) ·
[WACK tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests)

---

## 3. Store Policies & certification rules

Policies are versioned (current: **v7.19**, effective Oct 14, 2025). Product policies = section
**10.x**, content policies = **11.x**. The ones an indie desktop utility must satisfy:

| Policy | Requirement | Scalpel status |
|---|---|---|
| **10.1** Distinct function & value | Unique name (no keyword stuffing), honest metadata matching real features, value clear on first run, experience happens **in-app, not in a browser** | Easy pass (substantial native editor) |
| **10.2** Security | No malware/unwanted behavior; no executing remote code that changes described behavior; clean uninstall (**10.2.7**); consent for changing Windows defaults | Pass — MSIX = OS-owned clean uninstall; self-contained |
| **10.3** Testable | Provide test steps/credentials in **Notes for certification** if anything is hidden/locked | Provide brief notes |
| **10.4** Usability/stability | **Must not crash**, must start promptly, stay responsive, shut down gracefully, handle API exceptions (**10.4.2**); must **not crash without network** | Pass — local-only; crash sinks help |
| **10.5** Personal information | **Privacy policy required** if it accesses/collects/transmits personal info — *and* the policy explicitly names **Win32/Desktop-Bridge products as always needing one** | **Action: publish a privacy policy URL** (see below) |
| **10.6** Capabilities | Declare only what's used | Keep to `runFullTrust` |
| **10.8** Financial | Only applies with IAP/subscriptions | **N/A (free)** |
| **10.14** Account type | Company account required if app captures financial info | Individual OK |
| **11.1 / 11.2** Content & IP | Listing content ≤ PEGI 12 / ESRB E10+; no infringing names/logos | Pass |
| **11.11** Age rating | **Mandatory IARC questionnaire** | Complete it (→ Everyone/3+) |

### Privacy policy — the #1 desktop-app trip-up

Policy **10.5.1**: apps that access/collect/transmit personal information must have a privacy policy
URL entered in Partner Center — **and it explicitly says "product types that inherently have access
to Personal Information must always have privacy policies… including Win32 products."** Partner
Center also auto-sets the requirement to "Yes" if your package declares capabilities that *could*
touch personal info.

➡️ **Plan to publish a short privacy policy even though Scalpel has zero telemetry** — a page stating
"Scalpel processes PDFs entirely locally and collects/transmits nothing" satisfies it. (The
Developer Agreement §3.2(c) also independently requires one with an in-app link.)

### Telemetry

No policy *requires* or *forbids* telemetry. **Zero telemetry is fully compliant** and reduces the
10.5 burden — but you still likely need the privacy-policy URL per the Win32 rule above.

### Age rating (IARC)

Mandatory. Complete the multiple-choice questionnaire (or reuse an existing IARC rating ID); it
yields region-specific ratings. ⚠️ The questionnaire asks about **in-app purchases** — answer
honestly; a mismatch between IAP declarations and the questionnaire is a known rejection trigger.
For a free offline utility expect the lowest tier.

### Common rejection reasons

Unfinished app / "under construction" links · crashes (incl. offline) · **missing privacy policy
URL** · not testable (no credentials/steps) · minimal value / browser-wrapper · inaccurate metadata ·
package name not matching the reserved name · incomplete/inaccurate age-rating answers · falsely
declaring accessibility.

### Appeals

A failed submission gets an **emailed certification report** stating the failing test/policy. Fix →
new submission. Status/appeals questions: **reportapp@microsoft.com**.

**Sources:** [store-policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) ·
[avoid-common-certification-failures](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/avoid-common-certification-failures) ·
[Developer Agreement (mdsa)](https://learn.microsoft.com/en-us/legal/mdsa) ·
[age-ratings](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/age-ratings)

---

## 4. Licensing — GPLv3 on the Store

- **You license the app to customers, not Microsoft.** The submission lets you supply **your own
  license terms** instead of (or alongside) the standard EULA. ➡️ **Set the license to GPLv3** so
  the standard terms don't add GPL-incompatible redistribution restrictions.
- **"Excluded License" clause (⚠️ verify against the exact agreement at submission):** Microsoft's
  marketplace/publisher agreements define GPL/LGPL/AGPL as "Excluded Licenses" and forbid actions
  that would cause **a *Microsoft* product/service** to become governed by them. This is about not
  *infecting Microsoft's* software with copyleft — it is **not** a ban on publishing **your own**
  GPLv3 app (GPLv3 apps exist on the Store). Care points: (1) the Store/anti-tamper layer must not
  conflict with GPLv3's anti-tivoization clauses; (2) satisfy your source-availability obligation
  independently — **already done** via the bundled `Scalpel-<version>-src.zip` (`bundle-source.ps1`).
- **Bundled libraries are all permissive** (PdfSharpCore MIT, PdfPig Apache-2.0, Docnet/PDFium
  MIT/BSD-3, Costura.Fody/CommunityToolkit/PolySharp MIT) — no added Store conflict.
- **Repackaged-OSS naming rule** (title must show added value, or include the seller's name) targets
  people relisting *someone else's* OSS unchanged — **not triggered** for an original app that merely
  *uses* OSS libraries.
- Microsoft's OSS policy forbids charging for freely-available OSS you didn't author — **N/A** (you're
  the author and it's free).

**Sources:** [marketplace certification-policies](https://learn.microsoft.com/en-us/legal/marketplace/certification-policies) ·
[msft-publisher-agreement](https://learn.microsoft.com/en-us/legal/marketplace/msft-publisher-agreement) ·
[store-policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)

---

## 5. Listing assets

### 5a. In-package image assets (inside the MSIX, all PNG)

Referenced from `AppxManifest.xml`; generated by `packaging/generate-assets.ps1`. Windows uses MRT
qualifiers (`scale-NN`, `targetsize-NN`, plus `_altform-unplated` / `_altform-lightunplated` theme
variants). **Never combine `scale-` and `targetsize-` on one file.**

| Asset (manifest name) | Base (100%) | 200% | 400% |
|---|---|---|---|
| App icon `Square44x44Logo` | 44×44 | 88×88 | 176×176 |
| Store logo `StoreLogo` | 50×50 | 100×100 | 200×200 |
| Medium tile `Square150x150Logo` | 150×150 | 300×300 | 600×600 |
| Small tile `Square71x71Logo` | 71×71 | 142×142 | 284×284 |
| Wide tile `Wide310x150Logo` | 310×150 | 620×300 | 1240×600 |
| Large tile `Square310x310Logo` | 310×310 | 620×620 | 1240×1240 |
| Splash `SplashScreen` | 620×300 | 1240×600 | 2480×1200 |

- **Manifest-required:** `Square44x44Logo`, `Square150x150Logo`, `StoreLogo`.
- **Minimum to publish:** Square44x44 (with unplated/light-unplated **target-size** variants — at
  least 16, 24, 32, 48, 256), Square150x150 @100%, StoreLogo scale variants.
- **Optional / Win10 Live Tiles only:** small/wide/large tiles, splash, badge (Win11 ignores tiles).
- ⚠️ 125%/150% sizes follow base×scale and match the VS generator; 100/200/400 are doc-confirmed.

### 5b. Store-listing logo (Partner Center)

- **App tile icon — 300×300 PNG (1:1)** — strongly recommended; **overrides** the in-package
  StoreLogo as the listing thumbnail. (Poster/box art are games/Xbox only.)

### 5c. Screenshots

| Property | Value |
|---|---|
| Format | PNG only, ≤ 50 MB each |
| Count | **min 1** (required); **max 10** desktop (8 other device families); 4+ recommended |
| Desktop min size | 1366×768 (4K OK) |
| Caption | optional, ≤ 200 chars |

Keep key content in the top two-thirds; no added logos/marketing text. (Reuse the repo's `store-assets/screenshots/`.)

### 5d. Promotional assets (optional)

- **Super hero art** — 16:9 **1920×1080** or **3840×2160** PNG, **no text** — required for trailers
  to appear at top; makes the app **eligible for featuring**.
- **Trailers** — up to 15, `.mp4`/`.mov`, 1920×1080, ≤ 2 GB, ≤ 60 s recommended; PNG thumbnail required.

### 5e. Text metadata

| Field | Limit | Required? |
|---|---|---|
| **Description** | 10,000 chars | **Yes** (the one mandatory text field) |
| Short description | 1,000 chars (≤270 recommended) | Optional |
| What's new | 1,500 chars | Optional |
| Product features | up to 20 × ≤200 chars | Optional (bullets) |
| **Keywords** (search terms) | up to **7** keywords, 40 chars each, ≤21 total words | Optional (discoverability) |
| Short / sort / voice title | 50 / 255 / 255 chars | Optional |
| Additional license terms | 10,000 chars or one URL | Optional (GPLv3 here) |
| Copyright/trademark | 200 chars | Optional |
| "Developed by" | 255 chars | Optional |

- **Localization:** complete ≥1 language; recommended for every package language (Scalpel ships 6).
  Edit per-language or via CSV import/export. No auto-translate.

### 5f. Product declarations (Properties page)

- **Accessibility** — *"This product has been tested to meet accessibility guidelines"* — unchecked
  by default; only check if genuinely engineered/tested (keyboard nav, ≥4.5:1 contrast, Narrator, etc.).
- **Pen/ink support** declaration is potentially relevant to Scalpel's inking/signature features.
- Alternate-drive install and OneDrive-backup declarations are checked by default.

**Sources:** [screenshots-and-images (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images) ·
[add-and-edit-store-listing-info (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info) ·
[add-additional-information (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-additional-information) ·
[categories-and-subcategories (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/categories-and-subcategories) ·
[product-declarations (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/product-declarations)

---

## 6. Submission workflow

After reserving the name, the product page shows **Product release → Start submission**. Steps can
be done in any order; finish with **Submit for certification**.

| Step | Required inputs | Notes for Scalpel |
|---|---|---|
| **Pricing & availability** | Markets, Audience, Discoverability, Schedule, Base price | Price **Free**; all/240+ markets; discoverable |
| **Properties** | **Category**; privacy-policy URL (conditional); contact (companies) | Category **Utilities + tools → PDF editor** (or Productivity); add privacy URL |
| **Age ratings** | IARC questionnaire (all required) | Answer honestly → Everyone/3+ |
| **Packages** | ≥1 `.msix`/`.msixbundle` | Upload the `-NoSign` package |
| **Store listings** | Description + ≥1 screenshot (per language) | Reuse `store-assets/screenshots/`; 6 locales |
| **Submission options** | Restricted-capability justification (if any); Notes for certification | Justify only if non-`runFullTrust` caps |

### Certification

- **Timeline:** "up to **3 business days**," usually **a few hours**. After passing, the listing
  appears to customers on average within ~**15 minutes**.
- **What's reviewed:** (1) security scan (malware/static+dynamic), (2) technical compliance (WACK +
  install-and-run, no crashes/prohibited APIs), (3) content compliance (listing vs policies, manual).
- **Failure ⇒ emailed certification report** naming the failing test/policy. Fix → new submission.
- **Post-publish spot checks** can remove non-compliant apps. Once publishing begins you can't cancel.

### After approval — updates & rollout (MSIX advantages)

- **Update** = *Start update* (pre-populates from the last submission) → edit → resubmit. **Must bump
  the version** (higher than published) for the Store to treat it as an update — maps to the repo's
  existing bump convention (but remember the **`.0` revision rule**).
- **Store-managed auto-update:** OS checks ~every 24h and updates customers automatically.
- **Gradual rollout (MSIX only):** push an update to a % of users, monitor, then increase or halt —
  no new submission needed.
- **Package flights (MSIX only):** distribute to specific named beta users/groups.

**Sources:** [create-app-submission (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission) ·
[get-your-app-certified (FAQ)](https://learn.microsoft.com/en-us/windows/apps/publish/faq/get-your-app-certified) ·
[publish-update-to-your-app-on-store (MSIX)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/publish-update-to-your-app-on-store) ·
[gradual-package-rollout](https://learn.microsoft.com/en-us/windows/apps/publish/gradual-package-rollout) ·
[package-flights](https://learn.microsoft.com/en-us/windows/apps/publish/package-flights)

---

## 7. MSIX vs raw EXE/MSI — why Scalpel should use MSIX

Microsoft has allowed **unpackaged Win32 EXE/MSI** submissions since June 2021 (you self-host a
versioned HTTPS installer URL; the Store lists it). But for Scalpel the **MSIX path is clearly better**:

| Feature | MSIX (Store-managed) | EXE/MSI (self-hosted) |
|---|---|---|
| Hosting | **Free, by Microsoft** | You host + pay |
| Code signing | **Free, Store re-signs** | You sign (Microsoft Trusted Root CA cert) |
| Auto-update | **OS every ~24h** | App self-updates |
| S-mode support | **Yes** | No |
| Gradual rollout / flighting / private app | **Yes** | No |
| Silent-install requirement | n/a | **Required — no installer UI** (would break Scalpel's branded `InstallerUI.cs` dialogs) |

The repo already scaffolds MSIX (`build-msix.ps1`, `AppxManifest.xml`) and `App.IsPackaged()`
suppresses the self-installer when packaged — fully aligned with the MSIX route. The raw-EXE route
would require keeping a CA cert, self-hosting, a custom updater, **and** a silent (UI-less) installer.

**Source:** [how-to-distribute-your-win32-app-through-microsoft-store](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)

---

## 8. Open items to verify at submission

- ⚠️ **Confirm the "Excluded License"/GPL clause** in the exact agreement Partner Center presents.
- ⚠️ **Privacy-policy URL**: the Win32 "always" wording + capability auto-flag both point to *required* —
  plan to provide one regardless.
- ⚠️ **Version `.0` revision** — adjust the MSIX version before the first submission.
- ⚠️ Run **WACK locally** to confirm `pdfium.dll` passes BinScope (ASLR/DEP/AppContainer).
- ⚠️ Schedule/first-publish date may be **unchangeable after first publish** — verify in the live UI.

---

*Compiled from official Microsoft Learn documentation (2025–2026). See per-section source links.
For the build mechanics, see [`STORE-PUBLISHING.md`](STORE-PUBLISHING.md).*
