param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
if (Get-Process Voyage*,Vortex -ErrorAction SilentlyContinue) { throw 'Spiel und Vortex vor der Installation schliessen.' }
$game = (Resolve-Path -LiteralPath $GamePath).Path
$paks = Join-Path $game 'Voyage\Content\Paks'
if (-not (Test-Path -LiteralPath (Join-Path $game 'Voyage\Binaries\Win64\VoyageSteam-Win64-Shipping.exe'))) { throw 'Kein passender Spielordner.' }
if (-not (Test-Path -LiteralPath (Join-Path $game 'Voyage\Binaries\Win64\version.dll'))) { throw 'AssetBridge zuerst installieren (siehe README).' }
$source = Join-Path $PSScriptRoot 'Voyage\Content\Paks\~mods\CaretakerEnergy'
$expected = @('CaretakerEnergy_P.pak','CaretakerEnergy_P.utoc','CaretakerEnergy_P.ucas','CaretakerEnergy_P.tlcaddon.json')
foreach ($name in $expected) { if (-not (Test-Path -LiteralPath (Join-Path $source $name))) { throw "Release-Datei fehlt: $name" } }
$manifestPath = Join-Path $PSScriptRoot 'package-hashes.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Count -ne 4 -or @($manifest.name | Sort-Object -Unique).Count -ne 4) { throw 'Unerwartete Paketliste.' }
foreach ($record in $manifest) { if ($record.name -notin $expected) { throw 'Unbekannte Datei im Manifest.' } }
foreach ($record in $manifest) { if ((Get-FileHash -LiteralPath (Join-Path $source $record.name) -Algorithm SHA256).Hash -ne $record.sha256) { throw "Pruefsumme falsch: $($record.name)" } }
$known = @('CaretakerEnergy','CaretakerFuelGenerators','CaretakerPetrolRefinery')
$allowedPaths = $known | ForEach-Object { Join-Path $paks "~mods\$_" }
Get-ChildItem -LiteralPath $paks -Recurse -File -Filter '*.utoc' | Where-Object { $_.BaseName -in @('CaretakerEnergy_P','CaretakerFuelGenerators_P','CaretakerPetrolRefinery_P') } | ForEach-Object {
 if ($_.DirectoryName -notin $allowedPaths) { throw "Weitere Installation gefunden: $($_.FullName). Vorher aus Paks herausnehmen." }
}
$backup = Join-Path $game ('CaretakerModBackups\Energy-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($old in $allowedPaths) {
 if (Test-Path -LiteralPath $old) {
  $resolved = (Resolve-Path -LiteralPath $old).Path
  if (-not $resolved.StartsWith($paks + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsicherer Zielpfad.' }
  Move-Item -LiteralPath $resolved -Destination $backup
 }
}
$dest=Join-Path $paks '~mods\CaretakerEnergy'
try {
 New-Item -ItemType Directory -Path $dest -Force | Out-Null
 foreach ($name in $expected) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $dest }
 foreach ($record in $manifest) { if ((Get-FileHash -LiteralPath (Join-Path $dest $record.name) -Algorithm SHA256).Hash -ne $record.sha256) { throw 'Pruefsumme nach Installation falsch.' } }
} catch {
 if (Test-Path -LiteralPath $dest) {
  $resolved=(Resolve-Path -LiteralPath $dest).Path
  if (-not $resolved.StartsWith($paks+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsicherer Wiederherstellungspfad.' }
  Move-Item -LiteralPath $resolved -Destination (Join-Path $backup 'FailedNewInstall')
 }
 foreach ($name in $known) {
  $saved=Join-Path $backup $name
  if (Test-Path -LiteralPath $saved) { Move-Item -LiteralPath $saved -Destination (Join-Path $paks '~mods') }
 }
 throw
}
Write-Host "Caretaker Energy installiert. Vorherige Pakete gesichert: $backup"
