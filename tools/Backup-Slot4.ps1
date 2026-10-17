[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games before backing up slot 4.' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$destination = Join-Path $root ".local/backups/$stamp"
$hkRoot = Join-Path $env:USERPROFILE 'AppData/LocalLow/Team Cherry/Hollow Knight'
$nsSlot = Join-Path $env:USERPROFILE 'AppData/LocalLow/RedCandleGames/NineSols/saveslot3'
$hkDll = 'C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed/Assembly-CSharp.dll'
$nsDll = 'C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed/Assembly-CSharp.dll'
$expected = @{
    $hkDll = 'E9048EF6A633970F735E01EC166D3959F610EAEA7A88D827D48D67B1E5FB87BD'
    $nsDll = '51F2921C61E237D7FC6A38F31DE2C8BEA8DD5826CF381D2FB8033B3C66AFD743'
}
foreach ($p in $expected.Keys) {
    if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $expected[$p]) { throw "Game changed; reprove slot mapping first: $p" }
}
# Enumerate only the known slot-4 filename family, never other save files.
$hkFiles = @(Get-ChildItem -LiteralPath $hkRoot -Filter 'user4.dat*' -File | Where-Object { $_.Name -match '^user4\.dat(?:\.bak\d*|\.new|\.del)?$' })
$versioned = Join-Path $hkRoot 'user4_1.5.12620.dat'
if (Test-Path -LiteralPath $versioned) { $hkFiles += Get-Item -LiteralPath $versioned }
$hkAbsent = -not (Test-Path -LiteralPath (Join-Path $hkRoot 'user4.dat'))
if (-not $hkAbsent -and -not ($hkFiles | Where-Object Name -eq 'user4.dat')) { throw 'Slot-4 listing disagrees with exact-path existence; backup cannot proceed.' }
$null = New-Item -ItemType Directory -Path (Join-Path $destination 'HollowKnight-slot4') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $destination 'NineSols-slot4') -Force
$records = [Collections.Generic.List[object]]::new()
function Copy-Verified([IO.FileInfo]$file, [string]$target) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refuse reparse point: $($file.FullName)" }
    $before = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $after = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $current = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($before -ne $after -or $before -ne $current) { throw "Backup mismatch: $($file.FullName)" }
    $records.Add([pscustomobject]@{ source=$file.FullName; backup=$target; sha256=$before; bytes=$file.Length })
}
foreach ($f in $hkFiles) { Copy-Verified $f (Join-Path $destination "HollowKnight-slot4/$($f.Name)") }
$nsEmpty = -not (Test-Path -LiteralPath $nsSlot)
if (-not $nsEmpty) {
    $nsDirectory = Get-Item -LiteralPath $nsSlot
    if ($nsDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refuse redirected Nine Sols slot.' }
    $entries = @(Get-ChildItem -LiteralPath $nsSlot -Recurse -Force)
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Refuse link inside slot 4.' }
    foreach ($f in $entries | Where-Object { -not $_.PSIsContainer }) {
        $relative = [IO.Path]::GetRelativePath($nsSlot, $f.FullName)
        Copy-Verified $f (Join-Path $destination "NineSols-slot4/$relative")
    }
}
$manifest = [ordered]@{ utc=$stamp; slot=4; hkProfileId=4; nsSlotIndex=3; hollowKnightPrimaryWasAbsent=$hkAbsent; nineSolsSlotWasAbsent=$nsEmpty; files=@($records.ToArray()); assemblyHashes=$expected }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
$note = "Slot-4-only backup: $destination`nNine Sols slot 4 absent: $nsEmpty`nVerified files: $($records.Count)`nNo other save slot inspected or copied."
Add-Content -LiteralPath (Join-Path $root '.local/DEVELOPMENT-NOTES.txt') -Value $note
Write-Output $note
