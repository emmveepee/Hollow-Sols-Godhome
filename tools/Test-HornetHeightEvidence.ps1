param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('HornetProtector','HornetSentinel')){throw 'Not a native Hornet capture.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
if($hk -notmatch 'HK_NATIVE_AIM_QUERY[^\r\n]*state=ADash_Antic[^\r\n]*hero=True offsetX=0 offsetY=-0.5' -or
 $hk -notmatch 'HK_WEAPON_LOCAL_SHAPE owner=Hit_ADash[^\r\n]*vertices=3'){throw 'Actual native target/owned polygon contract is missing.'}
$samples=@([regex]::Matches($hk,'HK_HORNET_AIR_TARGET_EVIDENCE entity=-?\d+ collider=(-?\d+) sample=\d+ frames=\d+ minNativeDistance=([\d.E+-]+) nativeOverlapFrames=(\d+) groundedOverlapFrames=(\d+) aimedOffsetY=-0.5 frame=\d+'))
$overlaps=@($samples|Where-Object {[double]$_.Groups[2].Value -lt 0 -and [int]$_.Groups[3].Value -ge 3 -and [int]$_.Groups[4].Value -ge 3})
if($overlaps.Count -lt 3){throw 'Need repeated real multi-frame overlaps with grounded Yi.'}
$receipts=0
foreach($collider in @($overlaps|ForEach-Object {$_.Groups[1].Value}|Sort-Object -Unique)){
 $receipts+=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=[\w-]+ collider=$collider activation=\d+ action=\d+ kind=damage[^\r\n]*grounded=True")).Count
}
if($receipts -lt 3){throw 'Grounded native overlaps lack actual Yi injury receipts.'}
Write-Output "PASS (scoped): $boss grounded multi-frame air-dash overlaps=$($overlaps.Count), actual grounded injury receipts=$receipts with unchanged native aim. Does not certify untested positions/tiers."
