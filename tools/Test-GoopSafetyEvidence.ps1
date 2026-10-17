param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireParry)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$holds=@([regex]::Matches($ns,'NS_VOLLEY_DAMAGE_RETIRED group=(\d+) sameNativeFrame=(\d+) defenseIndependent=True initialHold=1 gameplayTime=([\d.]+) independentAfterHold=False repeat=True'))
$rearms=@([regex]::Matches($ns,'NS_RECURRING_GROUP_REARM collider=(-?\d+) activation=(\d+) action=(\d+) group=(\d+) holdUntil=([\d.]+) gameplayTime=([\d.]+) nativeFrame=(\d+) independent=False encounter=([\w-]+)'))
$hits=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=\d+ nativeFrame=(\d+)[^\r\n]* damageGroup=(\d+)[^\r\n]*'))
if(!$holds.Count -or !$rearms.Count){throw 'Recurring native goop injuries and safety-window expiry were not exercised.'}
$audited=0;$repeated=0
foreach($hold in $holds){
 $group=$hold.Groups[1].Value;$frame=[long]$hold.Groups[2].Value
 $start=[double]::Parse($hold.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
 $first=@($hits|Where-Object {$_.Groups[6].Value -eq $group -and [long]$_.Groups[5].Value -eq $frame})
 if($first.Count -ne 1){throw "Goop group=$group resolved more than one injury in its initial native frame."}
 $encounter=$first[0].Groups[1].Value
 foreach($hit in $hits){
  if($hit.Groups[1].Value -ne $encounter -or $hit.Groups[6].Value -ne $group -or [long]$hit.Groups[5].Value -le $frame){continue}
  $clock=[regex]::Match($hit.Value,'gameplayTime=([\d.]+)');if(!$clock.Success){throw 'Recurring safety proof requires actual injury gameplay timestamps.'}
  $injuryTime=[double]::Parse($clock.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
  if($injuryTime -ge $start-.0001 -and $injuryTime -lt $start+1-.001){throw 'A native goop injury bypassed the recurring one-second deadline.'}
 }
 $next=@($holds|Where-Object {$_.Index -gt $hold.Index -and $_.Groups[1].Value -eq $group}|Select-Object -First 1)
 $endIndex=if($next.Count){$next[0].Index}else{$ns.Length}
 $resumed=@($rearms|Where-Object {$_.Index -gt $hold.Index -and $_.Index -lt $endIndex -and $_.Groups[4].Value -eq $group -and $_.Groups[8].Value -eq $encounter})
 if(!$resumed.Count){continue} # Native lifetime may end while the shared window is still held.
 $rearmFrame=($resumed|ForEach-Object {[long]$_.Groups[7].Value}|Measure-Object -Minimum).Minimum
 if(@($hits|Where-Object {$_.Groups[1].Value -eq $encounter -and $_.Groups[6].Value -eq $group -and [long]$_.Groups[5].Value -gt $frame -and [long]$_.Groups[5].Value -lt $rearmFrame}).Count){throw 'Goop injured Yi before its shared window expired.'}
 foreach($resume in $resumed){
  $until=[double]::Parse($resume.Groups[5].Value,[Globalization.CultureInfo]::InvariantCulture)
  $now=[double]::Parse($resume.Groups[6].Value,[Globalization.CultureInfo]::InvariantCulture)
  if([Math]::Abs($until-$start-1) -gt .002 -or $now -lt $until -or [long]$resume.Groups[3].Value -le 0){throw 'Goop lost its one-second gameplay deadline or distinct native strike identity.'}
 }
 if($next.Count -and @($hits|Where-Object {$_.Groups[1].Value -eq $encounter -and $_.Groups[6].Value -eq $group -and $_.Groups[5].Value -eq $next[0].Groups[2].Value}).Count){$repeated++}
 $audited++
}
if(!$audited -or !$repeated){throw 'Repeated native goop injury and rearming coverage is missing.'}
if($hk -notmatch 'HK_GOOP_NATIVE_MANIFEST[^\r\n]*nativeLand=True nativeRecycle=True' -or $hk -notmatch 'HK_GOOP_NATIVE_GENERATION[^\r\n]*sharedSeconds=1'){throw 'Pinned native flight/puddle provenance is missing.'}
$parries=@([regex]::Matches($hk,'HK_GOOP_PARRY_RECYCLED action=(\d+)[^\r\n]*puddleActive=False nativeRecycle=True landSkipped=True'))
foreach($parry in $parries){if($ns -notmatch "NS_NATIVE_PROJECTILE_PARRY_NOTIFY action=$($parry.Groups[1].Value) collider=-?\d+ nativeFrame=\d+ damage=0"){throw 'Native goop recycling lacks an actual Nine Sols ordinary-parry receipt.'}}
if($RequireParry -and !$parries.Count){throw 'Actual airborne goop parry/recycle was not exercised.'}
Write-Output "PASS (scoped): native goop windows=$audited repeated injuries=$repeated ordinary flight parries=$($parries.Count); paused gameplay deadlines and native flight/puddle/recycle ownership. Other attacks and tiers remain separate."
