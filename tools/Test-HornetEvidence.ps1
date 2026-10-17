param([Parameter(Mandatory)][Guid]$Session,[Parameter(Mandatory)][ValidateSet('HornetProtector','HornetSentinel')][string]$Boss,[switch]$RequireKick,[switch]$RequireCounter,[switch]$RequireBarb)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne $Boss){throw 'Wrong Hornet profile.'}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native Hornet integration failed.'}
if($hk -notmatch 'HK_HORNET_ATTACK[^\r\n]*state=G_Dash[^\r\n]*kick=True' -or $hk -notmatch 'HK_HORNET_ATTACK[^\r\n]*state=Sphere(?:_A)? action=\d+ kick=False crimson=True'){throw 'Missing actual dash/circle windows.'}
$legs=@([regex]::Matches($hk,'HK_HORNET_NEEDLE_LEG entity=(-?\d+) returning=True action=(\d+) frame=(\d+)'))
if(!$legs.Count){throw 'No authored needle return reactivation.'}
foreach($leg in $legs){
 $action=$leg.Groups[2].Value
 if($hk -notmatch "HK_POOLED_ATTACK[^\r\n]*active=True category=Melee name=Needle action=$action frame="){throw 'Return leg did not acquire a damaging collider activation.'}
}
if($RequireKick){
 $hits=@([regex]::Matches($ns,'NS_TAICHI_NATIVE attack=(\d+) weakness=True bounce=True frame=\d+'))
 if($hits.Count -lt 2){throw 'Need two actual native kicks.'}
 foreach($h in $hits){$a=$h.Groups[1].Value;if($hk -notmatch "HK_HORNET_KICK[^\r\n]*action=$a nativeRecovery=True" -or $ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$a sameNativeFrame=\d+ damaging=False"){throw 'Missing matched native recovery/immediate retirement.'}}
}
if($RequireCounter -and ($hk -notmatch 'HK_HORNET_CIRCLE_COUNTER[^\r\n]*seconds=1' -or $hk -notmatch 'HK_HORNET_STAGGER_END[^\r\n]*deadline=True')){throw 'No timed native circle counter recovery.'}
if($RequireBarb -and $hk -notmatch 'HK_HORNET_BARB_MELEE[^\r\n]*state=Hit'){throw 'No actual native melee barb deflection.'}
Write-Output "PASS (scoped): $Boss session=$Session native dash/circle windows and $($legs.Count) returning needle activations; kick=$RequireKick counter=$RequireCounter barb=$RequireBarb. Unrequested successful defenses, trap breaks, visual quality and normal timing remain unverified."
