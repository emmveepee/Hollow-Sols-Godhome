param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
$entries=[regex]::Matches($hk,'(?m)^(\S+) HK_ARENA_GEOMETRY_EARLY geometry=(\d+) transitioned=False')
if($entries.Count -lt 2){throw 'First and repeat arena entries required.'}
$reports=foreach($entry in $entries){
 $geometry=$entry.Groups[2].Value
 $image=[regex]::Match($hk,"(?m)^(\S+) HK_ARENA_IMAGE_READY geometry=$geometry .*opaquePixels=True")
 if(-not $image.Success){throw "Current native image missing for geometry$geometry"}
 $start=[DateTimeOffset]::Parse($entry.Groups[1].Value); $first=[DateTimeOffset]::Parse($image.Groups[1].Value)
 $latency=($first-$start).TotalMilliseconds
 if($latency -lt 0 -or $latency -gt 250){throw "Arena first image delay$latency ms exceeds250ms for geometry$geometry"}
 $fadeEnd=@([regex]::Matches($hk,'(?m)^(\S+) HK_NATIVE_BOSS_FADE_EVENT [^\r\n]*fsm=Transitions state=In event=FINISHED ') |
  Where-Object {[DateTimeOffset]::Parse($_.Groups[1].Value) -ge $start} | Select-Object -First 1)
 if($fadeEnd.Count -ne 1 -or $first -ge [DateTimeOffset]::Parse($fadeEnd[0].Groups[1].Value)){throw 'Native Yi image must precede actual boss transition In/FINISHED, not merely HasTransitionedIn.'}
 $queue=[regex]::Match($ns,"(?m)^(\S+) NS_CONTENT_AFTER_IMAGE scene=\S+ geometry=$geometry ")
 if(-not $queue.Success){throw 'This acquisition continuity oracle requires a queued native content entry.'}
 $ready=@([regex]::Matches($ns,"(?m)^(\S+) NS_CONTENT_GEOMETRY_READY geometry=$geometry "))
 if($ready.Count -ne 1){throw 'Native content readiness missing after current image.'}
 $until=$first.AddSeconds(3)
 foreach($hidden in [regex]::Matches($hk,'(?m)^(\S+) HK_VISUAL_HIDDEN reason=stale-or-mismatched-frame')){
  $at=[DateTimeOffset]::Parse($hidden.Groups[1].Value)
  if($at -gt $first -and $at -lt $until){throw "Yi image hidden within first3seconds geometry$geometry at$at"}
 }
 $samples=[regex]::Matches($ns,"NS_ENTRY_PIXELS geometry=$geometry [^\r\n]+")
 if($samples.Count -lt 5 -or @($samples | Where-Object {$_.Value -notmatch 'renderer=True active=True .*opaque=True'}).Count){throw 'Native entry opacity/renderer samples absent or failed.'}
 $fade=[regex]::Matches($hk,"HK_ARENA_FADE_SAMPLE geometry=$geometry [^\r\n]+")
 if($fade.Count -lt 5 -or @($fade | Where-Object {$_.Value -match 'imageReady=False'}).Count){throw 'Continuous image during native fade not established.'}
 [ordered]@{geometry=$geometry;firstImageMs=$latency;beforeNativeFadeEndMs=([DateTimeOffset]::Parse($fadeEnd[0].Groups[1].Value)-$first).TotalMilliseconds;nativeSamples=$samples.Count;fadeSamples=$fade.Count;contentReadyUtc=$ready[0].Groups[1].Value}
}
$destination="$root/.local/input-tests/$Session/arena-entry.json"
$reports | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $destination
Write-Output "PASS: $($entries.Count) current native arena images within250ms and before actual native boss fade completion, native opacity and no stale hiding in first3seconds. Human reveal and other bosses remain separate; this does not certify invulnerability effects."
