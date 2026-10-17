param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'LOCAL_AURA_FAILED|NS_CONTACT_FAILED|HIT_FAILED'){throw 'Aura run has a gameplay failure.'}
if($hk -notmatch 'HK_LOCAL_AURA_STYLE .*interiorOverlap=True animatedRibbon=True nsOutlinePixels=False'){throw 'Wrong renderer.'}
$ready=[regex]::Matches($hk,'(?m)^(\S+) HK_MANTIS_AURA_READY entity=(-?\d+) action=(\d+) anticipation=(\S+) frame=(\d+)')
$checked=0;$gaps=@()
foreach($r in $ready){
 $entity=$r.Groups[2].Value;$action=$r.Groups[3].Value
 $warn=[regex]::Match($ns,"(?m)^(\S+) NS_MANTIS_WARNING encounter=(\S+) entity=$entity activation=\d+ action=$action ")
 if(-not $warn.Success){throw "No independent warning for body=$entity action=$action."}
 $enc=$warn.Groups[2].Value
 $complete=[regex]::Match($ns,"(?m)^(\S+) NS_DASH_CUE_COMPLETE action=$action encounter=$enc ")
 if(-not $complete.Success){throw "Native cue did not complete for action=$action."}
 $gap=([DateTimeOffset]::Parse($r.Groups[1].Value)-[DateTimeOffset]::Parse($complete.Groups[1].Value)).TotalMilliseconds
 if($gap -lt 20){throw "Warning completion lacks a visible beat before outline: body=$entity action=$action gap=$gap ms."}
 if([double]$r.Groups[4].Value -lt .28){throw 'Aura started before its anticipation gate.'}
 $gaps+=$gap;$checked++
}
if($checked -lt 6){throw 'Insufficient per-body sequencing evidence.'}
if($hk -notmatch 'HK_LOCAL_AURA_CAPTURE parts=[23] '){throw 'No concurrent local aura capture.'}
if($hk -notmatch 'HK_LOCAL_AURA_RETIRED owners=0 shapes=0 cameras=0 textures=0' -or $ns -notmatch 'NS_MANTIS_WARNINGS_RETIRED owners=0 anchors=0'){throw 'Visual teardown missing.'}
Write-Output "PASS (scoped): session=$Session sequencedBodies=$checked minimumCompletionToAuraMs=$(($gaps | Measure-Object -Minimum).Minimum) concurrent current-pose aura and visual teardown. Images require inspection; timestamps are not a guarantee under arbitrary IPC delay."
