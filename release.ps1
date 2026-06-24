#Requires -Version 5.1
<#
.SYNOPSIS
    Scalpel release script: build → sign → verify → hash → update BuildInfo → publish summary.
.DESCRIPTION
    1. Locates pdfium.dll in the NuGet cache, hashes it, and writes BuildInfo.cs so the
       embedded integrity check at startup knows the expected value.
    2. Publishes using FolderProfile1 (net48, win-x64); bundle-source.ps1 also runs.
    3. Signs Scalpel.exe. Prefers CertThumbprint (exact match) over CertName (CN match).
       Retries the timestamp across three TSA endpoints if the first attempt fails.
    4. Runs "signtool verify /pa /v" as a post-sign gate — aborts if the cert chain
       is not trusted to an accepted root.
    5. Prints thumbprint, SHA256s, and paste targets in the summary.

.PARAMETER CertThumbprint
    Preferred. SHA1 thumbprint of your code-signing certificate (40 hex chars, no spaces).
    Run: Get-ChildItem Cert:\CurrentUser\My | Select Thumbprint, Subject
    Omit if using CertName instead.

.PARAMETER CertName
    Fallback. CN (Subject) of your certificate as it appears in the Windows cert store.
    Ignored when CertThumbprint is supplied.

.PARAMETER SkipSign
    Skip signing. Writes all-zeros into BuildInfo.cs (disables runtime pdfium check).
    Useful for local test builds. Prints a red warning banner.

.PARAMETER OcrSourceDir
    Folder holding the bundled OCR engine for the "Windows (with OCR)" download:
    tesseract.exe (+ its dependent DLLs) and tessdata\eng.traineddata. When present, the
    script produces an extra "with OCR" zip (signed EXE + an adjacent ocr\ folder) so the
    installed build has OCR offline out of the box. The portable bare EXE is unaffected
    (it fetches language data on demand). Defaults to build\ocr-assets; skipped if absent.

.EXAMPLE
    .\release.ps1 -CertThumbprint "AABBCC..."
.EXAMPLE
    .\release.ps1 -CertName "Open Source Developer, Stephen Riley"
.EXAMPLE
    .\release.ps1 -SkipSign
.EXAMPLE
    .\release.ps1 -CertThumbprint "AABBCC..." -OcrSourceDir "D:\tesseract-portable"
#>
param(
    [string]$CertThumbprint = "",
    [string]$CertName       = "Open Source Developer Stephen Riley",
    [switch]$SkipSign,
    [string]$OcrSourceDir   = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$proj         = Join-Path $PSScriptRoot "Scalpel.csproj"
$buildInfoPath = Join-Path $PSScriptRoot "BuildInfo.cs"
$publishDir   = Join-Path $PSScriptRoot "bin\Release\net48\publish"
$exe          = Join-Path $publishDir "Scalpel.exe"

# TSA endpoints — tried in order; first success wins.
$tsaList = @(
    "http://timestamp.digicert.com",
    "http://timestamp.sectigo.com",
    "http://ts.ssl.com"
)

# ── 0. SimplySign preflight ──────────────────────────────────────────────────
if (-not $SkipSign) {
    $ssProc = Get-Process -Name "SimplySignDesktop" -ErrorAction SilentlyContinue
    if (-not $ssProc) {
        Write-Host ""
        Write-Warning "SimplySign Desktop does not appear to be running."
        Write-Host    "    Start it and wait for it to show 'Connected', then press Enter to continue."
        Write-Host    "    Or press Ctrl+C to abort."
        $null = Read-Host
    } else {
        Write-Host "`n==> SimplySign Desktop is running (PID $($ssProc.Id))." -ForegroundColor Green
    }
}

# ── 1. Hash pdfium.dll and update BuildInfo.cs ──────────────────────────────
Write-Host "`n==> Locating pdfium.dll for integrity pre-hash..." -ForegroundColor Cyan

# Look in the NuGet package cache for Docnet.Core's pdfium
$nugetCache = Join-Path $env:USERPROFILE ".nuget\packages"
$pdfiumNuget = Get-ChildItem "$nugetCache\docnet.core\*\runtimes\win-x64\native\pdfium.dll" `
                   -ErrorAction SilentlyContinue |
               Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName

# Also check the build output as a fallback
$pdfiumBuild = Join-Path $PSScriptRoot "bin\Release\net48\win-x64\pdfium.dll"

$pdfiumPath = $null
if ($pdfiumNuget -and (Test-Path $pdfiumNuget)) {
    $pdfiumPath = $pdfiumNuget
    Write-Host "    Using NuGet cache: $pdfiumPath"
} elseif (Test-Path $pdfiumBuild) {
    $pdfiumPath = $pdfiumBuild
    Write-Host "    Using build output: $pdfiumPath"
} else {
    Write-Warning "    pdfium.dll not found — BuildInfo.cs will retain all-zeros (check disabled)."
}

$pdfiumHash = "0000000000000000000000000000000000000000000000000000000000000000"
if ($pdfiumPath) {
    $pdfiumHash = (Get-FileHash $pdfiumPath -Algorithm SHA256).Hash
    Write-Host "    pdfium SHA256: $pdfiumHash" -ForegroundColor Green
}

if ($SkipSign) {
    # Leave all-zeros so the runtime check is disabled
    $pdfiumHash = "0000000000000000000000000000000000000000000000000000000000000000"
    Write-Host "    SkipSign: BuildInfo.cs will keep all-zeros (check disabled)." -ForegroundColor Yellow
}

Write-Host "`n==> Writing BuildInfo.cs..." -ForegroundColor Cyan
$buildInfoContent = @"
namespace Scalpel
{
    /// <summary>
    /// Build-time constants written or verified by release.ps1.
    /// </summary>
    internal static class BuildInfo
    {
        /// <summary>
        /// SHA256 of pdfium.dll (original bytes, before Costura compression).
        /// Updated by release.ps1 immediately before each build.
        /// All-zeros means the check is disabled (dev / SkipSign builds).
        /// </summary>
        internal const string PdfiumSha256 = "$pdfiumHash";

        internal const string PdfiumSha256Disabled = "0000000000000000000000000000000000000000000000000000000000000000";
    }
}
"@
[System.IO.File]::WriteAllText($buildInfoPath, $buildInfoContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "    BuildInfo.cs updated." -ForegroundColor Green

# ── 2. Build / Publish ──────────────────────────────────────────────────────
Write-Host "`n==> Building (Release, net48, win-x64)..." -ForegroundColor Cyan

$msbuild = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}
if (-not $msbuild) { $msbuild = "dotnet" }

if ($msbuild -eq "dotnet") {
    & dotnet publish $proj /p:PublishProfile=FolderProfile1 -c Release
} else {
    & $msbuild $proj /t:Publish /p:PublishProfile=FolderProfile1 /p:Configuration=Release /m /nologo /v:m
}

if ($LASTEXITCODE -ne 0) { throw "Build failed." }
if (-not (Test-Path $exe)) { throw "EXE not found at: $exe" }
Write-Host "    EXE: $exe" -ForegroundColor Green

# ── 3. Sign ─────────────────────────────────────────────────────────────────
if (-not $SkipSign) {
    Write-Host "`n==> Locating signtool..." -ForegroundColor Cyan
    $signtool = $null
    $kitBase  = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitBase) {
        $signtool = Get-ChildItem "$kitBase\*\x64\signtool.exe" -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK." }
    Write-Host "    $signtool"

    # Build cert selector args
    $certArgs = if ($CertThumbprint) {
        Write-Host "`n==> Signing with thumbprint $CertThumbprint..." -ForegroundColor Cyan
        @("/sha1", $CertThumbprint)
    } else {
        Write-Host "`n==> Signing with CN: $CertName..." -ForegroundColor Cyan
        @("/n", $CertName)
    }

    # Timestamp with retry across TSA list
    $signed = $false
    foreach ($tsa in $tsaList) {
        Write-Host "    Trying TSA: $tsa"
        & $signtool sign `
            /fd  sha256 `
            /tr  $tsa `
            /td  sha256 `
            @certArgs `
            /d   "Scalpel" `
            /du  "https://scalpel.example.com" `
            /v   $exe

        if ($LASTEXITCODE -eq 0) {
            Write-Host "    Signed and timestamped via $tsa" -ForegroundColor Green
            $signed = $true
            break
        }
        Write-Warning "    TSA $tsa failed (exit $LASTEXITCODE). Trying next..."
        Start-Sleep -Seconds 3
    }
    if (-not $signed) { throw "Signing failed on all TSA endpoints. Is SimplySign Desktop connected?" }

    # ── Post-sign verification gate ─────────────────────────────────────────
    Write-Host "`n==> Verifying signature chain (/pa)..." -ForegroundColor Cyan
    & $signtool verify /pa /v $exe
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify FAILED. The signed EXE does not pass trust validation. DO NOT RELEASE."
    }
    Write-Host "    Signature chain OK." -ForegroundColor Green

    # Print the thumbprint of the cert that was actually used
    try {
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($exe)
        $cert2 = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cert)
        $actualThumb = $cert2.Thumbprint
        $actualCN    = $cert2.GetNameInfo(
            [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
        Write-Host "    Signer : $actualCN" -ForegroundColor Green
        Write-Host "    Thumbprint: $actualThumb" -ForegroundColor Green
    } catch {
        Write-Warning "    Could not read signer info from signed EXE: $_"
        $actualThumb = "(unknown)"
        $actualCN    = "(unknown)"
    }
} else {
    Write-Host ""
    Write-Host "  #####################################################" -ForegroundColor Red
    Write-Host "  ##  WARNING: -SkipSign is set. EXE IS NOT SIGNED.  ##" -ForegroundColor Red
    Write-Host "  ##  DO NOT DISTRIBUTE this build as a release.     ##" -ForegroundColor Red
    Write-Host "  #####################################################" -ForegroundColor Red
    $actualThumb = "(not signed)"
    $actualCN    = "(not signed)"
}

# ── 4. SHA256 (final EXE) ─────────────────────────────────────────────────
Write-Host "`n==> Computing final EXE SHA256..." -ForegroundColor Cyan
$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "    Scalpel.exe : $exeHash" -ForegroundColor Green
if ($pdfiumPath) {
    Write-Host "    pdfium.dll    : $pdfiumHash" -ForegroundColor Green
}

# ── 4b. Stage "Windows (with OCR)" distribution ──────────────────────────────
# The installed/normal Windows download bundles the OCR engine; the portable bare EXE
# fetches language data on demand. Runtime resolution lives in Services/OcrAssets.cs
# (looks in <AppDir>\ocr first), and the self-installer copies an adjacent ocr\ folder
# into the install dir. Here we assemble that ocr\ folder next to the signed EXE.
Write-Host "`n==> Staging 'Windows (with OCR)' distribution..." -ForegroundColor Cyan

if (-not $OcrSourceDir) { $OcrSourceDir = Join-Path $PSScriptRoot "build\ocr-assets" }

$ocrZipPath = $null
$haveOcr     = $false
if (Test-Path $OcrSourceDir) {
    $srcTess = Join-Path $OcrSourceDir "tesseract.exe"
    $srcData = Join-Path $OcrSourceDir "tessdata\eng.traineddata"
    if ((Test-Path $srcTess) -and (Test-Path $srcData)) {
        $haveOcr = $true
    } else {
        Write-Warning "    '$OcrSourceDir' is missing tesseract.exe or tessdata\eng.traineddata — skipping OCR bundle."
    }
} else {
    Write-Warning "    OCR source not found: $OcrSourceDir"
    Write-Host    "    (Populate it with tesseract.exe + tessdata\eng.traineddata to build the 'with OCR' download." -ForegroundColor Yellow
    Write-Host    "     The portable EXE is still produced and fetches OCR data on demand.)" -ForegroundColor Yellow
}

if ($haveOcr) {
    $ver = "0.0.0"
    try { $ver = ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)).FileVersion } catch { }

    $stageDir = Join-Path $publishDir "with-ocr"
    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Path $stageDir | Out-Null

    # Signed EXE + an adjacent ocr\ folder (matches Services/OcrAssets.AppOcrDir).
    Copy-Item $exe (Join-Path $stageDir "Scalpel.exe")
    Copy-Item $OcrSourceDir (Join-Path $stageDir "ocr") -Recurse

    $ocrZipPath = Join-Path $publishDir "Scalpel-$ver-win-with-ocr.zip"
    if (Test-Path $ocrZipPath) { Remove-Item $ocrZipPath -Force }
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $ocrZipPath
    Write-Host "    With-OCR bundle: $ocrZipPath" -ForegroundColor Green
}

# ── 5. Source zip ────────────────────────────────────────────────────────────
$srcZip = Get-ChildItem $publishDir -Filter "*-src.zip" -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($srcZip) {
    Write-Host "`n==> Source zip: $($srcZip.FullName)" -ForegroundColor Green
} else {
    Write-Host "`n    (No source zip found — did bundle-source.ps1 run?)" -ForegroundColor Yellow
}

# ── 6. Write SHA256SUMS.txt ──────────────────────────────────────────────────
$sumsPath = Join-Path $PSScriptRoot "SHA256SUMS.txt"
$lines    = [System.Collections.Generic.List[string]]::new()
$lines.Add("Scalpel.exe           $exeHash")
if ($pdfiumPath) { $lines.Add("pdfium.dll              $pdfiumHash") }
if ($ocrZipPath -and (Test-Path $ocrZipPath)) {
    $ocrHash = (Get-FileHash $ocrZipPath -Algorithm SHA256).Hash
    $lines.Add("$((Split-Path $ocrZipPath -Leaf).PadRight(22))$ocrHash")
}
if ($srcZip) {
    $srcHash = (Get-FileHash $srcZip.FullName -Algorithm SHA256).Hash
    $lines.Add("$($srcZip.Name.PadRight(24))$srcHash")
}
[System.IO.File]::WriteAllLines($sumsPath, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "`n==> SHA256SUMS.txt written to: $sumsPath" -ForegroundColor Green

# ── 7. Summary ───────────────────────────────────────────────────────────────
Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host   "  Scalpel release artifacts" -ForegroundColor White
Write-Host   "  EXE  : $exe"
if ($ocrZipPath -and (Test-Path $ocrZipPath)) { Write-Host "  OCR  : $ocrZipPath" }
if ($srcZip) { Write-Host "  SRC  : $($srcZip.FullName)" }
Write-Host   ""
Write-Host   "  SHA256 (EXE)       : $exeHash" -ForegroundColor Green
if ($pdfiumPath) {
Write-Host   "  SHA256 (pdfium.dll): $pdfiumHash" -ForegroundColor Green }
Write-Host   ""
Write-Host   "  Signer : $actualCN"
Write-Host   "  Thumbprint: $actualThumb"
Write-Host   ""
Write-Host   "  Paste EXE SHA256 into:"
Write-Host   "    Scalpel\pdf-landing\index.html (line ~183)"
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
