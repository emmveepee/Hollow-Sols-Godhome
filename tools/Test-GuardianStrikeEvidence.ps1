param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireCounter)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('CrystalGuardian','EnragedGuardian')){throw 'Not a native Guardian capture.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$owned=@([regex]::Matches($hk,'HK_GUARDIAN_OWNED_BEAM entity=(-?\d+) root=(-?\d+) lateColliderBinding=True ceilingLasersIndependent=True'))
if(!$owned.Count){throw 'Exact authored beam root ownership is missing.'}
foreach($owner in $owned){
 $entity=$owner.Groups[1].Value;$weapon=$owner.Groups[2].Value
 if($hk -notmatch "HK_NATIVE_LATE_WEAPON_BOUND entity=$entity root=$weapon source=-?\d+ owner=$weapon nativeReference=True"){throw 'Actual late beam collider did not bind to its authored root.'}
}
$sources=@([regex]::Matches($hk,'HK_GUARDIAN_STRIKE_SOURCE entity=(-?\d+) action=(\d+) collider=(-?\d+) activation=(\d+) role=(beam|arm) crimson=True state=(Beam|Beam_2|Beam_End) nativeOwner=True'))
$pairs=0
foreach($group in @($sources|Group-Object {"$($_.Groups[1].Value)/$($_.Groups[2].Value)"})){
 $roles=@($group.Group|ForEach-Object {$_.Groups[5].Value}|Sort-Object -Unique)
 if($roles.Count -eq 2){$pairs++}
}
if($pairs -lt 2){throw 'Need multiple actual main-beam/arm activations sharing one crimson strike.'}
if($RequireCounter){& "$PSScriptRoot/Test-AuthoredMoveEvidence.ps1" -Session $Session -Boss $boss}
Write-Output "PASS (scoped): $boss native shared beam/arm strikes=$pairs, exact late-root ownership and strict injury gate. Counter hold required=$RequireCounter; other tiers and visual feel remain separate."
