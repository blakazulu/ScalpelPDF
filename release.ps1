#Requires -Version 5.1
<#
.SYNOPSIS
    KillerPDF release script: build → sign → SHA256 → print summary.
.DESCRIPTION
    1. Publishes using FolderProfile1 (net48, win-x64); also runs bundle-source.ps1 to zip the source.
    2. Signs KillerPDF.exe with your Certum cert via signtool.
    3. Computes and prints the SHA256 for pasting into the landing pages.

.PARAMETER CertName
    CN (Subject) of your Certum certificate as it appears in the Windows cert store.
    Run: Get-ChildItem Cert:\CurrentUser\My | Select Subject
    to find it. Defaults to the placeholder below.

.PARAMETER SkipSign
    Skip signing (useful for a test build).

.EXAMPLE
    .\release.ps1 -CertName "Open Source Developer, Stephen ..."
#>
param(
    [string]$CertName   = "Open Source Developer Stephen Riley",
    [switch]$SkipSign
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$proj      = Join-Path $PSScriptRoot "KillerPDF.csproj"
$publishDir = Join-Path $PSScriptRoot "bin\Release\net48\publish"
$exe       = Join-Path $publishDir "KillerPDF.exe"

# ── 1. Build / Publish ──────────────────────────────────────────────────────
Write-Host "`n==> Building (Release, net48, win-x64)..." -ForegroundColor Cyan

# Find MSBuild
$msbuild = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}
if (-not $msbuild) {
    # Try dotnet as fallback
    $msbuild = "dotnet"
}

if ($msbuild -eq "dotnet") {
    & dotnet publish $proj /p:PublishProfile=FolderProfile1 -c Release
} else {
    & $msbuild $proj /t:Publish /p:PublishProfile=FolderProfile1 /p:Configuration=Release /m /nologo /v:m
}

if ($LASTEXITCODE -ne 0) { throw "Build failed." }
if (-not (Test-Path $exe)) { throw "EXE not found at: $exe" }
Write-Host "    EXE: $exe" -ForegroundColor Green

# ── 2. Sign ─────────────────────────────────────────────────────────────────
if (-not $SkipSign) {
    Write-Host "`n==> Signing with Certum cert: $CertName..." -ForegroundColor Cyan

    # Find signtool
    $signtool = $null
    $kitBase  = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitBase) {
        $signtool = Get-ChildItem "$kitBase\*\x64\signtool.exe" -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw "signtool.exe not found. Install Windows SDK." }
    Write-Host "    signtool: $signtool"

    & $signtool sign `
        /fd  sha256 `
        /tr  "http://timestamp.digicert.com" `
        /td  sha256 `
        /n   $CertName `
        /d   "KillerPDF" `
        /du  "https://pdf.killertools.net" `
        /v   $exe

    if ($LASTEXITCODE -ne 0) { throw "Signing failed. Is Certum SimplySign Desktop running?" }
    Write-Host "    Signed OK" -ForegroundColor Green
} else {
    Write-Host "`n==> Skipping signing (-SkipSign)" -ForegroundColor Yellow
}

# ── 3. SHA256 ────────────────────────────────────────────────────────────────
Write-Host "`n==> Computing SHA256..." -ForegroundColor Cyan

$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "    KillerPDF.exe : $exeHash" -ForegroundColor Green

# pdfium.dll is embedded into KillerPDF.exe via Costura.Fody (Unmanaged64Assemblies).
# Costura extracts it to a temp folder at runtime; AppLocker sees it there.
# Hash the pre-embed copy so AppLocker admins can create a hash rule for it.
$pdfiumSrc  = Join-Path $PSScriptRoot "bin\Release\net48\win-x64\pdfium.dll"
$pdfiumHash = $null
if (Test-Path $pdfiumSrc) {
    $pdfiumHash = (Get-FileHash $pdfiumSrc -Algorithm SHA256).Hash
    Write-Host "    pdfium.dll    : $pdfiumHash" -ForegroundColor Green
} else {
    Write-Host "    pdfium.dll    : not found at $pdfiumSrc (skipped)" -ForegroundColor Yellow
}

# ── 4. Source zip ────────────────────────────────────────────────────────────
$srcZip = Get-ChildItem $publishDir -Filter "*-src.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($srcZip) {
    Write-Host "`n==> Source zip: $($srcZip.FullName)" -ForegroundColor Green
} else {
    Write-Host "`n    (No source zip found - did bundle-source.ps1 run?)" -ForegroundColor Yellow
}

# ── 5. Write SHA256SUMS.txt ──────────────────────────────────────────────────
$sumsPath = Join-Path $PSScriptRoot "SHA256SUMS.txt"
$lines    = [System.Collections.Generic.List[string]]::new()
$lines.Add("KillerPDF.exe           $exeHash")
if ($pdfiumHash) { $lines.Add("pdfium.dll              $pdfiumHash") }
if ($srcZip) {
    $srcHash = (Get-FileHash $srcZip.FullName -Algorithm SHA256).Hash
    $lines.Add("$($srcZip.Name.PadRight(24))$srcHash")
}
# KillerPDF.zip is created manually before GitHub upload; add its hash separately.
[System.IO.File]::WriteAllLines($sumsPath, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "`n==> SHA256SUMS.txt written to: $sumsPath" -ForegroundColor Green

# ── 6. Summary ───────────────────────────────────────────────────────────────
Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host   "  KillerPDF release artifacts" -ForegroundColor White
Write-Host   "  EXE  : $exe"
if ($srcZip) { Write-Host "  SRC  : $($srcZip.FullName)" }
Write-Host   "  SHA256 (EXE): $exeHash" -ForegroundColor Green
if ($pdfiumHash) {
Write-Host   "  SHA256 (pdfium.dll, for AppLocker hash rules): $pdfiumHash" -ForegroundColor Green }
Write-Host   ""
Write-Host   "  Paste EXE SHA256 into:"
Write-Host   "    KillerPDF\pdf-landing\index.html (line ~183)"
Write-Host   "    killer-tools-site\src\tools\killer-pdf\killer-pdf.vue (line ~90)"
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
