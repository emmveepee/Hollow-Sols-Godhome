# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $workspace ('.local/package-checks/' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ((Get-Item -LiteralPath $Archive).Length -gt 32MB) { throw 'Candidate exceeds the mod-only package budget.' }
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
$expected = @('LICENSE','THIRD-PARTY-NOTICES.md','INSTALLATION.md','Verify-Install.ps1','SOURCE.zip','BUILD-INFO.json','SHA256SUMS.txt')
foreach ($game in @('HollowKnight','NineSols')) {
    $expected += @("$game/BepInEx/core/YiGodhome.Bootstrap.dll","$game/BepInEx/core/HollowSols.Foundation.dll","$game/BepInEx/core/HollowSols.menu",
        "$game/BepInEx/patchers/YiGodhome.SaveGuard.dll","$game/BepInEx/plugins/YiGodhome.$game.dll","$game/doorstop_config.ini","$game/BepInEx/config/BepInEx.cfg")
}
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    if ($names.Count -ne $expected.Count -or @($names | Select-Object -Unique).Count -ne $expected.Count -or
        @(Compare-Object $names $expected).Count) { throw 'Candidate ZIP has unexpected, duplicate or missing entries.' }
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $Archive).Path,(Join-Path $fixture 'extracted'))
$extracted = Join-Path $fixture 'extracted'
$sums = @(Get-Content -LiteralPath (Join-Path $extracted 'SHA256SUMS.txt'))
$seen = @()
foreach ($line in $sums) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'Malformed checksum line.' }
    $hash = $Matches[1]; $relative = $Matches[2]
    if ($relative -notin $expected -or $relative -eq 'SHA256SUMS.txt' -or $relative -in $seen) { throw 'Unexpected checksum path.' }
    $seen += $relative
    if ((Get-FileHash -LiteralPath (Join-Path $extracted $relative)).Hash -ne $hash) { throw "Checksum mismatch: $relative" }
}
if ($seen.Count -ne $expected.Count-1) { throw 'Incomplete checksum coverage.' }
$info = Get-Content -LiteralPath (Join-Path $extracted 'BUILD-INFO.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $extracted 'SOURCE.zip')).Hash -ne $info.sourceSha256 -or
    $info.repository -ne 'https://github.com/emmveepee/Hollow-Sols-Godhome' -or !$info.candidate) { throw 'Incorrect source/candidate identity.' }
$sourceZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $extracted 'SOURCE.zip'))
try {
    foreach ($entry in $sourceZip.Entries) {
        if ($entry.FullName -match '(^|/)(\.git|\.local|bin|obj|archive)/|HISTORY|Edit conflict|\.(dll|exe|pdb|assets|sav|dat|log)$') { throw 'Excluded material in source archive.' }
    }
    foreach ($required in @('LICENSE','README.md','Directory.Build.props','.github/ISSUE_TEMPLATE/bug-report.yml','tools/New-PublicPackage.ps1')) {
        if (!$sourceZip.GetEntry($required)) { throw "Matching source is missing $required" }
    }
} finally { $sourceZip.Dispose() }

# Verifier tests use synthetic game/loader bytes only; no real installation/save access.
$sandbox = Join-Path $fixture 'verifier'
$null = New-Item -ItemType Directory -Path $sandbox -Force
Copy-Item -LiteralPath (Join-Path $extracted 'Verify-Install.ps1') -Destination (Join-Path $sandbox 'Verify-Install.ps1')
$manifest = $info | ConvertTo-Json -Depth 9 | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($game in @('HollowKnight','NineSols')) {
    $gameRoot = Join-Path $sandbox $game
    $null = New-Item -ItemType Directory -Path $gameRoot -Force
    Copy-Item -Path (Join-Path $extracted "$game/*") -Destination $gameRoot -Recurse
    $gameInfo = $manifest.games.PSObject.Properties[$game].Value
    foreach ($entry in @($gameInfo) + @($manifest.externalFiles)) {
        $relative = if ($entry -eq $gameInfo) { $gameInfo.nativeAssembly } else { $entry.relativePath }
        $path = Join-Path $gameRoot $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
        [IO.File]::WriteAllText($path,"synthetic prerequisite $relative",$utf8)
        if ($entry -eq $gameInfo) { $gameInfo.nativeSha256 = (Get-FileHash -LiteralPath $path).Hash }
        else { $entry.sha256 = (Get-FileHash -LiteralPath $path).Hash }
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $gameRoot 'untouched-saves') -Force
    [IO.File]::WriteAllText((Join-Path $gameRoot 'untouched-saves/user4.dat'),'synthetic untouched save',$utf8)
}
# Both fake loader sets use identical synthetic bytes, so their shared expected hashes agree.
$manifestPath = Join-Path $sandbox 'BUILD-INFO.json'
[IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 9),$utf8)
function Snapshot {
    return (@(Get-ChildItem -LiteralPath $sandbox -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n")
}
function Verify([string]$Game,[bool]$ShouldPass) {
    $before = Snapshot
    $failed = $false
    try { & (Join-Path $sandbox 'Verify-Install.ps1') -Game $Game -GameRoot (Join-Path $sandbox $Game) | Out-Null }
    catch { $failed = $true }
    if ($failed -eq $ShouldPass) { throw "Verifier result unexpected: $Game expectedPass=$ShouldPass" }
    if ((Snapshot) -ne $before) { throw 'Read-only verifier modified fixture data.' }
}
Verify 'HollowKnight' $true
Verify 'NineSols' $true
$changed = Join-Path $sandbox 'HollowKnight/BepInEx/plugins/YiGodhome.HollowKnight.dll'
$original = [IO.File]::ReadAllBytes($changed)
[IO.File]::WriteAllText($changed,'tampered project binary',$utf8)
Verify 'HollowKnight' $false
[IO.File]::WriteAllBytes($changed,$original)
$manifest.games.HollowKnight.nativeSha256 = ('0'*64)
[IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 9),$utf8)
Verify 'HollowKnight' $false
$manifest.games.HollowKnight.nativeSha256 = (Get-FileHash -LiteralPath (Join-Path $sandbox 'HollowKnight/hollow_knight_Data/Managed/Assembly-CSharp.dll')).Hash
$manifest.games.HollowKnight.files[0].relativePath = '../outside-root'
[IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 9),$utf8)
Verify 'HollowKnight' $false
Write-Output 'PASS: exact mod-only ZIP allowlist, checksums, matching source, synthetic install verification, tampering/native-hash/path-traversal rejection and read-only preservation.'
Write-Output "Synthetic fixture retained: $fixture"
