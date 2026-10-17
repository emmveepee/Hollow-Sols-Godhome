param([Parameter(Mandatory)][Guid]$Session, [Guid]$Encounter=[Guid]::Empty, [switch]$Mace)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('FalseKnight','FailedChampion')){throw 'Not an armor/head combat session.'}
if($Encounter -ne [Guid]::Empty){
 function Select-ArmorGeneration([string]$text,[string]$role,[string]$phase){
  $start=[regex]::Match($text,"ENCOUNTER_STATE role=$role encounter=$Encounter geometryFrame=\d+ phase=$phase")
  if(-not $start.Success){throw 'Missing requested armor generation.'}
  $tail=$text.Substring($start.Index)
  $next=[regex]::Match($tail,"ENCOUNTER_STATE role=$role encounter=(?!$Encounter)[\w-]+ geometryFrame=\d+ phase=$phase")
  if($next.Success){return $tail.Substring(0,$next.Index)};return $tail
 }
 $hk=Select-ArmorGeneration $hk 'HollowKnight' 'Preparing'
 $ns=Select-ArmorGeneration $ns 'NineSols' 'Restoring'
}
# A bounded peer shutdown after Ready is recorded but is not an active fight pass.
if(($hk+$ns) -match '_FAILED(?:\s|$)|CONTACT_CHANNEL_STOPPED|HIT_CHANNEL_STOPPED|SESSION_TERMINAL[^\r\n]*phase=(Preparing|Active|Victory|Defeated|Restoring)'){throw 'Session failed; no whole-session armor pass.'}
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss $boss -Encounter $Encounter
if($hk -notmatch 'HK_HUB_COMBAT_ENDED victory=True' -or $hk -notmatch 'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True' -or $ns -notmatch 'NS_SUMMON_DISPOSE retained=0'){throw 'Native victory/visible return/zero disposal missing.'}
$phases=[regex]::Matches($hk,'HK_ARMOR_PHASE encounter=([\w-]+) frame=(\d+) state=(\S+) broken=(True|False) exposed=(True|False) armor=(-?\d+) armorLease=(\d+) armorHP=(\d+) presentedArmor=(\d+) head=(-?\d+) headLease=(\d+) headHP=(-?\d+)')
$bindings=[regex]::Matches($hk,'HK_ARMOR_BOUND encounter=([\w-]+) armor=(-?\d+) head=(-?\d+)')
if($bindings.Count -eq 0){throw 'No native armor/head ownership.'}
foreach($binding in $bindings){
 $enc=$binding.Groups[1].Value;$armor=$binding.Groups[2].Value;$head=$binding.Groups[3].Value
 $frames=@($phases | Where-Object {$_.Groups[1].Value -eq $enc})
 $leases=@($frames | Where-Object {$_.Groups[5].Value -eq 'True'} | ForEach-Object {$_.Groups[11].Value} | Sort-Object -Unique)
 if($leases.Count -lt 3){throw "Missing three actual native head exposures $enc"}
 if(-not ($frames | Where-Object {$_.Groups[4].Value -eq 'True' -and [int]$_.Groups[8].Value -gt 0 -and $_.Groups[9].Value -eq '0'})){throw 'Early native armor refill was not masked.'}
 if(-not ($frames | Where-Object {$_.Groups[3].Value -eq 'Idle_Pause' -and [int]$_.Groups[7].Value -gt 1 -and [int]$_.Groups[9].Value -gt 0})){throw 'Native armor recovery/refill lease absent.'}
 $priorLease=$null
 foreach($f in $frames){
  if($f.Groups[5].Value -eq 'True'){
   if($f.Groups[3].Value -in @('Hit','Hit_2') -and $priorLease -ne $f.Groups[11].Value){throw 'Per-hit reaction replaced exposed head injury/maximum.'}
   $priorLease=$f.Groups[11].Value
  }else{$priorLease=$null}
 }
 foreach($entity in @($armor,$head)){
  $hits=[regex]::Matches($hk,"HK_ARMOR_HIT encounter=$enc entity=$entity activation=(\d+) sequence=(\d+) kind=(\w+) before=(\d+) nativeMinimum=(-?\d+) nativeAfter=(-?\d+) presentedAfter=(\d+) applied=True")
  if($hits.Count -eq 0){throw "No native damaging hits on $entity"}
  foreach($h in $hits){
   $gen=$h.Groups[1].Value;$seq=$h.Groups[2].Value;$hp=$h.Groups[7].Value
   if([int]$h.Groups[5].Value -ge [int]$h.Groups[4].Value){throw 'Applied hit has no observed native subtraction.'}
   $owner=[regex]::Match($ns,"NS_SUMMON_BOUND entity=$entity generation=$gen owner=(-?\d+) encounter=$enc ").Groups[1].Value
   if(-not $owner -or $ns -notmatch "NS_SUMMON_SETTLED sequence=$seq generation=$gen owner=$owner result=Applied health=$hp(?:\s|$)"){throw "Missing exact native settlement $enc/$seq"}
  }
 }
 if($ns -notmatch "NS_SUMMON_RETIRED encounter=$enc entity=$head "){throw 'Head retained outside exposure.'}
 if($ns -notmatch "NS_BOSS_HUD_BOUND native=True entity=$armor " -or $ns -match "NS_BOSS_HUD_BOUND native=True entity=$head "){throw 'Armor/head widget roles wrong.'}
}
if($Mace){
 if($ns -notmatch 'NS_ARMOR_HUD_DEPLETED[^\r\n]*active=True' -or -not (Test-Path "$root/.local/runs/NineSols/$Session/native-head-hud.png")){throw 'Missing depleted armor widget or native exposed head capture.'}
 $counters=[regex]::Matches($hk,'HK_MACE_COUNTER encounter=([\w-]+) action=(\d+) state=(\S+) interrupted=False frame=(\d+)')
 if($counters.Count -eq 0){throw 'No actual acknowledged unbound mace defense.'}
 foreach($c in $counters){
  $enc=$c.Groups[1].Value;$action=$c.Groups[2].Value
  if($ns -notmatch "NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=$enc "){throw 'Host counter has no same-encounter native defense verdict.'}
  $next=[int]$action+1
  if($hk -notmatch "HK_MACE_WINDUP encounter=$enc action=$next " -or $hk -notmatch "HK_POOLED_ATTACK[^\r\n]*active=True[^\r\n]*name=Hitter action=$next "){throw 'No continued native damaging slam after counter.'}
 }
 foreach($cue in [regex]::Matches($hk,'HK_MACE_WINDUP encounter=([\w-]+) action=(\d+) collider=(-?\d+) x=\S+ y=\S+ frame=(\d+)')){
  $enc=$cue.Groups[1].Value;$action=$cue.Groups[2].Value;$collider=$cue.Groups[3].Value
  if($ns -notmatch "NS_CRIMSON_WARNING attack=$action encounter=$enc " -or $ns -notmatch "NS_DASH_CUE_TIMING action=$action encounter=$enc [^\r\n]*completionBudget=0.10"){throw "Missing quick native mace cue $enc/$action"}
  $strike=[regex]::Match($hk.Substring($cue.Index),"(?m)^(\S+) HK_POOLED_ATTACK id=$collider activation=(\d+) active=True[^\r\n]*name=Hitter action=$action frame=\d+")
  $complete=[regex]::Match($ns,"(?m)^(\S+) NS_DASH_CUE_COMPLETE action=$action encounter=$enc animationProgress=([\d.E+-]+) ")
  if(-not $strike.Success -or -not $complete.Success -or [double]$complete.Groups[2].Value -lt 1 -or [DateTime]$complete.Groups[1].Value -gt [DateTime]$strike.Groups[1].Value){throw "Native cue did not complete before mace collision $enc/$action"}
  $activation=$strike.Groups[2].Value
  if($ns -notmatch "NS_ATTACK_POLICY encounter=$enc [^\r\n]*id=$collider activation=$activation action=$action traits=Crimson parriable=False "){throw 'Mace lost non-parriable crimson policy.'}
 }
 Write-Output "PASS: $($counters.Count) native unbound mace verdicts matched by encounter; subsequent damaging slams continue; each native warning completes before its crimson collision."
}
Write-Output "PASS: $boss session=$Session native armor/refill, three head exposures, retained per-hit head lease, separate widgets, exact damaging acknowledgements, native victory/visible return and zero disposal. Grants/placement do not prove normal timing or visual fit."
