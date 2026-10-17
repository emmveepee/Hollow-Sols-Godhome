param([Parameter(Mandatory)][Guid]$Session, [Guid]$Baseline=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session" -Lines
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native integration failure.'}
if(-not ($hk -match 'HK_NATIVE_CIRCLE_VERIFIED .*nativeBounds=True contourVertices=64')){throw 'Missing native source circle bounds proof.'}
if(-not ($ns -match 'NS_LOOP_TIMING_ATTACHED .*originalOrderVerified=True nativeSystemsUnchanged=True') -or
 -not ($ns -match 'NS_LOOP_TIMING_DETACHED ownedBrackets=0 nativeSystemsRetained=True')){throw 'Missing native loop preservation/teardown proof.'}
$dealers=@{}; $ids=@{}; $kinds=@{}
foreach($line in $ns){
 if($line -match 'NS_NATIVE_CIRCLE_VERIFIED dealer=(-?\d+) shapes=(\d+) maxWorldError=(\S+) .*nativeCallbacks=True separateParryReceiver=True'){
  if([int]$Matches[2] -ne 1 -or [double]$Matches[3] -gt .003){throw 'Native circle geometry mismatch.'}
  $dealers[$Matches[1]]=$true
 }
}
foreach($line in $ns){if($line -match 'NS_ATTACK_BOUND id=(-?\d+) dealer=(-?\d+) ' -and $dealers.ContainsKey($Matches[2])){$ids[$Matches[1]]=$true}}
foreach($line in $ns){if($line -match 'NS_VOLUME_VERDICT .*collider=(-?\d+).*kind=(damage|parry) ' -and $ids.ContainsKey($Matches[1])){$kinds[$Matches[2]]=$true}}
if($dealers.Count -lt 5 -or -not $kinds.ContainsKey('damage') -or -not $kinds.ContainsKey('parry')){throw 'Missing real native circle damage/parry coverage.'}
function DenseLateCost([Guid]$run){
 $hostLines=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$run" -Lines
 $guestLines=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$run" -Lines
 $dense=@(); $logging=@(); $started=$null
 foreach($line in $hostLines){
  if($line -match '^(\S+) HK_BALLOON_EXPORT_CULL frames=(\d+) examined=(\d+)' -and [int]$Matches[2] -gt 0 -and [double]$Matches[3]/[int]$Matches[2] -ge 30){$dense += [datetime]$Matches[1]}
  if($line -match '^(\S+) DETAIL_REQUEST active=True '){$started=[datetime]$Matches[1]}
  if($line -match '^(\S+) DETAIL_REQUEST active=False ' -and $started){$logging+=,@($started.AddSeconds(-2),([datetime]$Matches[1]).AddSeconds(2));$started=$null}
 }
 if($started){throw 'Capture remains active; use closed evidence.'}
 $values=@()
 foreach($line in $guestLines){
  if($line -notmatch '^(\S+) NS_NATIVE_WORK stage=loop_Physics2DLateUpdate samples=\d+ meanMs=(\S+) '){continue}
  $at=[datetime]$Matches[1]; $value=[double]$Matches[2]
  if(-not ($dense | Where-Object {[math]::Abs(($at-$_).TotalSeconds) -le 1})){continue}
  if($logging | Where-Object {$at -ge $_[0] -and $at -le $_[1]}){continue}
  $values+=$value
 }
 if($values.Count -lt 5){throw 'Need five dense logging-off late-physics windows.'}
 [pscustomobject]@{Windows=$values.Count; MaximumMeanMs=($values | Measure-Object -Maximum).Maximum}
}
$current=DenseLateCost $Session
if($Baseline -ne [Guid]::Empty){
 $before=DenseLateCost $Baseline
 if($current.MaximumMeanMs -ge $before.MaximumMeanMs*.5){throw 'Dense late-physics cost did not materially improve.'}
 "Late physics maximum dense-window mean: before=$($before.MaximumMeanMs)ms after=$($current.MaximumMeanMs)ms."
}
"PASS (scoped): $($dealers.Count) one-shape native circle leases, exact source/receiver bounds and bindings, real native damage/parry, preserved engine order/teardown; $($current.Windows) dense logging-off physics windows. Whole-frame 144 FPS requires its separate gate."
