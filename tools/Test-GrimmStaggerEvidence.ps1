param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native runtime failure invalidates stagger evidence.'}
$states=@([regex]::Matches($hk,'HK_GRIMM_BAT_STATE encounter=([\w-]+) entity=(-?\d+) activation=(\d+) state=(\w+) receiver=(-?\d+) rawHealth=(-?\d+) bossHealth=(\d+) presented=(\d+) collider=(\w+) visible=(\w+) exposed=(\w+) frame=(\d+)'))
$proved=$false
foreach($s in $states){
 if($s.Groups[4].Value -ne 'Exploded' -or $s.Groups[11].Value -ne 'True' -or [int]$s.Groups[6].Value -ge 0){continue}
 $enc=$s.Groups[1].Value;$entity=$s.Groups[2].Value;$activation=$s.Groups[3].Value
 $raw=[int]$s.Groups[6].Value;$boss=[int]$s.Groups[7].Value;$presented=[int]$s.Groups[8].Value;$frame=[int]$s.Groups[12].Value
 if($presented -ne [Math]::Max(0,$boss+[Math]::Max(-50,$raw))){throw 'Presented native bat debit violates cap.'}
 $hit=[regex]::Matches($hk,"HK_SUMMON_HIT entity=$entity generation=$activation kind=\w+ before=(\d+) after=$presented sequence=\d+ encounter=$enc frame=(\d+)") | Where-Object { [int]$_.Groups[1].Value -gt $presented -and [Math]::Abs([int]$_.Groups[2].Value-$frame) -le 3 }
 if(!$hit){continue}
 $later=$states | Where-Object {$_.Groups[1].Value -eq $enc -and $_.Groups[2].Value -eq $entity -and [int]$_.Groups[12].Value -gt $frame -and $_.Groups[4].Value -ne 'Exploded' -and $_.Groups[4].Value -ne 'Call_Bats' -and $_.Groups[4].Value -ne 'Reforming' -and $_.Groups[4].Value -ne 'Reformed' -and [int]$_.Groups[7].Value -le $presented}
 if($later){$proved=$true;break}
}
if(!$proved){throw 'No native bat damage acknowledgement followed by native boss HP adjustment.'}
if($ns -notmatch 'NS_TARGET_GEOMETRY_REBOUND'){throw 'No same-lease native geometry rebound.'}
Write-Output "PASS (scoped): session=$Session real stagger-bat damage, native capped presentation and native boss adjustment. Does not establish full clear, human timing, every damage type or higher tiers. Forced STUN, if logged, is diagnostic assistance."
