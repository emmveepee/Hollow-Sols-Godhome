param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireDamage,[switch]$RequireFullStagger)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Not a native Soul boss capture.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
if($RequireDamage){& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing}
$begins=@([regex]::Matches($hk,'(?m)^(\S+Z) HK_SOUL_DIVE_PATTERN_ENDED entity=(-?\d+) nativeExit=Deflated_Stagger wound=True renderer=True invincible=False fsmEnabled=True stopped=False'))
$completed=0;$injured=0
foreach($begin in $begins){
 $entity=$begin.Groups[2].Value;$at=[datetime]::Parse($begin.Groups[1].Value)
 $restart=[regex]::Match($hk.Substring($begin.Index),"(?m)^(\S+Z) HK_SOUL_NATIVE_DIVE_RESTART entity=$entity attackCountBefore=\d+ attackCountReset=0 nativeExit=(?:Reactivate|Tele_Out) nativeNext=Tele_Quake phaseWrite=False")
 if(!$restart.Success){continue}
 $end=[datetime]::Parse($restart.Groups[1].Value)
 if($RequireFullStagger){
  $window=$hk.Substring($begin.Index,$restart.Index)
  $stun=[regex]::Match($window,"HK_SOUL_DEFLATED_STAGE entity=$entity stage=3 clip=Stun seconds=3 vulnerable=True gameplayTime=([0-9.]+)")
  $away=[regex]::Match($window,"HK_SOUL_DEFLATED_STAGE entity=$entity stage=4 clip=Stun_Away seconds=[0-9.]+ vulnerable=False gameplayTime=([0-9.]+)")
  if(!$stun.Success -or !$away.Success){throw 'Full native stagger duration evidence missing.'}
  $held=[double]::Parse($away.Groups[1].Value,[cultureinfo]::InvariantCulture)-[double]::Parse($stun.Groups[1].Value,[cultureinfo]::InvariantCulture)
  if($held -lt 2.99 -or $held -gt 3.15){throw "Native stagger plateau duration is incorrect: $held"}
  if($restart.Value -notmatch 'nativeExit=Tele_Out'){throw 'Recovery skipped native teleport departure.'}
  foreach($soundStage in @(1,2,3)){
   $sounds=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_STAGGER_AUDIO entity=$entity stage=$soundStage nativePooledPlayer=True") | Where-Object {
    $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at.AddMilliseconds(-100) -and $time -le $at.AddSeconds(2)
   })
   if($sounds.Count -ne 1){throw 'Native stagger audio stage missing or duplicated.'}
  }
 }
 if(($end-$at).TotalSeconds -gt 5){throw 'Deflated recovery missed its bounded native restart.'}
 $previous=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_COUNTER_STREAK entity=$entity action=(\d+) count=(\d+)") | Where-Object {[datetime]::Parse($_.Groups[1].Value) -le $at})
 if($previous.Count -lt 3){throw 'Deflation lacks three actual counter receipts.'}
 $three=@($previous | Select-Object -Last 3)
 if(($three|ForEach-Object {$_.Groups[3].Value}) -join ',' -ne '1,2,3'){throw 'Deflation did not follow three consecutive counters.'}
 foreach($counter in $three){
  $action=$counter.Groups[2].Value;$time=[datetime]::Parse($counter.Groups[1].Value)
  $acks=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$time).TotalMilliseconds) -le 100})
  if($acks.Count -ne 1){throw 'Counter lacks an exact native acknowledgement.'}
  $seq=$acks[0].Groups[2].Value
  if(!@([regex]::Matches($ns,"(?m)^(\S+Z) NS_COUNTER_SUBMITTED sequence=$seq attack=$action") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$time).TotalMilliseconds) -le 100}).Count){throw 'Counter lacks actual Yi submission.'}
  if(!@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=[\w-]+ collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$time).TotalMilliseconds) -le 100}).Count){throw 'Counter lacks actual native unbound verdict.'}
 }
 $damage=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SUMMON_HIT entity=$entity generation=\d+ kind=\w+ before=(\d+) after=(\d+)") | Where-Object {
  $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at -and $time -le $end -and [int]$_.Groups[3].Value -lt [int]$_.Groups[2].Value
 })
 if($damage.Count){$injured++}
 $later=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_COUNTER_STREAK entity=$entity action=\d+ count=1") | Where-Object {$time=[datetime]::Parse($_.Groups[1].Value);$time -gt $end -and ($time-$end).TotalSeconds -lt 5})
 if($later.Count){$completed++}
}
if($completed -lt 2 -or $injured -lt 2){throw 'Need repeated actual vulnerable deflations and subsequent native diving counters.'}
Write-Output "PASS (scoped): $boss actual three-counter recoveries=$completed, vulnerable native wound damage=$injured, native dive restarts. Strict damage required=$RequireDamage; pixels and other tiers remain separate."
