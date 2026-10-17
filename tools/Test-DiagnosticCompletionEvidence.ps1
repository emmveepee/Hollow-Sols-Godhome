param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$complete=[regex]::Match($hk,'(?m)^(\S+Z) HK_DIAGNOSTIC_COMPLETE entries=(\d+) returns=(\d+) planned=(\d+) encounter=([\w-]+) nativeReturn=True drained=True')
if(!$complete.Success){throw 'Planned native combat completion was not exercised.'}
$enc=$complete.Groups[5].Value;$planned=[int]$complete.Groups[4].Value
if([int]$complete.Groups[2].Value -ne $planned -or [int]$complete.Groups[3].Value -ne $planned){throw 'Completion count differs from the native plan.'}
if($ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$enc geometryFrame=\d+ phase=Ready"){throw 'Matching peer did not become Ready.'}
$at=[datetime]::Parse($complete.Groups[1].Value)
foreach($game in @('HollowKnight','NineSols')){
 $text=if($game -eq 'HollowKnight'){$hk}else{$ns}
 $exit=[regex]::Match($text,"(?m)^(\S+Z) SMOKE_EXIT_REQUEST game=$game reason=diagnostic-complete encounter=$enc elapsedMs=(\d+) frame=\d+")
 if(!$exit.Success){throw 'Paired native graceful completion exit is missing.'}
 $seconds=([datetime]::Parse($exit.Groups[1].Value)-$at).TotalSeconds
 if($seconds -lt 0 -or $seconds -gt 2){throw 'Completion did not exit within the two-second handshake budget.'}
 $signal=if($game -eq 'HollowKnight'){'diagnostic-complete.request'}else{'diagnostic-complete.ack'}
 if((Get-Content "$root/.local/runs/$game/$Session/$signal" -Raw).Trim() -ne "$Session|$enc"){throw 'Completion token has a stale/foreign session or encounter.'}
 $limit=[int](Get-Content "$root/.local/runs/$game/$Session/run.json" -Raw|ConvertFrom-Json).seconds
 $saved=[math]::Round($limit-[double]$exit.Groups[2].Value/1000,2)
 if($saved -le 0){throw 'Completion did not beat the diagnostic timeout.'}
 Write-Output "PASS: $game native planned=$planned elapsedMs=$($exit.Groups[2].Value) completionToExitSeconds=$seconds savedSeconds=$saved exact session/encounter token."
}
