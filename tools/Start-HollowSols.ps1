[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$steam = 'C:/Steam/steam.exe'
if (-not (Test-Path -LiteralPath $steam)) { throw 'Steam was not found at the audited installation path.' }
foreach ($game in @('HollowKnight', 'NineSols')) {
    $manifestPath = Join-Path $workspace ".local/deployments/$game.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $manifest.assemblyPath).Hash -ne $manifest.assemblyHash) { throw "$game has changed; audit its save APIs before updating the loader." }
    $marker = Join-Path $manifest.root 'BepInEx/core/HollowSols.menu'
    if (-not (Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker) -ne "HollowSols native menu schema 1`n") { throw "$game native menu components have not been installed." }
    if (Test-Path -LiteralPath (Join-Path $manifest.root 'BepInEx/core/YiGodhome.session')) { throw "Finish the protected $game diagnostic launch before using the normal bridge menus." }
    foreach ($file in $manifest.files) {
        if ($file.relative -eq 'BepInEx\config\BepInEx.cfg') { continue }
        if ((Get-FileHash -LiteralPath (Join-Path $manifest.root $file.relative)).Hash -ne $file.sha256) { throw "Owned loader file changed: $game/$($file.relative)" }
    }
}
foreach ($game in @('HollowKnight', 'NineSols')) {
    $name = if ($game -eq 'HollowKnight') { 'hollow_knight' } else { 'NineSols' }
    if (Get-Process $name -ErrorAction SilentlyContinue) { continue }
    $launch = New-Object Diagnostics.ProcessStartInfo
    $launch.FileName = $steam
    $launch.Arguments = if ($game -eq 'HollowKnight') { '-applaunch 367520' } else { '-applaunch 1809540' }
    $launch.UseShellExecute = $false
    $launch.CreateNoWindow = $true
    $null = [Diagnostics.Process]::Start($launch)
}
Write-Output 'Both games launch normally through Steam. At either main menu, choose Hollow Sols Bridge once.'
