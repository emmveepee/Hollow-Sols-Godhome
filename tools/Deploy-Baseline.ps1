[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('HollowKnight','NineSols')][string]$Game)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
$gameRoot = if ($Game -eq 'HollowKnight') { 'C:/Steam/steamapps/common/Hollow Knight' } else { 'C:/Steam/steamapps/common/Nine Sols' }
$processName = if ($Game -eq 'HollowKnight') { 'hollow_knight' } else { 'NineSols' }
$expected = if ($Game -eq 'HollowKnight') { 'E9048EF6A633970F735E01EC166D3959F610EAEA7A88D827D48D67B1E5FB87BD' } else { '51F2921C61E237D7FC6A38F31DE2C8BEA8DD5826CF381D2FB8033B3C66AFD743' }
$assemblyPath = Join-Path $gameRoot ($processName + '_Data/Managed/Assembly-CSharp.dll')
if ((Get-FileHash -LiteralPath $assemblyPath).Hash -ne $expected) { throw 'Game version changed.' }
$manifestPath = Join-Path $workspace ".local/deployments/$Game.json"
if (Test-Path -LiteralPath $manifestPath) { throw 'Deployment already exists; uninstall it first.' }
foreach ($existing in @('winhttp.dll','doorstop_config.ini','BepInEx')) {
    if (Test-Path -LiteralPath (Join-Path $gameRoot $existing)) { throw "Existing loader component: $existing; refusing overwrite." }
}
for ($ancestor = Get-Item -LiteralPath $gameRoot; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Redirected install path: $($ancestor.FullName)" }
}
$deps = Join-Path $workspace '.local/deps/BepInEx-5.4.23.5'
$stage = Join-Path $workspace ".local/staging/$Game"
$null = New-Item -ItemType Directory -Force -Path "$stage/BepInEx/core","$stage/BepInEx/patchers","$stage/BepInEx/plugins","$stage/BepInEx/config"
Copy-Item -LiteralPath "$deps/winhttp.dll" -Destination "$stage/winhttp.dll"
Get-ChildItem -LiteralPath "$deps/BepInEx/core" -Filter '*.dll' -File | Copy-Item -Destination "$stage/BepInEx/core"
Copy-Item -LiteralPath "$workspace/src/Bootstrap/bin/Release/netstandard2.1/YiGodhome.Bootstrap.dll" -Destination "$stage/BepInEx/core"
Copy-Item -LiteralPath "$workspace/src/SaveGuard/bin/Release/netstandard2.1/YiGodhome.SaveGuard.dll" -Destination "$stage/BepInEx/patchers"
Copy-Item -LiteralPath "$workspace/src/$Game/bin/Release/netstandard2.1/YiGodhome.$Game.dll" -Destination "$stage/BepInEx/plugins"
@'
[General]
enabled = true
target_assembly=BepInEx\core\YiGodhome.Bootstrap.dll
redirect_output_log = false
ignore_disable_switch = true
[UnityMono]
debug_enabled = false
debug_suspend = false
'@ | Set-Content -LiteralPath "$stage/doorstop_config.ini" -Encoding ascii
@'
[Logging.Console]
Enabled = false
[Logging.Disk]
Enabled = true
LogLevels = All
[Preloader]
DumpAssemblies = false
LoadDumpedAssemblies = false
BreakBeforeLoadAssemblies = false
'@ | Set-Content -LiteralPath "$stage/BepInEx/config/BepInEx.cfg" -Encoding ascii
if ($Game -eq 'NineSols') { Add-Content -LiteralPath "$stage/BepInEx/config/BepInEx.cfg" -Value "`n[Chainloader]`nHideManagerGameObject = true" -Encoding ascii }
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    [pscustomobject]@{ relative=[IO.Path]::GetRelativePath($stage,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest = [ordered]@{ game=$Game; root=$gameRoot; executable="$processName.exe"; assemblyPath=$assemblyPath; assemblyHash=$expected; mode='no-save-io'; createdUtc=[DateTime]::UtcNow.ToString('O'); files=$files }
$null = New-Item -ItemType Directory -Path (Split-Path $manifestPath -Parent) -Force
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
foreach ($file in $files) {
    $target = Join-Path $gameRoot $file.relative
    $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
    Copy-Item -LiteralPath (Join-Path $stage $file.relative) -Destination $target
    if ((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256) { throw "Deployment hash mismatch: $target" }
}
Write-Output "Deployed $Game protected baseline. No game assembly or save was modified. Manifest: $manifestPath"
