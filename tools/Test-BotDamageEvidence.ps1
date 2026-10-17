param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireUnshieldedHealing,[Guid]$Encounter=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($Encounter -ne [Guid]::Empty){
 $start=[regex]::Match($ns,"ENCOUNTER_STATE role=NineSols encounter=$Encounter geometryFrame=\d+ phase=Restoring")
 if(!$start.Success){throw 'Requested native encounter generation was not recorded.'}
 $ns=$ns.Substring($start.Index)
 $next=[regex]::Match($ns,"ENCOUNTER_STATE role=NineSols encounter=(?!$Encounter)[\w-]+ geometryFrame=\d+ phase=Restoring")
 if($next.Success){$ns=$ns.Substring(0,$next.Index)}
 if($ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$Encounter geometryFrame=\d+ phase=Ready"){throw 'Scoped damage evidence must include the completed native return.'}
}
if(-not(Test-Path "$root/.local/runs/NineSols/$Session/cheat-probe.request")){throw 'Damage oracle requires bounded healing-bot evidence.'}
$damage=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_COMBAT_EVENT n=\d+ frame=(?<frame>\d+) phase=after method=PlayerHealth.ReceiveDamage instance=-?\d+ result=True health=(?<health>[\d.]+)[^\r\n]*'))
$deaths=@($damage | Where-Object { [float]$_.Groups['health'].Value -eq 0 })
foreach($death in $deaths){
 $at=[DateTimeOffset]::Parse($death.Groups['time'].Value)
 $grantsBefore=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_CHEAT_GRANT[^\r\n]*') | Where-Object { [DateTimeOffset]::Parse($_.Groups['time'].Value) -le $at })
 $from=if($grantsBefore.Count){[DateTimeOffset]::Parse($grantsBefore[-1].Groups['time'].Value)}else{$at.AddSeconds(-2)}
 $injuries=@($damage | Where-Object { $time=[DateTimeOffset]::Parse($_.Groups['time'].Value); $time -ge $from -and $time -le $at })
 Write-Output "BOT_DEATH_AUDIT frame=$($death.Groups['frame'].Value) injuriesSinceRefill=$($injuries.Count) elapsedMs=$([Math]::Round(($at-$from).TotalMilliseconds))"
 # Include existing native collider/action verdicts for this bounded refill
 # window. A legitimate multi-swing defeat still needs its lineage reviewed.
 $lineage=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT[^\r\n]*kind=damage[^\r\n]*') | Where-Object { $time=[DateTimeOffset]::Parse($_.Groups['time'].Value); $time -ge $from -and $time -le $at.AddMilliseconds(20) })
 foreach($record in $lineage){Write-Output $record.Value}
}
if($RequireUnshieldedHealing){
 $grants=@([regex]::Matches($ns,'NS_CHEAT_GRANT[^\r\n]*'))
 if(!$grants.Count -or @($grants | Where-Object {$_.Value -notmatch 'addedImmunity=False'}).Count){throw 'Healing diagnostic lacks proof that refills add no immunity.'}
 if(!$damage.Count){throw 'Unshielded healing bot exercised no actual injury; stacked-damage coverage unproven.'}
}
$overlaps=@($damage | Group-Object { $_.Groups['frame'].Value } | Where-Object Count -gt 1)
if($overlaps.Count -gt 0){
 $frames=($overlaps | ForEach-Object { "$($_.Name):$($_.Count) hits" }) -join ', '
 throw "Unexpected simultaneous native player injuries ($frames). Inspect collider/action lineage before accepting this bot run."
}
# A host collider swap may arrive one native frame later, rather than stacking
# in the exact same frame. Preserve nearby lineage even if it was not lethal.
$verdicts=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<collider>-?\d+) activation=(?<activation>\d+) action=(?<action>\d+) kind=damage[^\r\n]*'))
$damageGroups=@{}
foreach($verdict in $verdicts){
 $group=[regex]::Match($verdict.Value,'damageGroup=(\d+)')
 if(!$group.Success -or $group.Groups[1].Value -eq '0'){continue}
 $key=$verdict.Groups['encounter'].Value+':'+$group.Groups[1].Value
 if($damageGroups.ContainsKey($key)){
  $previous=$damageGroups[$key]
  $nativeFrame=[regex]::Match($verdict.Value,'nativeFrame=(\d+)').Groups[1].Value
  $firstFrame=[regex]::Match($previous,'nativeFrame=(\d+)').Groups[1].Value
  $hold=@([regex]::Matches($ns,"NS_VOLLEY_DAMAGE_RETIRED group=$($group.Groups[1].Value) sameNativeFrame=$firstFrame defenseIndependent=True initialHold=1 gameplayTime=([\d.]+)[^\r\n]*") | Where-Object {$_.Index -lt $verdict.Index}|Select-Object -Last 1)
  $repeat=$hold.Count -gt 0 -and $hold[0].Value -match 'repeat=True'
  $rearmName=if($repeat){'NS_RECURRING_GROUP_REARM'}else{'NS_INITIAL_GROUP_REARM'}
  $independent=if($repeat){'False'}else{'True'}
  $timed=if($hold.Count){@([regex]::Matches($ns,"$rearmName collider=-?\d+ activation=\d+ action=\d+ group=$($group.Groups[1].Value) holdUntil=([\d.]+) gameplayTime=([\d.]+) nativeFrame=(\d+) independent=$independent") |
    Where-Object { $_.Index -gt $hold[0].Index -and $_.Index -lt $verdict.Index -and [long]$_.Groups[3].Value -le [long]$nativeFrame -and [double]$_.Groups[2].Value -ge [double]$_.Groups[1].Value -and [Math]::Abs([double]$_.Groups[1].Value-[double]$hold[0].Groups[1].Value-1) -lt .002 })}else{@()}
  $clock=[regex]::Match($verdict.Value,'gameplayTime=([\d.]+)')
  $clockExpired=$hold.Count -gt 0 -and $clock.Success -and [double]$clock.Groups[1].Value -ge [double]$hold[0].Groups[1].Value+1
  if(!$timed.Count -and !$clockExpired){throw "One damage group injured Yi again before a proven safety-window rearm: before=$previous after=$($verdict.Value)"}
  if($repeat){$damageGroups[$key]=$verdict.Value}
 }
 if(!$damageGroups.ContainsKey($key)){$damageGroups[$key]=$verdict.Value}
}
$adjacent=0
for($i=1;$i -lt $verdicts.Count;$i++){
 $before=$verdicts[$i-1];$after=$verdicts[$i]
 $elapsed=([DateTimeOffset]::Parse($after.Groups['time'].Value)-[DateTimeOffset]::Parse($before.Groups['time'].Value)).TotalMilliseconds
 if($elapsed -lt 0 -or $elapsed -gt 100 -or $before.Groups['encounter'].Value -ne $after.Groups['encounter'].Value){continue}
 if($adjacent++ -lt 32){Write-Output "BOT_ADJACENT_DAMAGE elapsedMs=$([Math]::Round($elapsed,2)) before=$($before.Value) after=$($after.Value)"}
 if($after.Groups['action'].Value -ne '0' -and $before.Groups['action'].Value -eq $after.Groups['action'].Value){throw 'One logical strike injured Yi in adjacent native frames; inspect collider transition retirement.'}
}
foreach($hit in $damage){
 if([float]$hit.Groups['health'].Value -ne 0){continue}
 $at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
 $near=@($damage | Where-Object { $elapsed=($at-[DateTimeOffset]::Parse($_.Groups['time'].Value)).TotalMilliseconds; $elapsed -ge 0 -and $elapsed -le 100 })
 if($near.Count -gt 1){throw 'Multiple native injuries killed the healing bot within 100ms; investigate the complete dealer timeline.'}
}
Write-Output "PASS (scoped): healing-bot damage oracle encounter=$Encounter receipts=$($damage.Count), simultaneous injuries=0. This does not prove an unexercised attack or exclude legitimate slower defeats."
