param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'NS_MEMORY_FAILED|HIT_FAILED|NS_HOST_HIT_UNCERTAIN') { throw 'Native traversal run has a terminal combat failure.' }
if($ns -notmatch 'NS_TALISMAN_GROUNDED_FIXTURE .*grounded=True .*midDashPlacement=False') { throw 'No actual grounded setup.' }
$corrections=[regex]::Matches($ns,'NS_TALISMAN_TERRAIN frame=(\d+) nativeCliff=True corrected=True .*nativeRay=none surface=YiGodhome Terrain \d+ .*normalY=1 resultCliff=False')
if($corrections.Count -lt 2) { throw 'No repeated false-cliff correction on actual imported floor.' }
$moves=[regex]::Matches($ns,'NS_TALISMAN_NATIVE_MOVE frame=(\d+) grounded=True nativeOffsetX=([^ ]+) position=\(([-\d.]+), ([-\d.]+), [^\)]+\) forcedMovement=False')
$proved=$false
for($i=0;$i -lt $moves.Count;$i++) {
    for($j=$i+1;$j -lt $moves.Count;$j++) {
        $a=$moves[$i];$b=$moves[$j]
        if([int]$b.Groups[1].Value-[int]$a.Groups[1].Value -gt 60){break}
        $x1=[double]::Parse($a.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
        $x2=[double]::Parse($b.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
        if([Math]::Abs($x2-$x1) -ge 20 -and $a.Groups[4].Value -eq $b.Groups[4].Value){$proved=$true;break}
    }
    if($proved){break}
}
if(-not $proved){throw 'No grounded native dash advancing at least one host unit after correction.'}
if($hk -notmatch 'HK_SUMMON_HIT .*kind=Talisman before=\d+ after=\d+ .*encounter=' -or $ns -notmatch 'NS_HIT_OUTCOME .*kind=Talisman') {throw 'No native talisman damage settlement.'}
if($hk -notmatch 'HK_HUB_COMBAT_ENDED victory=True' -or $hk -notmatch 'HK_HUB_COMBAT_RETURN_READY .*presented=True .*nativeWakeComplete=True'){throw 'No completed challenge and visible native return.'}
Write-Output "PASS: $($corrections.Count) imported-floor corrections, grounded native movement, talisman settlement and one victory/visible return. No full repeated-clear or human post-dive claim."
