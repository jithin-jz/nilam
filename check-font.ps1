# Lists the font families WPF can actually see in the fonts folder, and says
# which one the weekday banner will use.
#
# The banner prefers a family named exactly "Anurati" if you have added your own
# copy, and otherwise uses the bundled "Michroma". A font file whose internal
# family name differs - "Anurati Regular", say - will not match, and the banner
# quietly moves on to the next choice. Run this after dropping a font in to
# confirm the name WPF reports.

Add-Type -AssemblyName PresentationCore

$dir = Join-Path $PSScriptRoot 'fonts'
if (-not (Test-Path $dir)) { Write-Host "No fonts folder at $dir" -ForegroundColor Red; exit 1 }

# -Include matches nothing on a bare directory path without -Recurse, so the
# extension is checked explicitly here.
$files = Get-ChildItem $dir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq '.otf' -or $_.Extension -eq '.ttf' }
Write-Host "Font files in $dir :" -ForegroundColor Cyan
if (-not $files) { Write-Host "  (none)" -ForegroundColor Yellow }
else { $files | ForEach-Object { Write-Host "  $($_.Name)  ($([math]::Round($_.Length/1KB)) KB)" } }

$uri = New-Object System.Uri(($dir + [IO.Path]::DirectorySeparatorChar))
$names = @([Windows.Media.Fonts]::GetFontFamilies($uri) | ForEach-Object { ($_.Source -split '#')[-1] })

Write-Host "`nFamily names WPF reports:" -ForegroundColor Cyan
if (-not $names) { Write-Host "  (none)" -ForegroundColor Yellow }
else { $names | ForEach-Object { Write-Host "  $_" } }

Write-Host ''
if ($names -contains 'Anurati') { Write-Host "The banner will use Anurati (your own copy)." -ForegroundColor Green }
elseif ($names -contains 'Michroma') { Write-Host "The banner will use Michroma (bundled)." -ForegroundColor Green }
else { Write-Host "Neither Anurati nor Michroma was found - the banner will fall back to Segoe UI." -ForegroundColor Yellow }
