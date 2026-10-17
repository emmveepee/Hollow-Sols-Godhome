param([Parameter(Mandatory)][Guid]$Session,[ValidateSet('Flail','Sky')][string]$Move='Flail',[switch]$BeforeSequenceCap)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'GreyPrinceZote'){throw 'Wrong native boss profile.'}
& "$PSScriptRoot/Test-AuthoredMoveEvidence.ps1" -Session $Session -Boss GreyPrinceZote
$exit=if($Move -eq 'Flail'){'Charge_Fall'}else{'Stun_Start'}
$counters=@([regex]::Matches($hk,"HK_AUTHORED_MOVE_COUNTER entity=(-?\d+) action=(\d+) exit=$exit hold=0 nativeFSM=True frame=(\d+)"))
$caps=@{}
foreach($cap in [regex]::Matches($hk,'HK_ZOTE_SEQUENCE_EVIDENCE_CAPPED entity=(-?\d+) frame=(\d+) transitions=256')){$caps[$cap.Groups[1].Value]=[long]$cap.Groups[2].Value}
if($BeforeSequenceCap){
 if(!$caps.Count){throw 'Explicit native sequence evidence cap missing.'}
 $counters=@($counters|Where-Object {!$caps.ContainsKey($_.Groups[1].Value) -or [long]$_.Groups[3].Value -lt $caps[$_.Groups[1].Value]})
}
if($counters.Count -lt 2){throw "Need repeated actual native $Move counters; observed=$($counters.Count)."}
$generations=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=\d+ phase=Preparing'))
$strikeShapes=@{}
foreach($shape in [regex]::Matches($hk,'HK_POOLED_ATTACK id=(-?\d+) activation=\d+ active=(?:True|False) category=Melee name=(?:Shockwave Spurt[^\r\n]*|Grey Prince) action=')){$strikeShapes[$shape.Groups[1].Value]=$true}
foreach($counter in $counters){
 $entity=$counter.Groups[1].Value;$action=$counter.Groups[2].Value;$frame=[long]$counter.Groups[3].Value
 $encounter=$null
 foreach($generation in $generations){if($generation.Index -gt $counter.Index){break};$encounter=$generation.Groups[1].Value}
 if(!$encounter){throw 'Counter lacks native encounter-generation identity.'}
 $defenseKind=if($Move -eq 'Flail'){'TaiChiKick|UnboundedCounter'}else{'UnboundedCounter'}
 if($ns -notmatch "NS_HIT_OUTCOME sequence=\d+ frame=\d+ entity=$entity kind=($defenseKind)[^\r\n]* attack=$action(?:\s|$)" -and
    $ns -notmatch "NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=$encounter") {throw 'Missing actual Yi defense acknowledgement.'}
 if($Move -eq 'Flail'){
  # Charge Slam can execute its native spawn actions and leave between frame
  # samples. Its exact spawn-owner state below proves that transition.
  foreach($state in @('Charge_Fall')){
   $states=@([regex]::Matches($hk,"HK_ZOTE_MOVE_SEQUENCE entity=$entity action=$action state=$state windup=False active=False retained=True frame=(\d+)"))
   if(!@($states|Where-Object {[long]$_.Groups[1].Value -ge $frame -and [long]$_.Groups[1].Value -le $frame+180}).Count){throw "Native $state ending lost the countered strike identity: action=$action."}
  }
  $waves=@([regex]::Matches($hk,"HK_ZOTE_NATIVE_WAVE_STRIKE entity=$entity action=$action state=Charge_Slam ordinaryWave=True frame=(\d+)"))
  if(!@($waves|Where-Object {[long]$_.Groups[1].Value -ge $frame -and [long]$_.Groups[1].Value -le $frame+180}).Count){throw 'Counter ending did not emit its native same-action wave.'}
  $defense=[regex]::Match($ns,"NS_HIT_OUTCOME sequence=\d+ frame=(\d+) entity=$entity kind=(?:$defenseKind)[^\r\n]* attack=$action(?:\s|$)")
  if(!$defense.Success){$defense=[regex]::Match($ns,"NS_VOLUME_CRIMSON_COUNTER action=$action frame=(\d+) encounter=$encounter")}
  foreach($injury in [regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=$encounter collider=(-?\d+) activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(\d+)")){
   if($strikeShapes.ContainsKey($injury.Groups[1].Value) -and [long]$injury.Groups[2].Value -ge [long]$defense.Groups[1].Value){throw 'Countered flail body or ending wave inflicted later injury.'}
  }
 }
}
Write-Output "PASS (scoped): Zote $Move actual counters=$($counters.Count), native exit and defense acknowledgement. Flail additionally requires retained fall/slam identity and native same-action waves. Other tiers and whole-session injury/lifecycle checks remain separate."
if($BeforeSequenceCap){Write-Output 'Partial sequence window: counters after each explicit owner cap are unverified; injury outcomes to observed counter actions remain checked without a time cutoff.'}
