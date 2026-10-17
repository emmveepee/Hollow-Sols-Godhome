param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
& "$PSScriptRoot/Test-TravelEvidence.ps1" -Session $Session
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($ns -match 'NS_WALL_FIXTURE_FAILED'){throw 'Native climb failure.'}
if($hk -notmatch 'HK_TRAVEL_WALL_FIXTURE_WAIT scene=GG_Atrium seconds=44'){throw 'Missing bounded root wall test interval.'}
for($i=0;$i -lt 7;$i++){
 $state=if($i -lt 2){'(Normal|WallSlide)'}else{'Normal'}
 if($ns -notmatch "NS_WALL_FIXTURE_PASS index=$i nativeWallRun=True gain=[\d.E+-]+ finalState=$state[^\r\n]* scene=GG_Atrium"){throw "Godhome wall$i native run/ledge exit unproven."}
 if($i -lt 2){
  $exit=[regex]::Match($ns,"NS_WALL_FIXTURE_PASS index=$i [^\r\n]*ceilingBlocked=True nativeCeilingReached=True nativeJumped=True jumpAway=([\d.E+-]+) scene=GG_Atrium")
  if(!$exit.Success -or [float]$exit.Groups[1].Value -lt .25 -or $ns -notmatch "NS_WALL_NATIVE_JUMP index=$i nativeResult=True[^\r\n]*scene=GG_Atrium"){throw "Tunnel wall$i native ceiling/jump/away displacement unproven."}
 }
}
if($ns -notmatch 'NS_WALL_FIXTURE_COMPLETE surfaces=7 originalPositionRestored=True scene=GG_Atrium'){throw 'Seven-strip test did not restore native player position.'}
if($hk -notmatch 'HK_WALL_ART_PRESENTED[^\r\n]* strips=7 nativePixels=True caps=Sliced width=0.64'){throw 'Seven-strip native wall artwork unproven.'}
Write-Output 'PASS (scoped): seven selective Godhome surfaces support native wall run; two tunnel ceiling/jump exits and five exposed ledge exits, original position restored, native wall art presented; no grants or campaign travel.'
