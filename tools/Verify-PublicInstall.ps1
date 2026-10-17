# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('HollowKnight','NineSols')][string]$Game,
    [Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $GameRoot).Path
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'GameRoot must be a directory.' }
function Resolve-SafeFile([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[/\\])\.\.([/\\]|$)' -or $Relative.Contains(':')) { throw 'Unsafe manifest path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $Relative))
    if (!$path.StartsWith($root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Out-of-root manifest path.' }
    $entry = Get-Item -LiteralPath $path
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected installation file.' }
    for ($directory = Get-Item -LiteralPath (Split-Path $path -Parent); $null -ne $directory; $directory = $directory.Parent) {
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected installation path.' }
    }
    return $path
}
$info = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BUILD-INFO.json') -Raw | ConvertFrom-Json
if ($info.schema -ne 1 -or $info.version -ne '0.1.0' -or $info.profile -ne 'public') { throw 'Unsupported candidate manifest.' }
$gameInfo = $info.games.PSObject.Properties[$Game].Value
if (!$gameInfo) { throw 'Game missing from candidate manifest.' }
$native = Resolve-SafeFile $gameInfo.nativeAssembly
if ((Get-FileHash -LiteralPath $native).Hash -ne $gameInfo.nativeSha256) { throw 'Unsupported game build. Do not launch the mod or remove its save guard.' }
foreach ($file in @($gameInfo.files) + @($info.externalFiles)) {
    $path = Resolve-SafeFile $file.relativePath
    if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "File differs from candidate/prerequisite: $($file.relativePath). Review rather than overwrite it." }
}
if ([IO.File]::ReadAllText((Join-Path $root 'BepInEx/core/HollowSols.menu')) -ne "HollowSols native menu schema 1`n") { throw 'Native-menu marker is invalid.' }
Write-Output "PASS read-only installation hashes: $Game v$($info.version) source=$($info.sourceRevision)"
Write-Output 'This does not launch either game, access saves, or certify gameplay/recovery.'
