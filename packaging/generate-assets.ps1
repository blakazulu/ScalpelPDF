<#
.SYNOPSIS
    Generates the MSIX / Microsoft Store visual assets from Resources\scalpel.ico.

.DESCRIPTION
    Renders the highest-resolution frame of the app icon into the PNG tile/logo
    assets referenced by packaging\AppxManifest.xml. Square targets are a direct
    high-quality resize; wide and splash targets center the square logo on a
    transparent canvas. Re-run this whenever the source icon changes.

    Uses WPF imaging (PresentationCore) rather than System.Drawing because the
    source .ico stores PNG-compressed frames that GDI+ cannot decode.
#>
[CmdletBinding()]
param(
    [string]$IconPath = (Join-Path $PSScriptRoot '..\Resources\scalpel.ico'),
    [string]$OutDir   = (Join-Path $PSScriptRoot 'Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ── Load the largest frame from the .ico ───────────────────────────────────
$uri     = [Uri]::new((Resolve-Path $IconPath).Path)
$decoder = [System.Windows.Media.Imaging.IconBitmapDecoder]::new(
    $uri,
    [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)

$src = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
Write-Host ("Source frame: {0}x{1}" -f $src.PixelWidth, $src.PixelHeight)

function Save-Png {
    param(
        [int]$CanvasW, [int]$CanvasH, [int]$LogoBox, [string]$FileName
    )
    # Fit the logo into a LogoBox-square, centered on a transparent CanvasW x CanvasH.
    $scale  = $LogoBox / [Math]::Max($src.PixelWidth, $src.PixelHeight)
    $logoW  = [int][Math]::Round($src.PixelWidth  * $scale)
    $logoH  = [int][Math]::Round($src.PixelHeight * $scale)
    $offX   = [int][Math]::Round(($CanvasW - $logoW) / 2)
    $offY   = [int][Math]::Round(($CanvasH - $logoH) / 2)

    $visual  = [System.Windows.Media.DrawingVisual]::new()
    $ctx     = $visual.RenderOpen()
    $ctx.DrawImage($src, [System.Windows.Rect]::new($offX, $offY, $logoW, $logoH))
    $ctx.Close()

    $rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $CanvasW, $CanvasH, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $path = Join-Path $OutDir $FileName
    $fs   = [System.IO.File]::Create($path)
    try { $encoder.Save($fs) } finally { $fs.Dispose() }
    Write-Host ("  {0,-26} {1}x{2}" -f $FileName, $CanvasW, $CanvasH)
}

# ── Square logos: logo fills the whole canvas ──────────────────────────────
Save-Png  44  44  44 'Square44x44Logo.png'
Save-Png 150 150 150 'Square150x150Logo.png'
Save-Png  71  71  71 'Square71x71Logo.png'
Save-Png 310 310 310 'Square310x310Logo.png'
Save-Png  50  50  50 'StoreLogo.png'

# ── Wide / splash: logo centered, padded on a transparent canvas ───────────
Save-Png 310 150 132 'Wide310x150Logo.png'
Save-Png 620 300 220 'SplashScreen.png'

Write-Host "Done. Assets written to $OutDir"
