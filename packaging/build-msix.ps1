<#
.SYNOPSIS
    Builds a Microsoft Store / sideload MSIX package for KillerPDF.

.DESCRIPTION
    1. Publishes the single-file KillerPDF.exe (Release, net48, win-x64).
    2. Stages a package layout (exe + assets + manifest with tokens substituted).
    3. Generates resources.pri.
    4. Packs the layout into an .msix with makeappx.
    5. Optionally signs it (self-signed for local testing, or a supplied cert).

    For a real Store submission, pass the Identity / Publisher / PublisherDisplayName
    values from Partner Center and DO NOT sign — the Store signs the package itself
    (use -NoSign). For local sideload testing, use -SelfSign: the script creates a
    matching self-signed cert and writes the .cer you must trust once (see -SelfSign
    output / STORE-PUBLISHING.md).

.EXAMPLE
    # Local test package, self-signed:
    pwsh -File packaging\build-msix.ps1 -SelfSign

.EXAMPLE
    # Store submission package (unsigned; upload the .msix to Partner Center):
    pwsh -File packaging\build-msix.ps1 -NoSign `
         -IdentityName "12345Publisher.KillerPDF" `
         -Publisher "CN=ABCD1234-1234-1234-1234-1234567890AB" `
         -PublisherDisplayName "Steve The Killer"
#>
[CmdletBinding(DefaultParameterSetName = 'SelfSign')]
param(
    [string]$Version             = '',                       # defaults to csproj <Version> + .0
    [string]$IdentityName        = 'KillerPDF',
    [string]$Publisher           = 'CN=KillerPDF Dev',       # MUST match signing cert subject
    [string]$PublisherDisplayName= 'KillerPDF',
    [string]$DisplayName         = 'KillerPDF',

    [Parameter(ParameterSetName='SelfSign')][switch]$SelfSign,   # create+use a self-signed cert
    [Parameter(ParameterSetName='Cert')][string]$CertPath,       # sign with an existing .pfx
    [Parameter(ParameterSetName='Cert')][string]$CertPassword,
    [Parameter(ParameterSetName='NoSign')][switch]$NoSign,       # leave unsigned (Store signs it)

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj  = Join-Path $repo 'KillerPDF.csproj'
$pkgDir= $PSScriptRoot
$outDir= Join-Path $pkgDir 'out'
$layout= Join-Path $outDir 'layout'
$publishDir = Join-Path $repo 'bin\Release\net48\publish'

# ── Resolve version (default from csproj) ──────────────────────────────────
if (-not $Version) {
    $csproj = [xml](Get-Content $proj)
    $v = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $v) { $v = '1.0.0' }
    $parts = @($v.ToString().Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }
    $Version = ($parts[0..3] -join '.')
}
Write-Host "==> Package version: $Version" -ForegroundColor Cyan

# ── Locate the .NET SDK ────────────────────────────────────────────────────
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $cand = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $cand) { $dotnet = $cand }
}
if (-not $dotnet) { throw "dotnet SDK not found. Install the .NET 8 SDK (https://dot.net) or add it to PATH." }

# ── Locate Windows SDK tools (newest version, host architecture) ───────────
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$binRoots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
function Find-SdkTool([string]$name) {
    foreach ($root in $binRoots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending |
               ForEach-Object { Join-Path $_.FullName "$arch\$name" } |
               Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    throw "$name not found in the Windows 10/11 SDK. Install the SDK (component 'Windows SDK Signing Tools')."
}
$makeappx = Find-SdkTool 'makeappx.exe'
$makepri  = Find-SdkTool 'makepri.exe'
$signtool = Find-SdkTool 'signtool.exe'
Write-Host "==> makeappx: $makeappx"

# ── 1. Publish the EXE ─────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "`n==> Publishing KillerPDF.exe (Release, net48, win-x64)..." -ForegroundColor Cyan
    & $dotnet publish $proj -c Release /p:PublishProfile=FolderProfile1
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
$exe = Join-Path $publishDir 'KillerPDF.exe'
if (-not (Test-Path $exe)) { throw "Published EXE not found at $exe" }

# ── 2. Stage the package layout ────────────────────────────────────────────
Write-Host "`n==> Staging package layout..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
Copy-Item $exe (Join-Path $layout 'KillerPDF.exe')
Copy-Item (Join-Path $pkgDir 'Assets') (Join-Path $layout 'Assets') -Recurse

# Substitute manifest tokens
$manifest = Get-Content (Join-Path $pkgDir 'AppxManifest.xml') -Raw
$manifest = $manifest.
    Replace('{IdentityName}',         $IdentityName).
    Replace('{Publisher}',            $Publisher).
    Replace('{PublisherDisplayName}', $PublisherDisplayName).
    Replace('{DisplayName}',          $DisplayName).
    Replace('{Version}',              $Version)
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

# ── 3. Generate resources.pri ──────────────────────────────────────────────
Write-Host "`n==> Generating resources.pri..." -ForegroundColor Cyan
$priConfig = Join-Path $outDir 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /o | Out-Null
Push-Location $layout
try {
    & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') `
        /of (Join-Path $layout 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { throw "makepri failed." }
} finally { Pop-Location }

# ── 4. Pack ────────────────────────────────────────────────────────────────
$msix = Join-Path $outDir "KillerPDF_$Version`_x64.msix"
Write-Host "`n==> Packing $msix ..." -ForegroundColor Cyan
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed." }
Write-Host "    Packed: $msix" -ForegroundColor Green

# ── 5. Sign ────────────────────────────────────────────────────────────────
switch ($PSCmdlet.ParameterSetName) {
    'NoSign' {
        Write-Host "`n==> -NoSign: package left unsigned. Upload the .msix to Partner Center; the Store signs it." -ForegroundColor Yellow
    }
    'Cert' {
        if (-not (Test-Path $CertPath)) { throw "CertPath not found: $CertPath" }
        Write-Host "`n==> Signing with $CertPath ..." -ForegroundColor Cyan
        $args = @('sign','/fd','SHA256','/f',$CertPath)
        if ($CertPassword) { $args += @('/p',$CertPassword) }
        $args += $msix
        & $signtool @args
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed." }
        Write-Host "    Signed." -ForegroundColor Green
    }
    'SelfSign' {
        Write-Host "`n==> Creating self-signed cert (subject $Publisher) for local testing..." -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher `
                    -KeyUsage DigitalSignature -FriendlyName 'KillerPDF Dev Signing' `
                    -CertStoreLocation 'Cert:\CurrentUser\My' `
                    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
        $pfx = Join-Path $outDir 'killerpdf-dev.pfx'
        $cer = Join-Path $outDir 'killerpdf-dev.cer'
        $pw  = ConvertTo-SecureString -String 'killerpdf' -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
        Export-Certificate    -Cert $cert -FilePath $cer | Out-Null
        & $signtool sign /fd SHA256 /f $pfx /p 'killerpdf' $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed." }
        Write-Host "    Signed with self-signed cert." -ForegroundColor Green
        Write-Host "`n    To install locally, trust the cert ONCE (elevated PowerShell):" -ForegroundColor Yellow
        Write-Host "      Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Yellow
        Write-Host "    then double-click the .msix (or: Add-AppxPackage `"$msix`")." -ForegroundColor Yellow
    }
}

Write-Host "`n==> Done." -ForegroundColor Green
Write-Host "    Output: $msix"
