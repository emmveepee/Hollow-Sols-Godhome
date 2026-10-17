param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireReflection,[switch]$ReflectionOnly,[switch]$RequireTerrainCleanup)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Sheo'){throw 'Wrong native encounter.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
function Near([string]$log,[string]$pattern,[DateTimeOffset]$time,[int]$milliseconds=100){
 return @([regex]::Matches($log,$pattern)|Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups[1].Value)-$time).TotalMilliseconds) -le $milliseconds})
}
$blue=0;$pink=0;$orange=0
foreach($event in [regex]::Matches($hk,'(\S+Z) HK_SHEO_PAINT_COUNTER action=(\d+) state=\S+ kind=(StrikeParried|UnboundedCounter) nativeSequenceContinues=True')){
 $time=[DateTimeOffset]::Parse($event.Groups[1].Value);$action=$event.Groups[2].Value
 if($event.Groups[3].Value -eq 'StrikeParried'){
  if(!(Near $ns "(\S+Z) NS_VOLUME_VERDICT[^\r\n]* action=$action kind=parry[^\r\n]*" $time).Count){throw 'Blue suppression lacks real native parry.'}
  if(!(Near $hk "(\S+Z) HK_STRIKE_PARRY_RECEIPT sequence=\d+ action=$action accepted=True damage=0" $time).Count){throw 'Blue receipt not accepted.'}
  if(!(Near $hk "(\S+Z) HK_SHEO_PAINT_SPAWN_SUPPRESSED action=$action state=S_3 prefab=Paint_Shot_B_fix" $time 500).Count){throw 'Blue native pool emission not suppressed.'}
  $blue++
 }else{
  if(!(Near $ns "(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+" $time).Count){throw 'Pink suppression lacks real native unbound.'}
  foreach($producer in @('GSlash_2 prefab=Paint_Shot_P_Up','Drops prefab=Paint_Shot_P_Down')){
   if(!(Near $hk "(\S+Z) HK_SHEO_PAINT_SPAWN_SUPPRESSED action=$action state=$producer" $time 1000).Count){throw 'A pink native pool producer was not suppressed.'}
  }
  $pink++
 }
}
foreach($event in [regex]::Matches($hk,'(\S+Z) HK_AUTHORED_MOVE_COUNTER entity=-?\d+ action=(\d+) exit=Stun_Reset hold=0 nativeFSM=True')){
 $time=[DateTimeOffset]::Parse($event.Groups[1].Value);$action=$event.Groups[2].Value
 if(!(Near $ns "(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+" $time).Count){throw 'Orange stagger lacks real native unbound.'}
 if(!(Near $hk "(\S+Z) HK_COUNTER_RESULT sequence=\d+ attack=$action applied=True" $time).Count){throw 'Orange counter was not acknowledged.'}
 $orange++
}
if(!$ReflectionOnly -and (!$blue -or !$pink -or !$orange)){throw 'Missing native blue, pink or orange defense coverage.'}
$reflections=[regex]::Matches($hk,'HK_PROJECTILE_REFLECTED id=-?\d+ activation=\d+ speed=[\d.E+-]+ nativeBody=True').Count
if($RequireReflection -or $ReflectionOnly){
 if(!$reflections -or $hk -notmatch 'HK_REFLECTED_HIT[^\r\n]*collision=True'){throw 'Actual paint reflection and native boss impact remain unproven.'}
 foreach($event in [regex]::Matches($hk,'(\S+Z) HK_PAINT_NATIVE_FLIGHT id=(-?\d+) activation=(\d+)')){
  $time=[DateTimeOffset]::Parse($event.Groups[1].Value);$id=$event.Groups[2].Value;$activation=$event.Groups[3].Value
  if(!(Near $ns "(\S+Z) NS_PROJECTILE_RESOLVED id=$id activation=$activation [^\r\n]*verdict=parry reflected=True" $time).Count){throw 'Paint flight lacks a matching actual native parry.'}
 }
}
if($RequireTerrainCleanup -and $hk -notmatch 'HK_REFLECTED_TERRAIN_POP'){throw 'Terrain cleanup remains unexercised.'}
Write-Output "PASS (scoped): Sheo $Session blueParries=$blue pinkUnbounds=$pink orangeStaggers=$orange nativeReflections=$reflections. Damage, final closure and tier coverage are separate gates."
