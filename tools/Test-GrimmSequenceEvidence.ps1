param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session" -Lines
$states=@(foreach($line in $hk){
 if($line -match '^(\S+) HK_GRIMM_BAT_STATE encounter=(\S+) entity=-?\d+ .*state=(\w+) .*frame=(\d+)'){
  [pscustomobject]@{Time=[DateTime]$Matches[1];Encounter=$Matches[2];State=$Matches[3];Frame=[long]$Matches[4]}
 }
})
$exits=0;$batWindows=0
for($i=0;$i -lt $states.Count;$i++){
 $s=$states[$i]
 if($s.State -eq 'End_Pause'){
  $next=$states | Select-Object -Skip ($i+1) | Where-Object {$_.Encounter -eq $s.Encounter -and $_.State -notin @('End_Pause','Balloon_End')} | Select-Object -First 1
  if($next){$exits++}
 }
 if($s.State -ne 'Call_Bats'){continue}
 $next=$states | Select-Object -Skip ($i+1) | Where-Object {$_.Encounter -eq $s.Encounter -and $_.State -notin @('Call_Bats','Reforming','Reformed')} | Select-Object -First 1
 if(!$next){continue};$batWindows++
 foreach($line in $ns){
  if($line -match '^(\S+) NS_CRIMSON_SOUND '){
   $at=[DateTime]$Matches[1]
   if($at -gt $s.Time.AddMilliseconds(250) -and $at -lt $next.Time.AddMilliseconds(-250)){throw 'Charge cue during native bat recovery.'}
  }
 }
}
if($exits -lt 2 -or $batWindows -lt 1){throw "Missing repeated native balloon exits/bat recovery: exits=$exits bats=$batWindows"}
$costs=@(foreach($line in $hk){if($line -match 'HK_LOCAL_AURA_COST .*meanMs=([\d.]+) maxMs=([\d.]+)'){[pscustomobject]@{Mean=[double]$Matches[1];Max=[double]$Matches[2]}}})
if($costs.Count -lt 2 -or $costs[-1].Mean -gt 10 -or ($costs | Where-Object {$_.Max -gt 50})){throw 'Aura readback/render cost exceeds focused regression limit.'}
Write-Output "PASS (scoped): session=$Session nativeBalloonExits=$exits batRecoveryWindows=$batWindows noBatChargeSpam=True boundedAuraCost=True. Does not certify full session, all difficulties or human visual acceptance."
