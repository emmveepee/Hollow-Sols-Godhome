param([Parameter(Mandatory)][Guid]$Session, [switch]$CompletedBeforeHostExit)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
if(($ns+$hk) -match '_FAILED(?:\s|$)' -or $hk -match 'SESSION_TERMINAL'){throw 'Native wall presentation session failed.'}
$hostExit=[regex]::Match($hk,'(?m)^(\S+) SMOKE_EXIT_REQUEST game=HollowKnight frame=')
foreach($terminal in [regex]::Matches($ns,'(?m)^(\S+) SESSION_TERMINAL reason=([^ ]+)[^\r\n]*phase=([^ ]+)')) {
 if(-not $CompletedBeforeHostExit -or -not $hostExit.Success -or $terminal.Groups[2].Value -notin @('VISUAL_CHANNEL_STOPPED','PEER_DISCONNECTED') -or $terminal.Groups[3].Value -ne 'Ready' -or [DateTime]::Parse($terminal.Groups[1].Value) -lt [DateTime]::Parse($hostExit.Groups[1].Value)) {throw 'Unexpected terminal failure; completed-work scope requires a Ready peer exit after the recorded host timer.'}
}
foreach($pattern in @('NS_REPOSITORY_DONOR scene=A10_S1_TombEntrance_remake .*levelsBlocked=\d+','NS_REPOSITORY_TEMPLATE kind=HallClimbTrim .*inactive=True','NS_REPOSITORY_READY .*donorsLoaded=False','NS_WALL_ART_CREATED .*scene=YiGodhomeCanvas nativeArt=True visualOnly=True','NS_WALL_ART_CAPTURE .*opaque=True blankInteriorRows=[012] cachedStatic=True','NS_WALL_ART_RETIRED activeLeases=0','NS_REPOSITORY_DISPOSED .*leases=0')){
 if($ns -notmatch $pattern){throw "Missing native trim evidence: $pattern"}
}
$projection=[regex]::Match($hk,'HK_WALL_ART_PRESENTED geometry=(\d+) nativeFrame=(\d+) hash=(\d+) sequence=(\d+) strips=4 nativePixels=True caps=Sliced width=0.64')
if(-not $projection.Success -or $ns -notmatch ('WALL_ART_SENT sequence='+$projection.Groups[4].Value+' frame='+$projection.Groups[2].Value+' hash='+$projection.Groups[3].Value+' bytes=524288 geometryFrame='+$projection.Groups[1].Value)){throw 'Exact native atlas/host upload identity unproven.'}
if($hk -notmatch 'HK_WALL_ART_DISPOSED surfaces=4'){throw 'Host trim owners were not disposed.'}
foreach($image in @("$root/.local/runs/NineSols/$Session/native-wall-atlas.png","$root/.local/runs/HollowKnight/$Session/native-wall-trim.png")){if(-not (Test-Path -LiteralPath $image)){throw 'Actual native/host captured image missing.'}}
& "$PSScriptRoot/Test-WallEvidence.ps1" -Session $Session -ClimbOnly
& "$PSScriptRoot/Test-CanvasEvidence.ps1" -Session $Session
Write-Output "PASS: admitted clean native trim, donor unload, connected atlas, exact host pixels, four native climbs and owner cleanup. CompletedBeforeHostExit=$CompletedBeforeHostExit; timer-ended sessions do not prove unlimited health. Full-screen style/fit remains player acceptance."
