param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireMinionReuse)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Flukemarm'){throw 'Wrong arena.'}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|NS_VISUAL|HK_HUB)_FAILED|NS_WALL_FIXTURE_FAILED'){throw 'Native arena integration failure.'}
$water=[regex]::Match($hk,'HK_HUB_SHALLOW_WATER scene=GG_Flukemarm left=([\d.E+-]+) right=([\d.E+-]+) surface=([\d.E+-]+) floor=([\d.E+-]+) depth=([\d.E+-]+)')
if(!$water.Success -or [Math]::Abs(([double]$water.Groups[3].Value-[double]$water.Groups[4].Value)-.30) -gt .001 -or [double]$water.Groups[5].Value -ne .30){throw 'Native ankle-depth floor unproven.'}
if([double]$water.Groups[2].Value-[double]$water.Groups[1].Value -lt 60){throw 'Authored water width unproven.'}
for($i=0;$i -lt 2;$i++){
 $wall=[regex]::Match($ns,"NS_WALL_FIXTURE_PASS index=$i nativeWallRun=True[^\r\n]*ceilingBlocked=True nativeCeilingReached=True nativeJumped=True jumpAway=([\d.E+-]+) scene=GG_Flukemarm jumpExitState=(Normal|WallSlide)")
 if(!$wall.Success -or [double]$wall.Groups[1].Value -lt .25){throw "Native wall$i climb, ceiling, jump and exit unproven."}
}
if($ns -notmatch 'NS_WALL_FIXTURE_COMPLETE surfaces=2 originalPositionRestored=True scene=GG_Flukemarm' -or $hk -notmatch 'HK_WALL_ART_PRESENTED[^\r\n]* strips=2 nativePixels=True'){throw 'Two surfaces/native art/restored position unproven.'}
if($RequireMinionReuse){
 $reused=@([regex]::Matches($hk,'HK_ROSTER_REACTIVATED entity=(-?\d+) activation=(\d+) health=(\d+) nativeSpawnPhase=True frame=\d+ encounter=([\w-]+)'))
 $proven=@{}
 foreach($reuse in $reused){
  $entity=$reuse.Groups[1].Value;$generation=$reuse.Groups[2].Value;$encounter=$reuse.Groups[4].Value
  if([long]$generation -lt 2){continue}
  if($hk -notmatch "HK_NATIVE_MINION_SPAWN entity=$entity activation=$generation state=(Spawn|Chase) health=13 nativeFSM=True" -or $ns -notmatch "NS_SUMMON_HUD_VISIBILITY encounter=$encounter geometry=\d+ target=$entity activation=$generation visible=True hurtActive=True hp=13 "){continue}
  if($hk -match "HK_SUMMON_HIT entity=$entity generation=$generation kind=(Melee|Talisman|Bow) before=13 after=0 sequence=\d+ encounter=$encounter "){$proven["$entity`:$generation`:$encounter"]=$true}
 }
 if($proven.Count -lt 4){throw 'Need repeated native respawns with fresh HP bars and real damage.'}
 & "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
 Write-Output "PASS (scoped): native fly reuse/damage/bar generations=$($proven.Count); other tiers and ordinary-play talisman feel remain separate."
}
Write-Output 'PASS (scoped): native water floor0.30 below artwork, two exact arena wall climbs/ceilings/jump-away exits and position restoration. Native victory/return is checked separately.'
