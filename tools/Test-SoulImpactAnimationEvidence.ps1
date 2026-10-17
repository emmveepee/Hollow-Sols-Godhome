param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireVerdictMix)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Wrong native Soul family.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$owners=@([regex]::Matches($hk,'HK_SOUL_NATIVE_IMPACT_RESTORED root=(-?\d+) impact=-?\d+(?: collider=(-?\d+))? nativeAnimationOwner=True poolReset=True'))
$proved=0;$reflected=0;$injured=0;$reused=0;$incomplete=0;$actualInjuries=0;$actualReflections=0
foreach($owner in $owners){
 $id=$owner.Groups[1].Value;$collider=$owner.Groups[2].Value
 $episodes=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_ORB_LIFETIME root=$id state=Impact parent=(\S+) impactActive=True gameplayTime=([0-9.]+) impactElapsed=0(?:\s|$)"))
 foreach($episode in $episodes){
  $at=[datetime]::Parse($episode.Groups[1].Value);$gameAt=[double]::Parse($episode.Groups[3].Value,[cultureinfo]::InvariantCulture)
  $frames=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_IMPACT_DIRECT root=$id animator=-?\d+ cachedAlive=\d+ enabled=True paused=False clip=Impact animationFrame=(\d+) playing=(?:True|False)")|Where-Object {
   $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at.AddMilliseconds(-10) -and $time -le $at.AddSeconds(1)
  })
  $cleared=@([regex]::Matches($hk,"HK_SOUL_ORB_LIFETIME root=$id state=Impact parent=\S+ impactActive=False gameplayTime=([0-9.]+) impactElapsed=([0-9.]+)")|Where-Object {
   $time=[double]::Parse($_.Groups[1].Value,[cultureinfo]::InvariantCulture);$time -ge $gameAt -and $time -le $gameAt+.8
  })
  $late=@([regex]::Matches($hk,"HK_SOUL_ORB_LIFETIME root=$id state=Impact parent=\S+ impactActive=True gameplayTime=([0-9.]+) impactElapsed=([0-9.]+)")|Where-Object {
   $time=[double]::Parse($_.Groups[1].Value,[cultureinfo]::InvariantCulture);$elapsed=[double]::Parse($_.Groups[2].Value,[cultureinfo]::InvariantCulture)
   $time -ge $gameAt+.8 -and $time -lt $gameAt+2 -and $elapsed -ge .8
  })
  if($late.Count){throw "Native impact remained active beyond the animation window: root=$id gameplayTime=$gameAt"}
  if(!$cleared.Count -or @($frames | ForEach-Object {$_.Groups[2].Value} | Select-Object -Unique).Count -lt 3){$incomplete++;continue}
  $proved++
  if($episode.Groups[2].Value -eq 'none'){$reflected++}else{$injured++}
  $later=@([regex]::Matches($hk,"HK_SOUL_ORB_LIFETIME root=$id state=Orbiting parent=Orb_Spinner impactActive=(True|False) gameplayTime=([0-9.]+)")|Where-Object {
   [double]::Parse($_.Groups[2].Value,[cultureinfo]::InvariantCulture) -gt $gameAt+.8
  })
  if(@($later | Where-Object {$_.Groups[1].Value -eq 'True'}).Count){throw 'Pooled orbit reuse retained an active impact burst.'}
  if($later.Count){$reused++}
  if($RequireVerdictMix){
   if(!$collider){throw 'Exact root/collider verdict binding missing.'}
   $verdicts=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_VERDICT encounter=[\w-]+ collider=$collider activation=(\d+) action=\d+ kind=(damage|parry|counter) hostFrame=\d+ nativeFrame=(\d+) ")|Where-Object {
    $time=[datetime]::Parse($_.Groups[1].Value);$time -le $at.AddMilliseconds(50) -and $time -ge $at.AddSeconds(-3)
   })
   if(!$verdicts.Count){throw "Impact lacks actual matching Yi injury or defense verdict: root=$id collider=$collider at=$at parent=$($episode.Groups[2].Value)"}
   if($verdicts.Count -ne 1){throw 'Impact verdict ownership is ambiguous.'}
   $v=$verdicts[0];$activation=$v.Groups[2].Value;$kind=$v.Groups[3].Value;$frame=$v.Groups[4].Value
   $resolution=[regex]::Match($ns,"NS_PROJECTILE_RESOLVED id=$collider activation=$activation sameNativeFrame=$frame verdict=$kind reflected=(True|False)")
   if(!$resolution.Success){throw 'Impact lacks exact native projectile resolution.'}
   if($episode.Groups[2].Value -eq 'none'){
    if($resolution.Groups[1].Value -ne 'True' -or $kind -eq 'damage'){throw 'Detached impact lacks actual native reflection.'};$actualReflections++
   }elseif($kind -eq 'damage'){$actualInjuries++}
  }
 }
}
if($proved -lt 2 -or $reflected -lt 1 -or $injured -lt 1 -or $reused -lt 1){throw "Injury/reflection animation and clean pooled reuse not all exercised: impacts=$proved reflected=$reflected attached=$injured reuse=$reused"}
if($RequireVerdictMix -and ($actualInjuries -lt 1 -or $actualReflections -lt 1)){throw 'Both actual injury and actual reflection animation must be exercised.'}
Write-Output "PASS (scoped): $boss native one-shot impacts=$proved reflected=$reflected attached=$injured clean reused owners=$reused exact Yi verdict binding=$RequireVerdictMix incomplete episodes=$incomplete. Pixels and other tiers remain separate."
