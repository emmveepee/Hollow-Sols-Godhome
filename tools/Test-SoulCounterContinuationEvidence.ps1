param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireFinal)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Wrong Soul family.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
if($hk -match 'HK_SOUL_PHASE_ONE_COUNTER|HK_SOUL_DIVE_PATTERN_ENDED|HK_SOUL_COUNTER_STREAK|HK_SOUL_DEFLATED_STAGE'){throw 'Retired custom Soul stagger behavior remains active.'}
$counters=@([regex]::Matches($hk,'(?m)^(\S+Z) HK_SOUL_DIVE_COUNTER entity=(-?\d+) action=(\d+) interrupted=False nativeSequenceContinues=True nativeState=(Quake_Down|Quake_Land|Quake_Waves|Re_Pos) final=(True|False) frame=(\d+)'))
$first=0;$final=0;$continued=0
foreach($counter in $counters){
 $at=[datetime]::Parse($counter.Groups[1].Value);$entity=$counter.Groups[2].Value;$action=$counter.Groups[3].Value;$frame=[long]$counter.Groups[6].Value
 $acks=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($acks.Count -ne 1){throw 'Counter continuation lacks exact native acknowledgement.'}
 $seq=$acks[0].Groups[2].Value
 $sent=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_COUNTER_SUBMITTED sequence=$seq attack=$action\b")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 $verdict=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=[\w-]+ collider=-?\d+ activation=\d+")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($sent.Count -ne 1 -or $verdict.Count -ne 1){throw 'Counter continuation lacks actual Yi Unbound success.'}
 if($counter.Groups[5].Value -eq 'True'){$final++}else{$first++}
 $later=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_ROSTER_ATTACK_WINDOW entity=$entity state=\S+ action=(\d+) kick=(?:True|False) crimson=(?:True|False) frame=(\d+)")|Where-Object {
  # Native first-phase recovery can exceed ten seconds (Tyrant observed 12.9).
  # This checks bounded continuation, not a customized attack cadence.
  $time=[datetime]::Parse($_.Groups[1].Value);$time -gt $at -and $time -le $at.AddSeconds(30) -and [long]$_.Groups[3].Value -gt $frame -and $_.Groups[2].Value -ne $action
 })
 if($later.Count){$continued++}
}
if($first -lt 2 -or $continued -lt 2 -or ($RequireFinal -and $final -lt 3)){throw "Repeated native continuation not exercised: first=$first final=$final continued=$continued"}
Write-Output "PASS (scoped): $boss actual first-phase unbounds=$first final-phase unbounds=$final later native attacks=$continued, no custom counter stagger. Native victory/other tiers remain separate."
