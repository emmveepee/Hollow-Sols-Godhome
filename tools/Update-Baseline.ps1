[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('HollowKnight','NineSols')][string]$Game, [switch]$NativeMenus)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
$built = Get-Content -LiteralPath (Join-Path $workspace ".local/build-$Game.json") -Raw | ConvertFrom-Json
$plugin = Join-Path $workspace "src/$Game/bin/Release/netstandard2.1/YiGodhome.$Game.dll"
if (-not $built.success -or $built.sha256 -ne (Get-FileHash -LiteralPath $plugin).Hash) { throw "Latest $Game build did not succeed or artifact changed. Run tools/Build.ps1 first." }
$path = Join-Path $workspace ".local/deployments/$Game.json"
$manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $manifest.assemblyPath).Hash -ne $manifest.assemblyHash) { throw 'Installed native assembly changed; re-audit before deployment.' }
$updates = @{
    'BepInEx\core\YiGodhome.Bootstrap.dll' = 'src/Bootstrap/bin/Release/netstandard2.1/YiGodhome.Bootstrap.dll'
    'BepInEx\patchers\YiGodhome.SaveGuard.dll' = 'src/SaveGuard/bin/Release/netstandard2.1/YiGodhome.SaveGuard.dll'
    "BepInEx\plugins\YiGodhome.$Game.dll" = "src/$Game/bin/Release/netstandard2.1/YiGodhome.$Game.dll"
}
if ($NativeMenus) {
    $updates['BepInEx\core\HollowSols.Foundation.dll'] = 'src/Foundation/bin/Release/netstandard2.1/HollowSols.Foundation.dll'
    $marker = Join-Path $workspace '.local/native-menu-install.marker'
    [IO.File]::WriteAllText($marker,"HollowSols native menu schema 1`n")
    $updates['BepInEx\core\HollowSols.menu'] = '.local/native-menu-install.marker'
}
foreach ($relative in $updates.Keys) {
    $entry = @($manifest.files | Where-Object relative -eq $relative)
    if ($entry.Count -eq 0 -and $relative -in @('BepInEx\core\HollowSols.Foundation.dll','BepInEx\core\HollowSols.menu')) {
        if(Test-Path -LiteralPath (Join-Path $manifest.root $relative)){throw "Unowned file exists: $relative"}
        $manifest.files += [pscustomobject]@{relative=$relative;sha256=$null};continue
    }
    if ($entry.Count -ne 1) { throw "Manifest entry missing: $relative" }
    if (-not (Test-Path -LiteralPath (Join-Path $workspace $updates[$relative]))) { throw "Build missing: $relative" }
    $installedHash=(Get-FileHash -LiteralPath (Join-Path $manifest.root $relative)).Hash
    $sourceHash=(Get-FileHash -LiteralPath (Join-Path $workspace $updates[$relative])).Hash
    # An interrupted owned-file update may finish copying before its manifest
    # is published. Resume only when the bytes equal our exact current build.
    if ($installedHash -ne $entry[0].sha256 -and $installedHash -ne $sourceHash) { throw "Installed component changed: $relative" }
}
foreach ($relative in $updates.Keys) {
    $target = Join-Path $manifest.root $relative
    Copy-Item -LiteralPath (Join-Path $workspace $updates[$relative]) -Destination $target
    ($manifest.files | Where-Object relative -eq $relative).sha256 = (Get-FileHash -LiteralPath $target).Hash
}
$pending=$path+'.pending-'+[Guid]::NewGuid().ToString('N')
[IO.File]::WriteAllText($pending,($manifest | ConvertTo-Json -Depth 5))
if ([Environment]::Version.Major -ge 5) { [IO.File]::Move($pending,$path,$true) }
else { [IO.File]::Replace($pending,$path,($path+'.previous-'+[Guid]::NewGuid().ToString('N'))) }
Write-Output "Updated owned $Game components and deployment hashes."
