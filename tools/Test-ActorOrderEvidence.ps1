[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$workspace = Split-Path $PSScriptRoot -Parent
$records = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/HollowKnight/$Session"
if ($records -match 'HK_VISUAL_FAILED|LOCAL_AURA_FAILED') { throw 'Visual runtime failure.' }
$culture = [Globalization.CultureInfo]::InvariantCulture
$native = @([regex]::Matches($records,'HK_ACTOR_ORDER_BIND entity=(\d+) renderer=(\d+) depth=([^ ]+) layer=(-?\d+) order=(-?\d+) queue=(\d+)'))
if ($native.Count -lt 3) { throw 'Missing independent native body renderer bindings.' }
# This focused Sisters oracle covers the installed default transparent band.
if (@($native | Where-Object { $_.Groups[4].Value -ne '0' -or $_.Groups[5].Value -ne '0' -or $_.Groups[6].Value -ne '3000' }).Count) { throw 'Different native sorting/queue band requires separate evidence.' }
$nearest = ($native | ForEach-Object { [float]::Parse($_.Groups[3].Value,$culture) } | Measure-Object -Minimum).Minimum
$draws = @([regex]::Matches($records,'HK_ACTOR_DRAW_ORDER depth=([^ ]+) layer=0 order=0 bodies=(\d+) aheadOfKnownBodies=True') | Where-Object { [int]$_.Groups[2].Value -ge 2 })
if (-not $draws.Count) { throw 'No concurrent body ordering recorded.' }
foreach ($draw in $draws) {
    if ([float]::Parse($draw.Groups[1].Value,$culture) -ge $nearest) { throw 'Actor is not ahead of the recorded native body depths.' }
}
Write-Output "PASS: $($native.Count) native body bindings and $($draws.Count) concurrent actor order records ahead of native depth $nearest. Renderer evidence only; overlap appearance and foreground waves need visual acceptance."
