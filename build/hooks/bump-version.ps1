# Bump the revision (4th) component of <Version>, <AssemblyVersion>, and
# <FileVersion> in KillerPDF.csproj, then commit. Invoked by build/hooks/pre-push.
$ErrorActionPreference = 'Stop'

$root   = (& git rev-parse --show-toplevel).Trim()
$csproj = Join-Path $root 'KillerPDF.csproj'

# Read raw so existing line endings (CRLF) are preserved untouched.
$text = Get-Content -LiteralPath $csproj -Raw

function Step-Revision([string] $version) {
	$parts = $version.Split('.')
	while ($parts.Count -lt 4) { $parts += '0' }
	$parts[3] = [string]([int] $parts[3] + 1)
	return ($parts -join '.')
}

$newVersion = $null
foreach ($tag in 'Version', 'AssemblyVersion', 'FileVersion') {
	$pattern = "<$tag>([^<]+)</$tag>"
	$match   = [regex]::Match($text, $pattern)
	if (-not $match.Success) {
		throw "pre-push: could not find <$tag> in $csproj"
	}
	$bumped = Step-Revision $match.Groups[1].Value
	$text   = [regex]::Replace($text, $pattern, "<$tag>$bumped</$tag>")
	if ($tag -eq 'Version') { $newVersion = $bumped }
}

# Preserve original encoding: UTF-8 without BOM.
[System.IO.File]::WriteAllText($csproj, $text, (New-Object System.Text.UTF8Encoding $false))

& git add -- $csproj
& git commit -m "Bump version to $newVersion [skip-bump]" | Out-Null

Write-Host "pre-push: bumped version to $newVersion"
