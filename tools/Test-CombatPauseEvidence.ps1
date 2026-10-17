param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'SESSION_TERMINAL.*phase=(Preparing|Active|Restoring|Victory|Defeated)|_FAILED(?:\s|$)'){throw 'Pause fixture had a gameplay failure.'}
if($hk -match 'SESSION_TERMINAL'){throw 'Host pause fixture terminated unexpectedly.'}
foreach($terminal in [regex]::Matches($ns,'(?m)^(\S+) SESSION_TERMINAL reason=(\S+)[^\r\n]+')) {
    $exit=[regex]::Match($hk,'(?m)^(\S+) SMOKE_EXIT_REQUEST game=HollowKnight');
    $done=[regex]::Match($hk,'(?m)^(\S+) HK_COMBAT_PAUSE_RETURNED nativeCancellation=True pendingOutcomes=0');
    if(-not $exit.Success -or -not $done.Success -or $terminal.Groups[2].Value -notin @('VISUAL_CHANNEL_STOPPED','PEER_DISCONNECTED') -or $terminal.Value -notmatch 'phase=Ready ' -or [DateTimeOffset]::Parse($terminal.Groups[1].Value) -lt [DateTimeOffset]::Parse($exit.Groups[1].Value) -or [DateTimeOffset]::Parse($exit.Groups[1].Value) -le [DateTimeOffset]::Parse($done.Groups[1].Value)){throw 'Peer failed before completed pause cancellation/host shutdown.'}
}
$begins=@([regex]::Matches($hk,'(?m)^([^ ]+) HK_COMBAT_PAUSE_BEGIN stage=(\d+) encounter=([\w-]+) geometry=(\d+) nativeProduced=True frame=(\d+)'))
$ends=@([regex]::Matches($ns,'NS_PAUSE_GRAPH_END geometry=(\d+) samples=(\d+) seconds=([\d.E+-]+) graphChanges=(\d+) pixelChanges=(\d+) effects=(\d+) arrows=(\d+)'))
if($begins.Count -ne 3 -or $ends.Count -ne 3 -or $hk -notmatch 'HK_COMBAT_PAUSE_COMPLETE nativeToggle=True'){throw 'Missing melee/bow/talisman pause windows or completed native resume.'}
$resumes=@([regex]::Matches($ns,'NS_PAUSE_GRAPH_RESUMED geometry=(\d+) samples=(\d+) changes=(\d+)'))
if($resumes.Count -ne 3 -or $hk -notmatch 'HK_COMBAT_PAUSE_RETURNED nativeCancellation=True pendingOutcomes=0'){throw 'Missing native effect progress after resume or drained cancellation.'}
for($i=0;$i -lt 3;$i++){
    $b=$begins[$i];$e=$ends[$i]
    if([int]$b.Groups[2].Value -ne $i -or $b.Groups[4].Value -ne $e.Groups[1].Value){throw 'Pause geometry/stage mismatch.'}
    if([int]$e.Groups[2].Value -lt 30 -or [double]$e.Groups[3].Value -lt 1.5 -or [int]$e.Groups[4].Value -ne 0 -or [int]$e.Groups[5].Value -ne 0 -or [int]$e.Groups[6].Value -lt 1){throw "Native effect graph/pixels did not hold in pause stage${i}: $e"}
    if($i -eq 1 -and [int]$e.Groups[7].Value -lt 1){throw 'Bow pause did not contain an actual in-flight arrow.'}
    if($resumes[$i].Groups[1].Value -ne $b.Groups[4].Value -or [int]$resumes[$i].Groups[2].Value -lt 5 -or [int]$resumes[$i].Groups[3].Value -lt 1){throw 'Native effect graph did not progress after resume.'}
    $r=[regex]::Match($hk,"(?m)^([^ ]+) HK_COMBAT_PAUSE_RESUME stage=$i frame=\d+")
    if(-not $r.Success){throw 'Missing native resume.'}
    $from=[DateTimeOffset]::Parse($b.Groups[1].Value);$until=[DateTimeOffset]::Parse($r.Groups[1].Value)
    foreach($hit in [regex]::Matches($hk,'(?m)^([^ ]+) (?:HK_NATIVE_HIT[^\r\n]*applied=True|HK_SUMMON_HIT[^\r\n]*before=(\d+) after=(\d+))')){
        $at=[DateTimeOffset]::Parse($hit.Groups[1].Value)
        if($at -ge $from.AddMilliseconds(100) -and $at -lt $until -and ($hit.Value.Contains('applied=True') -or [int]$hit.Groups[3].Value -lt [int]$hit.Groups[2].Value)){throw 'Translated damage settled during stable native pause.'}
    }
}
Write-Output 'PASS: native melee, in-flight bow and talisman pause windows hold renderer identities/animation/particles/pixels with no translated damage; three native resumes. Other defense/focus combinations remain separate.'
