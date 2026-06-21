<#
.SYNOPSIS
    Regenerates the Microsoft Store screenshots by driving the app's DEBUG /shoot harness.
.DESCRIPTION
    Builds Scalpel (Debug), runs `Scalpel.exe /shoot` which renders 6 PNGs into this folder,
    then verifies each is exactly 1920x1080. Close any running Scalpel.exe first (it locks
    pdfium.dll). The /shoot path exists only in DEBUG builds.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$shotDir = $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $cand = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $cand) { $dotnet = $cand }
}
if (-not $dotnet) { throw "dotnet SDK not found (PATH or ~/.dotnet/dotnet.exe)." }

Write-Host "==> Building Debug..." -ForegroundColor Cyan
& $dotnet build -c Debug (Join-Path $repo 'Scalpel.csproj')
if ($LASTEXITCODE -ne 0) { throw "build failed." }

$exe = Join-Path $repo 'bin\Debug\net48\Scalpel.exe'
if (-not (Test-Path $exe)) { throw "Scalpel.exe not found at $exe" }

Write-Host "==> Running /shoot harness..." -ForegroundColor Cyan
Start-Process -FilePath $exe -ArgumentList '/shoot' -Wait

$expected = @(
    '01-view-dark.png','02-edit-light.png','03-pages-dark.png',
    '04-sign-dark.png','05-highcontrast.png','06-edit-green.png')

Add-Type -AssemblyName System.Drawing
$fail = $false
foreach ($name in $expected) {
    $p = Join-Path $shotDir $name
    if (-not (Test-Path $p)) { Write-Host "  MISSING: $name" -ForegroundColor Red; $fail = $true; continue }
    $img = [System.Drawing.Image]::FromFile($p)
    try {
        $ok = ($img.Width -eq 1920 -and $img.Height -eq 1080)
        $color = if ($ok) { 'Green' } else { 'Red' }
        Write-Host ("  {0}  {1}x{2}" -f $name, $img.Width, $img.Height) -ForegroundColor $color
        if (-not $ok) { $fail = $true }
    } finally { $img.Dispose() }
}
if ($fail) { throw "One or more screenshots missing or not 1920x1080." }
Write-Host "==> Done. 6 screenshots verified." -ForegroundColor Green
