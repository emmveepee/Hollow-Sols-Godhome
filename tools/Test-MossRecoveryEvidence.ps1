param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL)_FAILED'){throw 'Moss recovery integration failure.'}
if($ns -match 'NS_INVULNERABLE_VICTORY_BEGIN'){throw 'Invulnerable fixture cannot certify harmless burrow.'}
$recoveries=[regex]::Matches($hk,'HK_MOSS_COUNTER_BURROW_IMMUNITY entity=(-?\d+) action=(\d+) active=True nativeState=Submerge_1 frame=(\d+)')
if($recoveries.Count -lt 2){throw 'Need repeated native counter-triggered burrows.'}
foreach($recovery in $recoveries) {
 $entity=$recovery.Groups[1].Value;$action=$recovery.Groups[2].Value;$start=[long]$recovery.Groups[3].Value
 $end=[regex]::Match($hk,"HK_MOSS_COUNTER_BURROW_IMMUNITY entity=$entity action=$action active=False nativeState=(?!Submerge)\w+ frame=(\d+)")
 if(-not $end.Success -or [long]$end.Groups[1].Value -le $start){throw 'Harmless lease did not cover the complete native submerge sequence.'}
 if($hk -notmatch "HK_MOSS_BURROW_DAMAGE_SUPPRESSED entity=$entity action=$action source=Burrow_Hit id=-?\d+ nativeState=Submerge_\d+ nativeActive=True translatedActive=False"){throw 'Actual native burrow collider suppression is unproven.'}
}
Write-Output "PASS (scoped): Moss native counter burrows=$($recoveries.Count), actual Burrow Hit active but translated inactive, lease spans all native Submerge states, no invulnerable fixture."
