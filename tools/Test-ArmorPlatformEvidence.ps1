param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
$boss=(Get-Content -LiteralPath "$run/encounter-profile.request" -Raw).Trim()
if($boss -notin @('FalseKnight','FailedChampion') -or !(Test-Path "$run/stagger-probe.request")){throw 'Requires bounded authored armor-platform fixture.'}
$hk=Get-BoundedRuntimeLog $run
if($hk -match 'HK_TARGET_FIXTURE_FAILED|SESSION_TERMINAL[^\r\n]*phase=(Preparing|Active)'){throw 'Platform diagnostic failed before completion.'}
if($hk -notmatch 'HK_ARMOR_STANDING_FIXTURE_PASS nativeInput=True grounded=True'){throw 'Native movement/jump did not land Yi on armor.'}
$standing=[regex]::Match($hk,'HK_NATIVE_ARMOR_STANDING encounter=[\w-]+ collider=(-?\d+) grounded=True nativeTop=([\d.E+-]+) yiX=[\d.E+-]+ yiY=([\d.E+-]+) nativeState=(Opened|Hit)')
if(!$standing.Success -or [Math]::Abs([float]$standing.Groups[2].Value-[float]$standing.Groups[3].Value) -gt .1){throw 'Native grounded pose does not match the authored platform top.'}
if($boss -eq 'FailedChampion'){
 $admit=[regex]::Match($hk,"HK_NATIVE_ARMOR_PLATFORM exported=True scene=GG_Failed_Champion[^\r\n]*nativeCollider=$($standing.Groups[1].Value) ")
 if(!$admit.Success -or $hk.Substring($admit.Index) -notmatch 'HK_NATIVE_ARMOR_PLATFORM exported=False scene=GG_Failed_Champion'){throw 'Exact Soft Terrain platform admission/recovery removal unproven.'}
}
Write-Output "PASS (scoped): $boss native-input jump/landing, grounded Yi at authored platform top, exact platform activation/removal. Does not certify armor stagger reset or every tier."
