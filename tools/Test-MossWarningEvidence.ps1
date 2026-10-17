param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL)_FAILED'){throw 'Moss integration failure.'}
$encounters=[regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=\d+ phase=Active')
$reactions=0;$repeated=0
for($i=0;$i -lt $encounters.Count;$i++) {
 $enc=$encounters[$i].Groups[1].Value
 $end=if($i+1 -lt $encounters.Count){$encounters[$i+1].Index}else{$hk.Length}
 $scope=$hk.Substring($encounters[$i].Index,$end-$encounters[$i].Index)
 $states=[regex]::Matches($scope,'HK_MOSS_MOVE_SEQUENCE entity=(-?\d+) action=(\d+) state=(\w+) taichi=(True|False) frame=\d+')
 $current=0;$counts=@{}
 foreach($state in $states) {
  $action=[long]$state.Groups[2].Value;$name=$state.Groups[3].Value
  if($name -eq 'Emerge'){$current=$action}
  if($name -in @('Hit_Left','Hit_Right')) {
   if($current -eq 0 -or $action -ne $current){throw 'Ordinary hit restarted the dash activation.'}
   $reactions++;if(-not $counts.ContainsKey($current)){$counts[$current]=0};$counts[$current]++
  }
 }
 foreach($action in $counts.Keys) {
  $warnings=[regex]::Matches($ns,"NS_CRIMSON_WARNING attack=$action encounter=$enc ").Count
  if($warnings -ne 1){throw "Expected one native warning across hit reactions; got $warnings for $enc/$action"}
  $warning=[regex]::Match($ns,"(?m)^([^\s]+) NS_CRIMSON_WARNING attack=$action encounter=$enc ")
  if(-not $warning.Success){throw 'Native warning timestamp missing.'}
  $warningTime=[datetime]::Parse($warning.Groups[1].Value)
  $sounds=@([regex]::Matches($ns,"(?m)^([^\s]+) NS_CRIMSON_SOUND attack=$action ") | Where-Object { $gap=($warningTime-[datetime]::Parse($_.Groups[1].Value)).TotalMilliseconds; $gap -ge 0 -and $gap -lt 100 })
  if($sounds.Count -ne 1){throw 'Matching single native warning sound call missing.'}
  if($counts[$action] -ge 2){$repeated++}
 }
}
if($reactions -lt 3 -or $repeated -eq 0){throw 'Need repeated ordinary hits within one native dash.'}
Write-Output "PASS (scoped): Moss hit reactions=$reactions repeated-hit dashes=$repeated; native dash IDs retained with one warning/sound each."
