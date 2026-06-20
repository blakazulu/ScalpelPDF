$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'Scalpel'
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }

$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Scalpel.lnk'
if (Test-Path $startMenuPath) { Remove-Item $startMenuPath -Force }
