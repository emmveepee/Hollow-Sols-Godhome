param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireWalls)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Uumuu'){throw 'Wrong encounter.'}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|NS_VISUAL|HK_HUB)_FAILED|NS_WALL_FIXTURE_FAILED|CONTACT_STOP reason=(InvalidDataException|InvalidOperationException)'){throw 'Native Uumuu integration failed.'}
$circles=@([regex]::Matches($hk,'HK_UUMUU_REPEL_GEOMETRY encounter=([\w-]+) entity=(-?\d+) activation=(\d+) radius=([\d.E+-]+) offset=\([^)]+\) width=([\d.E+-]+) height=([\d.E+-]+) spriteWidth=([\d.E+-]+) spriteHeight=([\d.E+-]+) authoredCircle=True'))
if(!$circles.Count){throw 'No actual authored shell geometry.'}
foreach($circle in $circles){
 if([Math]::Abs([double]$circle.Groups[4].Value-2.94) -gt .001 -or [double]$circle.Groups[5].Value -ge [double]$circle.Groups[7].Value -or [double]$circle.Groups[6].Value -ge [double]$circle.Groups[8].Value){throw 'Bounce geometry was not reduced from the live sprite rectangle.'}
}
if($ns -notmatch 'NS_UUMUU_BODY_REPEL[^\r\n]*nativeKnockback=True'){throw 'Native bounce response not exercised.'}
$shown=0;$hidden=0
foreach($target in [regex]::Matches($hk,'HK_ROSTER_TARGET entity=(-?\d+) activation=(\d+) name=(Jellyfish GG|Lil Jellyfish)\(Clone\) max=1 shape=\w+')){
 $entity=$target.Groups[1].Value;$generation=$target.Groups[2].Value
 if($target.Groups[3].Value -eq 'Lil Jellyfish'){
  if($ns -notmatch "NS_SUMMON_BOUND entity=$entity generation=$generation[^\r\n]* hiddenHealthBar=True nativeHealthWidget=False"){throw 'Orange core health widget suppression unproven.'}
  if($ns -match "NS_SUMMON_HUD_VISIBILITY[^\r\n]* target=$entity activation=$generation visible=True"){throw 'Orange core rendered a health bar.'}
  $hidden++
 }elseif($ns -match "NS_SUMMON_HUD_VISIBILITY[^\r\n]* target=$entity activation=$generation visible=True hurtActive=True hp=1 " -and $hk -match "HK_SUMMON_HIT entity=$entity generation=$generation kind=(Melee|Talisman|Bow) before=1 after=0 " -and $ns -match "NS_SUMMON_RETIRED[^\r\n]* entity=$entity generation=$generation "){$shown++}
}
if($shown -lt 2 -or $hidden -lt 2){throw 'Need repeated real initial Ooma damage/bar retirement and orange cores without widgets.'}
$water=[regex]::Match($hk,'HK_HUB_SHALLOW_WATER scene=GG_Uumuu left=[\d.E+-]+ right=[\d.E+-]+ surface=([\d.E+-]+) floor=([\d.E+-]+) depth=([\d.E+-]+)')
if($water.Success -and ([Math]::Abs(([double]$water.Groups[1].Value-[double]$water.Groups[2].Value)-.30) -gt .001 -or [double]$water.Groups[3].Value -ne .30)){throw 'Water depth differs from the Godhome reference.'}
if(!$water.Success -and $hk -notmatch 'NS_GEOMETRY_IMPORTED[^\r\n]*scene=GG_Uumuu_V' -and $ns -notmatch 'NS_GEOMETRY_IMPORTED[^\r\n]*scene=GG_Uumuu_V'){throw 'No native water floor or alternate dry arena.'}
if($RequireWalls){
 for($index=0;$index -lt 2;$index++){
  if($ns -notmatch "NS_WALL_FIXTURE_PASS index=$index nativeWallRun=True[^\r\n]*finalState=Normal[^\r\n]*scene=GG_Uumuu(?:_V)? "){throw "Native wall$index climb/exit not exercised."}
 }
 if($ns -notmatch 'NS_WALL_FIXTURE_COMPLETE surfaces=2 originalPositionRestored=True scene=GG_Uumuu' -or $hk -notmatch 'HK_WALL_ART_PRESENTED[^\r\n]* strips=2 nativePixels=True'){throw 'Native wall art and restored position not proven.'}
}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
Write-Output "PASS (scoped): Uumuu authored smaller shell, initial Ooma bars/damage/retirement=$shown, orange cores without health widgets=$hidden, native water/dry-arena policy. Walls required=$RequireWalls. Victory and long-session repetition remain separate."
