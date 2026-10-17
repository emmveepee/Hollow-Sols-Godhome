param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native runtime failure invalidates tracking evidence.'}
if($ns -notmatch 'NS_HOMING_FIXTURE .*nativeEquip=True'){throw 'No native Shadow Hunter equip evidence.'}
if($ns -match 'NS_HOMING_TARGET .*available=False'){throw 'Native follower selected an unavailable translated target.'}
$selected=@([regex]::Matches($ns,'NS_HOMING_TARGET encounter=([\w-]+) entity=(-?\d+) activation=(\d+) available=True arrow=-?\d+ nativeFrame=(\d+)'))
$selected=$selected | Group-Object { $_.Groups[1].Value+"/"+$_.Groups[2].Value+"/"+$_.Groups[3].Value } | ForEach-Object { $_.Group[0] }
$connected=$false
foreach($s in $selected){
 $enc=$s.Groups[1].Value;$entity=$s.Groups[2].Value;$activation=$s.Groups[3].Value
 foreach($out in [regex]::Matches($ns,"NS_NATIVE_ARROW_OUTCOME encounter=$enc entity=$entity activation=$activation dealer=-?\d+ weapon=\(Weapon\)3_[^ ]+ damage=[\d.E+-]+ nativeFrame=(\d+)")){
  $frame=$out.Groups[1].Value
  if([int]$frame -lt [int]$s.Groups[4].Value){continue}
  $wire=[regex]::Match($ns,"NS_HIT_OUTCOME sequence=(\d+) frame=$frame entity=$entity kind=Bow damage=[\d.E+-]+ encounter=$enc activation=$activation")
  if(!$wire.Success){continue};$seq=$wire.Groups[1].Value
  $hit=[regex]::Match($hk,"HK_SUMMON_HIT entity=$entity generation=$activation kind=Bow before=(\d+) after=(\d+) sequence=$seq encounter=$enc")
  if($hit.Success -and [int]$hit.Groups[2].Value -lt [int]$hit.Groups[1].Value){$connected=$true;break}
 }
}
if(!$connected){throw 'No native acquired target -> Shadow Hunter loss -> acknowledged HK damage chain.'}
if($ns -match 'NS_HOMING_TARGET[^\r\n]*available=False'){throw 'Native follower selected an inactive translated target.'}
if($ns -notmatch 'NS_HOMING_TARGET_DISPOSE retained=0'){throw 'Translated native homing query owners did not drain.'}
Write-Output "PASS (scoped): session=$Session native Shadow Hunter acquisition, target damage, no observed inactive acquisition and zero retained query owners. Authored Watcher layer admission is separately audited; this does not establish every dormant transition, upgrade tier, trajectory or whole encounter completion."
