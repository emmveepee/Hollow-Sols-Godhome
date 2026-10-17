param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session" -Lines
if(($hk+$ns)-match '(?:CONTACT|HIT|NS_INPUT|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Integration failure.'}
# Join two-second host geometry windows to whole-game windows by timestamp.
# Logging windows are excluded, with a conservative two-second boundary margin.
$logging=@();$started=$null
foreach($line in $hk){
 if($line -match '^(\S+) DETAIL_REQUEST active=True '){$started=[datetime]$Matches[1]}
 if($line -match '^(\S+) DETAIL_REQUEST active=False ' -and $started){$logging+=,@($started.AddSeconds(-2),([datetime]$Matches[1]).AddSeconds(2));$started=$null}
}
if($started){throw 'Capture still active; use closed evidence.'}
$dense=@()
foreach($line in $hk){
 if($line -match '^(\S+) HK_BALLOON_EXPORT_CULL frames=(\d+) examined=(\d+)'){
  if([int]$Matches[2] -gt 0 -and [double]$Matches[3]/[int]$Matches[2] -ge 30){$dense += [datetime]$Matches[1]}
 }
 if($line -match '^(\S+) HK_WORK_COST stage=contacts samples=(\d+).*units=(\d+)'){
  $at=[datetime]$Matches[1]
  if([double]$Matches[3]/[int]$Matches[2] -ge 30){$dense+=$at}
 }
}
$budget=1000/144
$failures=@();$counts=@{}
foreach($role in 'HollowKnight','NineSols'){
 $lines=if($role -eq 'HollowKnight'){$hk}else{$ns}
 $windows=@()
 foreach($line in $lines){
  if($line -notmatch '^(\S+) FRAME_PERF scene=(GG_Grimm_Nightmare|YiGodhomeCanvas) frames=(\d+) meanMs=([\d.]+) p95Ms=([\d.]+) maxMs=([\d.]+)'){continue}
  $at=[datetime]$Matches[1];$mean=[double]$Matches[4];$p95=[double]$Matches[5];$max=[double]$Matches[6]
  if(!($dense | Where-Object { [math]::Abs(($at-$_).TotalSeconds) -le 1 })){continue}
  if($logging | Where-Object {$at -ge $_[0] -and $at -le $_[1]}){continue}
  $windows+=,[pscustomobject]@{time=$at;mean=$mean;p95=$p95;max=$max}
  # Small timestamp/vblank jitter tolerance; an extra missed refresh fails.
  if($mean -gt $budget+.02 -or $p95 -gt $budget+.1){$failures+="$role $($at.ToString('O')) meanMs=$mean p95Ms=$p95 maxMs=$max"}
 }
 $counts[$role]=$windows.Count
 if($windows.Count -lt 5){throw "Insufficient dense logging-off whole-frame windows: $role"}
 Write-Output "$role dense logging-off windows=$($windows.Count) maximumMeanMs=$(($windows.mean|Measure-Object -Maximum).Maximum) maximumP95Ms=$(($windows.p95|Measure-Object -Maximum).Maximum) maximumFrameMs=$(($windows.max|Measure-Object -Maximum).Maximum) targetMs=$budget"
}
if($failures.Count){$failures|Select-Object -First 12|Write-Output;throw "144-Hz sustained frame budget rejected in $($failures.Count) dense windows. No performance acceptance."}
Write-Output 'PASS (scoped): both whole-game dense logging-off windows meet the 144-Hz mean/p95 budget with .02/.1 ms sampling tolerances. This is not an all-frame or hardware guarantee.'
