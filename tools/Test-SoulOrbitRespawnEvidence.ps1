param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Wrong Soul family.'}
$expected=if($boss -eq 'SoulMaster'){4}else{6}
$casts=@([regex]::Matches($hk,'(?m)^(\S+Z) HK_SOUL_ORBIT_VOLLEY group=(\d+) owner=(-?\d+) nativeSpawns=(\d+) independentDefense=True frame=\d+'))
$proved=0;$incomplete=0;$owners=@{};$roots=@{};$reuse=0
foreach($cast in $casts){
 $group=$cast.Groups[2].Value;$owner=$cast.Groups[3].Value;$at=[datetime]::Parse($cast.Groups[1].Value)
 if([int]$cast.Groups[4].Value -ne $expected){throw 'Native orbit spawn count changed.'}
 $samples=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_ORBIT_SPAWN_SAMPLE group=$group root=(-?\d+) sample=(\d+) state=(\S+) active=(True|False) fsmEnabled=(True|False) renderer=(True|False) alpha=([0-9.]+) scale=\(([^)]+)\)[^\r\n]*")|Where-Object {
  $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at -and $time -lt $at.AddSeconds(4)
 })
 $births=@($samples|Where-Object {$_.Groups[3].Value -eq '1'})
 if(@($births|ForEach-Object {$_.Groups[2].Value}|Select-Object -Unique).Count -ne $expected){$incomplete++;continue}
 foreach($birth in $births){
  $id=$birth.Groups[2].Value;$key=$owner+':'+$id
  if($roots.ContainsKey($key)){$reuse++};$roots[$key]=$true
  $visible=@($samples|Where-Object {
   $_.Groups[2].Value -eq $id -and $_.Groups[4].Value -eq 'Orbiting' -and $_.Groups[5].Value -eq 'True' -and $_.Groups[6].Value -eq 'True' -and
   $_.Groups[7].Value -eq 'True' -and [double]::Parse($_.Groups[8].Value,[cultureinfo]::InvariantCulture) -gt 0 -and
   @($_.Groups[9].Value.Split(',')|Select-Object -First 2|Where-Object {[math]::Abs([double]::Parse($_,[cultureinfo]::InvariantCulture)) -lt .1}).Count -eq 0
  })
  $bindings=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_REFLECTION_BINDING id=(-?\d+) activation=(\d+) root=$id\b")|Where-Object {
   $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at -and $time -lt $at.AddSeconds(.3)
  })
  $resolved=$false
  foreach($binding in $bindings){
   $cid=$binding.Groups[2].Value;$activation=$binding.Groups[3].Value
   $resolved=$resolved -or @([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_ORBIT_IMPACT id=$cid activation=$activation state=Impact parent=none frame=\d+")|Where-Object {
    $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at -and $time -lt $at.AddSeconds(.3)
   }).Count -gt 0
  }
  $admission=[regex]::Match($hk,"HK_SOUL_NATIVE_IMPACT_RESTORED root=$id impact=-?\d+ collider=(-?\d+) ")
  if($admission.Success){
   $cid=$admission.Groups[1].Value
   $resolved=$resolved -or @([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_VERDICT encounter=[\w-]+ collider=$cid activation=\d+ action=\d+ kind=(?:damage|parry|counter) ")|Where-Object {
    $time=[datetime]::Parse($_.Groups[1].Value);$time -ge $at.AddMilliseconds(-10) -and $time -lt $at.AddSeconds(.3)
   }).Count -gt 0
  }
  if(!$visible.Count -and !$resolved){throw "Spawned orbit root stayed invisible without an observed defense: group=$group root=$id"}
 }
 $owners[$owner]=1+$owners[$owner];$proved++
}
$settled=@([regex]::Matches($hk,'(?m)^(\S+Z) HK_SOUL_ORBIT_TWEEN_SETTLED root=(-?\d+) spinner=(-?\d+) collider=-?\d+ activation=\d+ actionFinished=True nativeState=Spawn otherPending=(\d+) nativeCompletionOnly=True frame=\d+'))
$continued=0
foreach($event in $settled){
 $at=[datetime]::Parse($event.Groups[1].Value);$owner=$event.Groups[3].Value
 if([int]$event.Groups[4].Value -ge $expected){throw 'Stopped expansion did not settle its own action.'}
 if(@($casts|Where-Object {$_.Groups[3].Value -eq $owner -and [datetime]::Parse($_.Groups[1].Value) -gt $at}).Count){$continued++}
}
if($proved -lt 3 -or $reuse -lt $expected -or $continued -lt 1 -or !@($owners.Values|Where-Object {$_ -ge 3}).Count){throw "Repeated native respawn not all exercised: casts=$proved reuse=$reuse afterSettlement=$continued incomplete=$incomplete"}
Write-Output "PASS (scoped): $boss complete native orbit casts=$proved reused roots=$reuse later casts after stopped-tween settlement=$continued incomplete capture groups=$incomplete. Native victory and other tiers remain separate."
