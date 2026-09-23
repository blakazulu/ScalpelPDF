#Requires -Version 5.1
<#
.SYNOPSIS
    Compiles the Scalpel Inno Setup installer into installer\out\Scalpel-<version>-Setup.exe.
.DESCRIPTION
    Locates ISCC.exe (the Inno Setup command-line compiler), derives the version
    from Scalpel.csproj (Major.Minor.Build) unless -Version is given, and builds
    Scalpel.iss against the published EXE.

    Prerequisites:
      - Inno Setup 6 installed (winget install JRSoftware.InnoSetup), and
      - a published EXE at bin\Release\net48\publish\Scalpel.exe
        (run: dotnet publish -c Release   — or release.ps1).
.PARAMETER Version
    Override the version stamped into the installer (defaults to the csproj version, 3 parts).
.PARAMETER SourceExe
    Override the source EXE path (defaults to bin\Release\net48\publish\Scalpel.exe).
.EXAMPLE
    pwsh -File installer\build-installer.ps1
#>
param(
    [string]$Version  = "",
    [string]$SourceExe = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # repo root (installer\ is one level down)
$iss  = Join-Path $PSScriptRoot "Scalpel.iss"

# ── Locate ISCC.exe ──────────────────────────────────────────────────────────
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw "ISCC.exe (Inno Setup compiler) not found. Install it with:  winget install JRSoftware.InnoSetup"
}
Write-Host "ISCC: $iscc" -ForegroundColor Cyan

# ── Resolve version (csproj -> Major.Minor.Build) ────────────────────────────
if (-not $Version) {
    $csproj = Join-Path $root "Scalpel.csproj"
    if (Test-Path $csproj) {
        $m = Select-String -Path $csproj -Pattern '<Version>([0-9]+\.[0-9]+\.[0-9]+)' | Select-Object -First 1
        if ($m) { $Version = $m.Matches[0].Groups[1].Value }
    }
    if (-not $Version) { $Version = "2.2.0" }
}
Write-Host "Version: $Version" -ForegroundColor Cyan

# ── Resolve source EXE ───────────────────────────────────────────────────────
if (-not $SourceExe) { $SourceExe = Join-Path $root "bin\Release\net48\publish\Scalpel.exe" }
if (-not (Test-Path $SourceExe)) {
    throw "Published EXE not found: $SourceExe`n  Build it first:  dotnet publish -c Release"
}
Write-Host "Source EXE: $SourceExe" -ForegroundColor Cyan

# ── Compile ──────────────────────────────────────────────────────────────────
& $iscc "/DAppVersion=$Version" "/DSourceExe=$SourceExe" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)." }

$out = Join-Path $PSScriptRoot "out\Scalpel-Setup.exe"
if (Test-Path $out) {
    $size = "{0:N0}" -f (Get-Item $out).Length
    Write-Host "`nInstaller built: $out ($size bytes)" -ForegroundColor Green
} else {
    Write-Warning "ISCC reported success but output not found at: $out"
}
