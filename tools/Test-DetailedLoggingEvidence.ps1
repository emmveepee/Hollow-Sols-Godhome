param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$protocol=[regex]::Match((Get-Content -LiteralPath "$root/src/Bridge/Protocol.cs" -Raw),'const ushort Version = (\d+);').Groups[1].Value
if(!$protocol){throw 'Missing current protocol identity.'}
$identity=$null
$stops=@()
foreach($game in @('HollowKnight','NineSols')) {
 $run=Join-Path $root ".local/runs/$game/$Session"
 $files=@(Get-ChildItem -LiteralPath $run -Filter 'detail-*.log' -File)
 if($files.Count -ne 1){throw "Expected one focused capture in $game, found $($files.Count)."}
 $file=$files[0];if($file.Length -gt 8MB){throw 'Capture exceeded byte budget.'}
 $text=Get-Content -LiteralPath $file.FullName -Raw
 $begin=[regex]::Match($text,"DETAIL_BEGIN id=([\w-]+) session=([\w-]+) role=(\w+) protocol=$protocol(?:\s|$)")
 if(!$begin.Success -or $begin.Groups[2].Value -ne "$Session" -or $begin.Groups[3].Value -ne $game){throw 'Missing exact capture/build/session identity.'}
 $id=$begin.Groups[1].Value
 if($identity -and $id -ne $identity){throw 'Games recorded different capture IDs.'};$identity=$id
 $stop=[regex]::Match($text,"DETAIL_END id=$id reason=(manual-or-peer-stop|size-limit|peer-capture-ended) bytes=(\d+)")
 if(!$stop.Success -or $text -notmatch 'DETAIL_SAMPLE' -or $text -notmatch 'DETAIL_POSE' -or $text -notmatch 'deployment='){throw 'Incomplete native capture, stop or context.'}
 $stops+=$stop.Groups[1].Value
 if($game -eq 'HollowKnight' -and (($text -notmatch 'DETAIL_VOLUME' -and $text -notmatch 'DETAIL_PRIMARY_ATTACK collider=-?[1-9]\d*') -or $text -notmatch 'DETAIL_NATIVE_TARGET')){throw 'No actual translated attack/native receiver snapshot.'}
 if($game -eq 'NineSols' -and $text -notmatch 'DETAIL_YI state='){throw 'No actual native Yi state.'}
}
if(($stops -contains 'size-limit' -or $stops -contains 'peer-capture-ended') -and (($stops | Where-Object {$_ -eq 'size-limit'}).Count -ne 1 -or ($stops | Where-Object {$_ -eq 'peer-capture-ended'}).Count -ne 1)){throw 'Unpaired capture-cap shutdown.'}
Write-Output "PASS (scoped): session=$Session paired capture=$identity exact build/session, native state/pose/collision records, explicit start/stop and8MiB budget. Does not certify physical F9 operation, every attack family or diagnostic performance neutrality."
