# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param([string]$BepCore, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (!$BepCore) { $BepCore = Join-Path $workspace '.local/deps/BepInEx-5.4.23.5/BepInEx/core' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $workspace '.local/packages' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$localRoot = [IO.Path]::GetFullPath((Join-Path $workspace '.local')) + [IO.Path]::DirectorySeparatorChar
if (!$outputRoot.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be inside this checkout .local directory.' }
$existingParent = $outputRoot
while (!(Test-Path -LiteralPath $existingParent)) { $existingParent = Split-Path $existingParent -Parent }
for ($directory = Get-Item -LiteralPath $existingParent; $null -ne $directory; $directory = $directory.Parent) {
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected package output path.' }
}
$sourceRevision = (& git -C $workspace rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Source checkout is required.' }
$sourceTree = (& git -C $workspace rev-parse 'HEAD^{tree}')
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source tree.' }
$sourceStatus = @(& git -C $workspace status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0 -or $sourceStatus.Count) { throw 'Packaging requires a clean reviewed source checkout, including its index.' }

function Assert-Artifact([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing artifact: $Path" }
    if ([Reflection.AssemblyName]::GetAssemblyName($Path).Version -ne [Version]'0.1.0.0') { throw "Wrong artifact version: $Path" }
    $binaryText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($Path))
    if (!$binaryText.Contains("0.1.0+$sourceRevision")) { throw "Artifact source revision mismatch: $Path" }
    if ($binaryText.IndexOf($workspace, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $binaryText.IndexOf($workspace.Replace('\','/'), [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $binaryText -match '(?i)[a-z]:[/\\]Users[/\\][^/\\\x00]+') { throw "Artifact contains a personal/compiler path: $Path" }
}
$inputs = @{}
foreach ($game in @('HollowKnight','NineSols')) {
    $receipt = Get-Content -LiteralPath (Join-Path $workspace ".local/build-$game-public.json") -Raw | ConvertFrom-Json
    $plugin = Join-Path $workspace ".local/public-build/$game/YiGodhome.$game.dll"
    if (!$receipt.success -or $receipt.flavor -ne 'public' -or !$receipt.sourceClean -or
        $receipt.sourceRevision -ne $sourceRevision -or $receipt.sourceTree -ne $sourceTree -or
        $receipt.sha256 -ne (Get-FileHash -LiteralPath $plugin).Hash) { throw "Stale/dirty/incorrect public build: $game" }
    $inputs[$game] = @{
        'BepInEx/core/YiGodhome.Bootstrap.dll' = Join-Path $workspace 'src/Bootstrap/bin/Release/netstandard2.1/YiGodhome.Bootstrap.dll'
        'BepInEx/core/HollowSols.Foundation.dll' = Join-Path $workspace ".local/public-build/$game/HollowSols.Foundation.dll"
        'BepInEx/patchers/YiGodhome.SaveGuard.dll' = Join-Path $workspace 'src/SaveGuard/bin/Release/netstandard2.1/YiGodhome.SaveGuard.dll'
        "BepInEx/plugins/YiGodhome.$game.dll" = $plugin
    }
    foreach ($artifact in $inputs[$game].Values) { Assert-Artifact $artifact }
}
$foundationHash = (Get-FileHash -LiteralPath $inputs.HollowKnight['BepInEx/core/HollowSols.Foundation.dll']).Hash
if ((Get-FileHash -LiteralPath $inputs.NineSols['BepInEx/core/HollowSols.Foundation.dll']).Hash -ne $foundationHash) { throw 'Role Foundation builds differ.' }
# The external loader is fingerprinted, never copied into the package.
$external = @()
$loaderNames = @('0Harmony.dll','0Harmony20.dll','BepInEx.dll','BepInEx.Harmony.dll','BepInEx.Preloader.dll','HarmonyXInterop.dll',
    'Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.RuntimeDetour.dll','MonoMod.Utils.dll')
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $BepCore 'BepInEx.dll')).Version -ne [Version]'5.4.23.5') { throw 'Wrong external loader version.' }
foreach ($dllName in $loaderNames) {
    $dll = Get-Item -LiteralPath (Join-Path $BepCore $dllName)
    $external += [ordered]@{ relativePath="BepInEx/core/$($dll.Name)"; sha256=(Get-FileHash -LiteralPath $dll.FullName).Hash }
}
if ($external.Count -lt 2 -or !($external.relativePath -contains 'BepInEx/core/BepInEx.dll') -or
    !($external.relativePath -contains 'BepInEx/core/BepInEx.Preloader.dll')) { throw 'Audited external loader is missing.' }
$loaderRoot = Split-Path (Split-Path $BepCore -Parent) -Parent
$external += [ordered]@{ relativePath='winhttp.dll'; sha256=(Get-FileHash -LiteralPath (Join-Path $loaderRoot 'winhttp.dll')).Hash }
$name = "HollowSols-v0.1.0-candidate-$($sourceRevision.Substring(0,8))-windows-x64"
$stage = Join-Path $outputRoot ($name + '-contents')
$archive = Join-Path $outputRoot ($name + '.zip')
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive)) { throw 'Candidate already exists; preserve it and use a new output directory.' }
$null = New-Item -ItemType Directory -Path $stage -Force
$utf8 = [Text.UTF8Encoding]::new($false)
$games = [ordered]@{}
foreach ($game in @('HollowKnight','NineSols')) {
    $payload = Join-Path $stage $game
    foreach ($relative in $inputs[$game].Keys) {
        $target = Join-Path $payload $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
        Copy-Item -LiteralPath $inputs[$game][$relative] -Destination $target
    }
    [IO.File]::WriteAllText((Join-Path $payload 'BepInEx/core/HollowSols.menu'), "HollowSols native menu schema 1`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $payload 'doorstop_config.ini'), @"
[General]
enabled = true
target_assembly=BepInEx\core\YiGodhome.Bootstrap.dll
redirect_output_log = false
ignore_disable_switch = true
[UnityMono]
debug_enabled = false
debug_suspend = false
"@.Replace("`r`n","`n") + "`n", $utf8)
    $null = New-Item -ItemType Directory -Path (Join-Path $payload 'BepInEx/config') -Force
    $loaderConfig = @"
[Logging.Console]
Enabled = false
[Logging.Disk]
Enabled = true
LogLevels = Fatal, Error, Warning, Message, Info
[Preloader]
DumpAssemblies = false
LoadDumpedAssemblies = false
BreakBeforeLoadAssemblies = false
"@.Replace("`r`n","`n") + "`n"
    if ($game -eq 'NineSols') { $loaderConfig += "`n[Chainloader]`nHideManagerGameObject = true`n" }
    [IO.File]::WriteAllText((Join-Path $payload 'BepInEx/config/BepInEx.cfg'), $loaderConfig, $utf8)
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ relativePath=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $games[$game] = [ordered]@{
        nativeAssembly = $(if ($game -eq 'HollowKnight') { 'hollow_knight_Data/Managed/Assembly-CSharp.dll' } else { 'NineSols_Data/Managed/Assembly-CSharp.dll' })
        nativeSha256 = $(if ($game -eq 'HollowKnight') { 'E9048EF6A633970F735E01EC166D3959F610EAEA7A88D827D48D67B1E5FB87BD' } else { '51F2921C61E237D7FC6A38F31DE2C8BEA8DD5826CF381D2FB8033B3C66AFD743' })
        files = $files
    }
}
foreach ($entry in @(@('LICENSE','LICENSE'),@('THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.md'),@('docs/INSTALLATION.md','INSTALLATION.md'),@('tools/Verify-PublicInstall.ps1','Verify-Install.ps1'))) {
    Copy-Item -LiteralPath (Join-Path $workspace $entry[0]) -Destination (Join-Path $stage $entry[1])
}
$sourceArchive = Join-Path $stage 'SOURCE.zip'
& git -C $workspace -c core.autocrlf=false archive --format=zip "--output=$sourceArchive" HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
$info = [ordered]@{ schema=1; version='0.1.0'; candidate=$true; profile='public'; sourceRevision=$sourceRevision; sourceTree=$sourceTree;
    repository='https://github.com/emmveepee/Hollow-Sols-Godhome'; sourceArchive='SOURCE.zip'; sourceSha256=(Get-FileHash -LiteralPath $sourceArchive).Hash;
    externalLoader='BepInEx 5.4.23.5 Windows x64 (installed separately)'; externalFiles=$external; games=$games }
[IO.File]::WriteAllText((Join-Path $stage 'BUILD-INFO.json'), ($info | ConvertTo-Json -Depth 9) + "`n", $utf8)
$checksums = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')
})
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $checksums, $utf8)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
Write-Output "CANDIDATE_ONLY $archive"
Write-Output "SHA256 $((Get-FileHash -LiteralPath $archive).Hash)"
Write-Output 'No loader/game binaries, deployment, game launch, GitHub upload, tag or release publication.'
