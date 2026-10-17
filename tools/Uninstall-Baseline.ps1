[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('HollowKnight','NineSols')][string]$Game)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
$manifestPath = Join-Path $workspace ".local/deployments/$Game.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedRoot = if ($Game -eq 'HollowKnight') { 'C:/Steam/steamapps/common/Hollow Knight' } else { 'C:/Steam/steamapps/common/Nine Sols' }
if ([IO.Path]::GetFullPath($manifest.root) -ne [IO.Path]::GetFullPath($expectedRoot)) { throw 'Unexpected manifest root.' }
$targets = foreach ($file in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $manifest.root $file.relative))
    if (-not $target.StartsWith([IO.Path]::GetFullPath($expectedRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Out-of-root target.' }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    if ($file.relative -eq 'BepInEx\config\BepInEx.cfg') { continue } # Retain generated config/logs/cache.
    if ((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256) { throw "Refuse removal of changed file: $target" }
    $target
}
foreach ($target in $targets) { Remove-Item -LiteralPath $target }
Move-Item -LiteralPath $manifestPath -Destination ($manifestPath + '.uninstalled-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
Write-Output "Removed owned executable components from $Game. Inert BepInEx config/log/cache directories retained. Saves and original game files unchanged."
