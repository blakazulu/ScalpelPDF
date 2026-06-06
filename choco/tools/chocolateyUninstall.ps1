$ErrorActionPreference = 'Stop'

$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\KillerPDF.lnk'
if (Test-Path $startMenuPath) { Remove-Item $startMenuPath -Force }
