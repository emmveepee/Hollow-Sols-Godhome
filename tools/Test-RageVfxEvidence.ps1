param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireCounter)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'NS_MEMORY_FAILED|CONTACT_FAILED|NS_CONTENT_FAILED'){throw 'Rage session has a terminal failure.'}
# Only the fixture's ordered host shutdown may disconnect a peer in Ready.
# Active failures, unrelated terminal reasons and early disconnects remain failures.
$exit=[regex]::Match($hk,'(?m)^(\S+) SMOKE_EXIT_REQUEST game=HollowKnight frame=\d+')
foreach($terminal in [regex]::Matches($hk+$ns,'(?m)^(\S+) SESSION_TERMINAL ([^\r\n]+)')) {
 if(-not $exit.Success -or $terminal.Groups[2].Value -notmatch 'reason=(VISUAL_CHANNEL_STOPPED|RAGE_MASK_CHANNEL_STOPPED|PEER_DISCONNECTED) .*phase=Ready ' -or [DateTimeOffset]::Parse($terminal.Groups[1].Value) -lt [DateTimeOffset]::Parse($exit.Groups[1].Value)){throw 'Rage session has a terminal gameplay failure.'}
}
if([regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count -lt 2){throw 'Two complete native return boundaries required.'}
$begins=[regex]::Matches($hk,'HK_RAGE_VISUAL_BEGIN encounter=(\S+) sequence=(\d+) chargeSeconds=(\S+) frame=(\d+)')
if($begins.Count -lt 2){throw 'At least two real native rage sequences required.'}
foreach($begin in $begins){
 $enc=$begin.Groups[1].Value;$seq=$begin.Groups[2].Value
 if($hk -notmatch "HK_RAGE_MASK_STYLE nativeBodyPreserved=True outlineOnly=True hiddenOwners=0"){throw 'Native-body continuity/outline-only contract missing.'}
 $start=[regex]::Matches($ns,"NS_RAGE_VISUAL_BEGIN encounter=$enc sequence=$seq action=\d+ warningOwner=(-?\d+) nativeOutline=True frame=\d+")
 if($start.Count -ne 1){throw "Initial warning repeated or absent: $enc/$seq"}
 $binding=[regex]::Matches($ns,"NS_RAGE_OUTLINE_BOUND encounter=$enc owner=(-?\d+) geometry=(\d+) sequence=$seq maskFrame=\d+")
 if($binding.Count -ne 1){throw "Native outline never bound its exact current mask: $enc/$seq"}
 $owner=$binding[0].Groups[1].Value
 if($ns -notmatch "NS_RAGE_OUTLINE_RETIRED owner=$owner sequence=\d+ owners=0 textures=0"){throw 'Native outline owner retained after its sequence.'}
 $release=[regex]::Matches($ns,"NS_RAGE_VISUAL_RELEASE encounter=$enc sequence=$seq action=\d+ frame=\d+")
 $end=[regex]::Matches($ns,"NS_RAGE_VISUAL_END encounter=$enc sequence=$seq reason=\S+ contentLeases=0 outlineOwners=\d+ frame=\d+")
 if($release.Count -ne 1 -or $end.Count -ne 1 -or $release[0].Index -le $start[0].Index -or $end[0].Index -le $release[0].Index){throw 'Release/retirement absent, repeated or out of order.'}
 $strikes=[regex]::Matches($ns,"NS_RAGE_VISUAL_STRIKE encounter=$enc sequence=$seq action=(\d+) released=True frame=\d+")
 if(@($strikes | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique).Count -lt 2){throw 'Released artwork did not survive multiple native swing identities.'}
 foreach($strike in $strikes){if($strike.Index -lt $release[0].Index -or $strike.Index -gt $end[0].Index){throw 'Released strike lies outside visual lease lifetime.'}}
}
if($ns -match 'NS_DASH_CUE_TIMING|NS_CRIMSON_WARNING attack='){throw 'Rage restarted old per-strike warnings.'}
$counters=[regex]::Matches($hk,'HK_MACE_COUNTER encounter=(\S+) action=(\d+) state=Rage interrupted=False frame=\d+')
if($RequireCounter -and $counters.Count -eq 0){throw 'No actual native unbound defense exercised.'}
foreach($counter in $counters){
 $enc=$counter.Groups[1].Value;$action=[int]$counter.Groups[2].Value;$next=$action+1
 if($ns -notmatch "NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=$enc " -or $hk -notmatch "HK_POOLED_ATTACK[^\r\n]*active=True[^\r\n]*name=Hitter action=$next "){throw 'Counter lacks native verdict or a subsequent damaging swing.'}
}
$kinds=@([regex]::Matches($ns,'NS_REPOSITORY_TEMPLATE kind=(\S+) owner=-?\d+ inactive=True') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
if('JiequanWarning' -notin $kinds -or @($kinds | Where-Object {$_ -notin @('JiequanWarning','HallClimbTrim')}).Count -gt 0){throw 'Unexpected or missing admitted rage templates.'}
# Hall now admits its independent native trim before a statue challenge. Count
# the exact admitted kinds, retaining the zero-lease disposal requirement.
if($ns -notmatch ('NS_REPOSITORY_DISPOSED templates='+$kinds.Count+' leases=0')){throw 'Rage repository leases retained or template count mismatched at teardown.'}
if([regex]::Matches($hk,'HK_RAGE_MASK_RETIRED cameras=0 parts=0 textures=0').Count -lt 2){throw 'Two encounter-owned mask teardown boundaries required.'}
if($hk -notmatch 'HK_RAGE_MASK [^\r\n]*bytes=4194304 '){throw 'Final 1024-square silhouette quality gate absent.'}
$nativePictures=@(1..16 | Where-Object {Test-Path -LiteralPath "$root/.local/runs/NineSols/$Session/outline-atlas-$_.png"})
$hostPictures=@(1..16 | Where-Object {Test-Path -LiteralPath "$root/.local/runs/HollowKnight/$Session/outline-live-$_.png"})
if($nativePictures.Count -eq 0 -or $hostPictures.Count -eq 0 -or $hk -notmatch 'HK_RAGE_REPROJECT'){throw 'Missing native outline atlas/live reprojected evidence.'}
Write-Output "PASS: $($begins.Count) native sequences, one initial warning each, sustained native outline over current host silhouettes across strikes, $($counters.Count) native counter continuations and zero leases. Visual fit and normal timing require player acceptance."
