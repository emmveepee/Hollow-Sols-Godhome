param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireSlashCounter,[switch]$RequireFlameDamage,[switch]$RequireBalloonCost,[switch]$RequirePlatformCull,[switch]$RequireBurnExpiry,[switch]$RequireWholePillar,[switch]$RequireNativeProjectiles,[switch]$RequirePolygonReuse,[switch]$RequireSafeUppercut,[switch]$RequireBatDamage,[switch]$RequireProjectileGroups,[ValidateSet(5,18,36)][int]$BurnTick=18)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'NightmareKingGrimm'){throw 'Wrong encounter profile.'}
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'NKG integration failure; preserve evidence.'}
if($RequireFlameDamage){
 foreach($name in @('Pillar')){
  $windows=[regex]::Matches($hk,"HK_FSM_ATTACK_WINDOW id=(-?\d+) name=$name activation=(\d+) action=(\d+) active=True damage=[12] frame=\d+")
  $matched=$false
  foreach($w in $windows){
   $id=$w.Groups[1].Value;$activation=$w.Groups[2].Value;$action=$w.Groups[3].Value
   if($ns -match "NS_VOLUME_VERDICT encounter=\S+ collider=$id activation=$activation action=$action kind=damage "){$matched=$true;break}
  }
  if(!$matched){throw "No exact native damage verdict for authored $name collision."}
 }
 $burns=[regex]::Matches($ns,"NS_BURN_NATIVE_DOT amount=$BurnTick healthBefore=([\d.]+) health=([\d.]+) nativeTime=([\d.]+) frame=\d+") | Where-Object {[double]$_.Groups[1].Value-[double]$_.Groups[2].Value -eq $BurnTick}
 if($burns.Count -lt 2 -or $ns -notmatch 'NS_BURN_NATIVE_APPLIED module=(-?\d+) new=False' -or $ns -notmatch 'NS_BURN_WINDOW .*directDamage=False nativeBuff=True'){throw 'Missing actual native burn DOT and overlap extension.'}
 foreach($window in [regex]::Matches($ns,'NS_BURN_WINDOW id=(-?\d+) activation=(\d+)')){
  $id=$window.Groups[1].Value;$activation=$window.Groups[2].Value
  if($ns -match "NS_VOLUME_VERDICT encounter=\S+ collider=$id activation=$activation action=\d+ kind=damage "){throw 'Trail still used direct-hit authority.'}
 }
}
if($RequireSlashCounter){
 $hits=[regex]::Matches($hk,'HK_GRIMM_SLASH_COUNTER entity=(-?\d+) action=(\d+) state=Slash_Recover continuation=Uppercut_Antic nativeSequenceContinues=True frame=(\d+)')
 if($hits.Count -lt 2){throw 'Need two actual slash counters and native continuations.'}
 $requests=@()
 foreach($hit in ($hits | Select-Object -First 2)){
  $entity=$hit.Groups[1].Value;$action=$hit.Groups[2].Value;$at=[long]$hit.Groups[3].Value
  $request=[regex]::Match($ns,"NS_KICK_FIXTURE attack=$action nativeState=True requested=(SkullKick|AerialParry)")
  if(!$request.Success){throw 'Missing native defense fixture identity.'};$requests += $request.Groups[1].Value
  if($ns -notmatch "NS_TAICHI_NATIVE attack=$action weakness=True bounce=True frame=\d+" -or $ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=\d+ damaging=False"){throw 'Missing actual native Tai-Chi/strike retirement.'}
  $up=[regex]::Matches($hk,"HK_SOUL_RECEIVER_STATE encounter=\S+ entity=$entity activation=\d+ states=Control:Uppercut Up,[^\r\n]*frame=(\d+)") | Where-Object {[long]$_.Groups[1].Value -gt $at -and [long]$_.Groups[1].Value -lt $at+240} | Select-Object -First 1
  if(!$up){throw 'Counter did not continue to native uppercut.'}
  $rain=[regex]::Matches($hk,"HK_SOUL_RECEIVER_STATE encounter=\S+ entity=$entity activation=\d+ states=Control:UP Explode,[^\r\n]*frame=(\d+)") | Where-Object {[long]$_.Groups[1].Value -gt [long]$up.Groups[1].Value -and [long]$_.Groups[1].Value -lt $at+400}
  if(!$rain){throw 'Counter did not continue to native fire rain.'}
 }
 if($requests -notcontains 'SkullKick' -or $requests -notcontains 'AerialParry'){throw 'First two captures must cover skull kick and aerial parry.'}
}
if($RequireBurnExpiry -and ($ns -notmatch 'NS_BURN_NATIVE_EXPIRED module=' -or $ns -notmatch 'NS_BURN_NATIVE_RELEASED colliders=0 modules=0')){throw 'Missing native burn expiration/zero-module cleanup.'}
if($RequireWholePillar -and $hk -notmatch 'HK_FSM_FLAME_COUNTER .*wholePillar=True shapes=2 nativeRecycle=True rootActive=False'){throw 'Missing actual whole-pillar native recycle.'}
if($RequireNativeProjectiles -and ($hk -notmatch 'HK_PROJECTILE_RESOLVED .*nativeImpact=True root=Nightmare Firebat\(Clone\) state=Impact siblingRetired=True' -or $hk -notmatch 'HK_GRIMM_BALL_IMPACT .*nativeDoHit=True')){throw 'Missing confirmed native bat/balloon impact cleanup.'}
if($RequirePolygonReuse){
 $reuse=[regex]::Matches($ns,'NS_POLYGON_REUSE_VERIFIED .*vertices=64 maxWorldError=([\d.Ee+-]+)')
 if($reuse.Count -lt 2){throw 'Missing actual retained-circle round-trip checks.'}
 foreach($r in $reuse){if([double]$r.Groups[1].Value -gt .003){throw 'Retained polygon exceeds native error bound.'}}
 if($ns -notmatch 'NS_NKG_ATTACK_COST .*polygonReuses=[1-9]\d{3,}'){throw 'Missing substantive native polygon reuse.'}
}
if($RequireSafeUppercut){
 if($hk -notmatch 'HK_GRIMM_UPPERCUT_SAFE .*damage=False fireRain=True'){throw 'Missing countered uppercut suppression.'}
 $safe=[regex]::Matches($hk,'HK_GRIMM_SAFE_UPPERCUT_WINDOW entity=(-?\d+) action=(\d+) damaging=False nativeMotion=True frame=(\d+)')
 if($safe.Count -lt 2){throw 'Need two actual harmless native uppercuts.'}
 foreach($w in $safe){
  $action=$w.Groups[2].Value
  if($hk -match "HK_POOLED_ATTACK .*active=True .*action=$action frame=\d+" -or $ns -match "NS_VOLUME_VERDICT .*action=$action kind=damage "){throw 'Countered uppercut exported/inflicted damage.'}
 }
}
if($RequireBatDamage){
 if(([regex]::Matches($ns,'NS_NKG_GRANT nativeHealth=True addedImmunity=False')).Count -lt 5){throw 'Missing repeated NKG refills without added immunity.'}
 $damaged=@{}
 foreach($impact in [regex]::Matches($hk,'HK_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) [^\r\n]*nativeImpact=True root=Nightmare Firebat\(Clone\)')){
  $id=$impact.Groups[1].Value;$activation=$impact.Groups[2].Value
  if($ns -match "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=damage reflected=False"){$damaged["$id/$activation"]=$true}
 }
 if($damaged.Count -lt 2){throw 'Need two actual fire-bat native damage verdicts and matching native impacts.'}
}
if($RequireProjectileGroups){
 $groups=[regex]::Matches($ns,'NS_PROJECTILE_GROUP_RETIRED action=(\d+) collider=(-?\d+) nativeFrame=(\d+) siblingDamage=False')
 if($groups.Count -lt 2){throw 'Need two independent native projectile group resolutions.'}
 foreach($group in $groups){
  $action=$group.Groups[1].Value;$id=$group.Groups[2].Value;$nativeFrame=$group.Groups[3].Value
  $owner=[regex]::Match($ns,"NS_VOLUME_VERDICT encounter=(\S+) collider=$id activation=\d+ action=$action kind=(?:damage|parry|counter) hostFrame=\d+ nativeFrame=$nativeFrame ")
  if(!$owner.Success){throw 'Projectile group lacks its exact native verdict/encounter identity.'}
  $encounter=[regex]::Escape($owner.Groups[1].Value)
  $verdicts=[regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=$encounter [^\r\n]*action=$action kind=(?:damage|parry|counter) ")
  if($verdicts.Count -ne 1){throw 'Native projectile siblings resolved more than one hit/defense.'}
  if($hk -notmatch "HK_PROJECTILE_RESOLVED id=$id activation=\d+ nativeImpact=True root=Nightmare Firebat\(Clone\) state=Impact siblingRetired=True"){throw 'Native projectile group lacks matching native root impact.'}
 }
}
if($RequirePlatformCull -and ($ns -notmatch 'NS_BALLOON_OUTSIDE_PLATFORM .*wholeShape=True nativeLifetime=True queuedHitDrain=True' -or $ns -notmatch 'NS_NKG_ATTACK_COST .*culled=[1-9]')){throw 'Missing actual projectile cutoff and reduced computational workload.'}
if($RequireBalloonCost){
 $costs=[regex]::Matches($ns,'NS_NKG_ATTACK_COST [^\r\n]*active=(\d+) volumes=\d+ meanMs=([\d.]+) maxMs=([\d.]+)[^\r\n]*syncCalls=(\d+) samples=(\d+)(?: culled=(\d+))?') | Where-Object {[int]$_.Groups[1].Value+[int]$_.Groups[6].Value -ge 40}
 if($costs.Count -lt 2){throw 'Missing substantial native balloon workloads.'}
 foreach($c in $costs){if([double]$c.Groups[2].Value -gt 4 -or [int]$c.Groups[4].Value -gt [int]$c.Groups[5].Value){throw 'Balloon attack work/synchronization exceeds focused budget.'}}
}
Write-Output "PASS (scoped): session=$Session flameDamage=$RequireFlameDamage slashCounterContinuation=$RequireSlashCounter balloonCost=$RequireBalloonCost platformCull=$RequirePlatformCull burnExpiry=$RequireBurnExpiry wholePillar=$RequireWholePillar nativeProjectiles=$RequireNativeProjectiles polygonReuse=$RequirePolygonReuse safeUppercut=$RequireSafeUppercut batDamage=$RequireBatDamage projectileGroups=$RequireProjectileGroups. Does not certify normal timing, difficulty sweep, full session or human feel."
