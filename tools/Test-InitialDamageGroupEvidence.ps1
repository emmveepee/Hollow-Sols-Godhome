param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireLaterInjury)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$holds=@([regex]::Matches($ns,'NS_VOLLEY_DAMAGE_RETIRED group=(\d+) sameNativeFrame=(\d+) defenseIndependent=True initialHold=1 gameplayTime=([\d.]+) independentAfterHold=True'))
$hits=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=\d+ nativeFrame=(\d+)[^\r\n]* damageGroup=(\d+)'))
$rearms=@([regex]::Matches($ns,'NS_INITIAL_GROUP_REARM collider=(-?\d+) activation=(\d+) action=(\d+) group=(\d+) holdUntil=([\d.]+) gameplayTime=([\d.]+) nativeFrame=(\d+) independent=True'))
if(!$holds.Count -or !$rearms.Count){throw 'Actual native initial injury hold and survivor rearming are unexercised.'}
$audited=0;$survivors=0;$later=0;$unproven=0
foreach($hold in $holds){
 $group=$hold.Groups[1].Value;$firstFrame=[long]$hold.Groups[2].Value
 $start=[double]::Parse($hold.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
 # Rotated logs are merged by timestamp; records sharing one timestamp can
 # sort lexically. The process-native frame and group identify the receipt.
 $first=@($hits|Where-Object {$_.Groups[5].Value -eq "$firstFrame" -and $_.Groups[6].Value -eq $group})
 if($first.Count -ne 1){throw "Initial damage group=$group did not resolve exactly one native injury before its synchronous hold."}
 $encounter=$first[0].Groups[1].Value
 $next=@($holds|Where-Object {$_.Index -gt $hold.Index -and $_.Groups[1].Value -eq $group}|Select-Object -First 1)
 $endIndex=if($next.Count){$next[0].Index}else{$ns.Length}
 $resumed=@($rearms|Where-Object {$_.Index -gt $hold.Index -and $_.Index -lt $endIndex -and $_.Groups[4].Value -eq $group})
 if(!$resumed.Count){continue} # No survivor is required when native shots have already expired.
 if($hk -notmatch "HK_(?:NATIVE_VOLLEY_GROUP|HIVE_NATIVE_VOLLEY) group=$group\b" -and $hk -notmatch "HK_VESSEL_SLAM_PROJECTILE[^\r\n]* group=$group\b[^\r\n]*initialHold=1 independentAfterHold=True"){$unproven++;continue} # Bounded HK owner traces can cap before later volleys; never count those as verified.
 $firstRearmFrame=($resumed|ForEach-Object {[long]$_.Groups[7].Value}|Measure-Object -Minimum).Minimum
 $groupHits=@($hits|Where-Object {$_.Groups[1].Value -eq $encounter -and $_.Groups[6].Value -eq $group})
 if(@($groupHits|Where-Object {[long]$_.Groups[5].Value -gt $firstFrame -and [long]$_.Groups[5].Value -lt $firstRearmFrame}).Count){throw "A surviving initial group=$group injured Yi before its native rearming."}
 foreach($resume in $resumed){
  $until=[double]::Parse($resume.Groups[5].Value,[Globalization.CultureInfo]::InvariantCulture)
  $now=[double]::Parse($resume.Groups[6].Value,[Globalization.CultureInfo]::InvariantCulture)
  if([Math]::Abs($until-$start-1) -gt .002 -or $now+.0001 -lt $until){throw "Initial group=$group did not honor one paused gameplay second."}
  if([long]$resume.Groups[2].Value -le 0){throw 'Rearmed survivor has no independent native collider activation.'}
  $survivors++
  $later+=@($groupHits|Where-Object {$_.Groups[2].Value -eq $resume.Groups[1].Value -and $_.Groups[3].Value -eq $resume.Groups[2].Value -and $_.Groups[4].Value -eq $resume.Groups[3].Value -and [long]$_.Groups[5].Value -ge [long]$resume.Groups[7].Value}).Count
 }
 if(@($resumed|Group-Object {if($_.Groups[3].Value -eq '0'){"collider:$($_.Groups[1].Value):$($_.Groups[2].Value)"}else{"action:$($_.Groups[3].Value)"}}|Where-Object {$_.Count -gt 1}).Count){throw 'Distinct surviving projectiles shared a subsequent strike identity.'}
 $audited++
}
if(!$audited -or !$survivors -or ($RequireLaterInjury -and !$later)){throw 'Native initial groups, independent survivors or required later actual injury were not exercised.'}
if($hk -notmatch 'HK_VESSEL_SLAM_PROJECTILE[^\r\n]*initialHold=1 independentAfterHold=True' -and $hk -notmatch 'HK_NATIVE_VOLLEY_GROUP|HK_HIVE_NATIVE_VOLLEY'){throw 'Native authored projectile group provenance is missing.'}
Write-Output "PASS (scoped): initial injury groups=$audited independently rearmed survivors=$survivors later actual survivor injuries=$later owner-provenance-unobserved groups=$unproven; one paused gameplay second, exact collider/activation and native damage verdicts. Unobserved owners, other contacts and unexercised groups remain separate."
