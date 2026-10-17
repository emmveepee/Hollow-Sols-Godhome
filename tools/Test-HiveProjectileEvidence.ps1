param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireMelee)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'HiveKnight'){throw 'Hive Knight evidence required.'}
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL|HK_TARGET_FIXTURE)_FAILED|CONTACT_STOP reason=(?:InvalidDataException|InvalidOperationException)'){throw 'Hive integration failure.'}
$groups=@([regex]::Matches($hk,'HK_HIVE_NATIVE_VOLLEY group=(?<group>\d+) root=Hive_Knight_Glob(?:_\([12]\))? nativeChildren=8 independentDefense=True coreAndBurst=True'))
if($groups.Count -lt 6){throw 'Missing repeated native core-through-burst damage groups.'}
$injuries=@([regex]::Matches($ns,'(?m)^\S+ NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<id>-?\d+) activation=(?<activation>\d+) action=\d+ kind=damage[^\r\n]*damageGroup=(?<group>\d+)'))
$grouped=@($injuries | Where-Object {$_.Groups['group'].Value -ne '0'})
if($grouped.Count -lt 2){throw 'Shared trap injuries were not exercised.'}
if(@($grouped | Group-Object {$_.Groups['encounter'].Value+':'+$_.Groups['group'].Value} | Where-Object Count -gt 1).Count){throw 'One trap injured Yi more than once across its core and burst.'}
if($ns -notmatch 'NS_VOLLEY_PARRY_INDEPENDENT encounter=[\w-]+ group=\d+ collider=-?\d+ siblingsDamaging=[1-8] nativeFrame=\d+'){throw 'Independent sibling defenses were not exercised.'}
$cores=@([regex]::Matches($hk,'HK_POOLED_ATTACK id=(?<id>-?\d+) activation=\d+ active=True category=Melee name=Hive Knight Glob(?: \([12]\))?(?:\s|$)') | ForEach-Object {$_.Groups['id'].Value} | Sort-Object -Unique)
$coreHits=@($injuries | Where-Object {$_.Groups['id'].Value -in $cores})
if(!$coreHits.Count){throw 'No actual armed-core injury; transition coverage unproven.'}
if(@($coreHits | Where-Object {$_.Groups['group'].Value -eq '0'}).Count){throw 'Armed trap core escaped its burst damage group.'}
$cleanup=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_HIVE_PROJECTILE_CLEANUP id=(?<id>-?\d+) activation=(?<activation>\d+) kind=(?<kind>bee|stinger) state=(?<state>[^\r\n]+?) objectActive=(?<active>True|False) colliderEnabled=(?<collider>True|False) frame=\d+'))
$kinds=@{};$reused=0;$hits=0
foreach($end in $cleanup){
 $id=$end.Groups['id'].Value;$activation=$end.Groups['activation'].Value;$kind=$end.Groups['kind'].Value;$at=[DateTimeOffset]::Parse($end.Groups['time'].Value)
 $parries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=parry reflected=True") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -le $at -and ($at-$t).TotalSeconds -lt 3.5})
 if($parries.Count -ne 1){throw 'Hive cleanup lacks a unique actual native parry.'}
 $start=[DateTimeOffset]::Parse($parries[0].Groups['time'].Value)
 if($kind -eq 'stinger' -and $end.Groups['active'].Value -ne 'False'){throw 'Reflected stinger remained active.'}
 if($kind -eq 'bee' -and ($end.Groups['state'].Value -ne 'Spell Death' -or $end.Groups['collider'].Value -ne 'False')){throw 'Bee reflection did not use harmless native cleanup.'}
 if($hk -notmatch "HK_PROJECTILE_REFLECTED id=$id activation=$activation speed=[\d.]+ nativeBody=True damage=21"){throw 'Missing native-body reflection.'}
 $bad=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=[\w-]+ collider=$id activation=$activation action=0 kind=damage") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $start -and $t -le $at.AddMilliseconds(20)})
 if($bad.Count){throw 'Reflected projectile injured Yi.'}
 $kinds[$kind]=$true
 $next=[long]$activation+1
 if($hk -match "HK_POOLED_ATTACK id=$id activation=$next active=True category=Projectile"){$reused++}
 foreach($hit in [regex]::Matches($hk,"(?m)^(?<time>\S+) HK_REFLECTED_HIT id=$id activation=$activation before=(?<before>\d+) after=(?<after>\d+) collision=True")){
  $t=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
  if($t -lt $start -or $t -gt $at.AddMilliseconds(20)){continue}
  $before=[int]$hit.Groups['before'].Value;$delta=$before-[int]$hit.Groups['after'].Value
  if($delta -eq 0){continue}
  if($delta -ne [Math]::Min(21,$before)){throw 'Unexpected native reflection injury.'}
  $hits++
 }
}
if(!$kinds.bee -or !$kinds.stinger -or $reused -lt 2 -or $hits -lt 1){throw "Need both native projectile kinds, repeated reuse and actual boss subtraction: reused=$reused hits=$hits."}
if($RequireMelee){
 $motion=@([regex]::Matches($hk,'HK_HIVE_NATIVE_MELEE_MOTION id=(?<id>-?\d+) activation=(?<activation>\d+) swing=(?<swing>\d+) direction=(?<direction>[\d.]+) deltaX=(?<x>-?[\d.]+) deltaY=(?<y>-?[\d.]+) nativeRecoiling=True nativeState=Swarm'))
 $directions=@{};$positive=0
 foreach($m in $motion){
  $id=$m.Groups['id'].Value;$activation=$m.Groups['activation'].Value;$swing=$m.Groups['swing'].Value;$d=$m.Groups['direction'].Value
  if($hk -notmatch "HK_HIVE_NATIVE_MELEE id=$id activation=$activation swing=$swing sourceCollider=-?\d+ sourceActivation=\d+ direction=$([regex]::Escape($d)) previouslyRecoiling=(?:True|False) nativeRecoiling=True nativeState=Swarm healthReceiver=False"){throw 'Motion lacks actual native melee/recoil lineage.'}
  $r=[double]$d*[Math]::PI/180
  if([double]$m.Groups['x'].Value*[Math]::Cos($r)+[double]$m.Groups['y'].Value*[Math]::Sin($r) -gt .25){$positive++;$directions[$d]=$true}
 }
 if($positive -lt 2 -or !$directions['0'] -or !$directions['180']){throw 'Need native melee nudges in both horizontal directions.'}
 Write-Output "PASS (scoped): $positive native melee/recoil motions, both directions; bees remain native Swarm bodies without health receivers."
}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
Write-Output "PASS (scoped): Hive core/burst groups=$($groups.Count), core injuries=$($coreHits.Count), native reflected hits=$hits, fresh pooled activations=$reused. Tier, victory and paired-return coverage remain separate."
