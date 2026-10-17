param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($ns -match '_FAILED(?:\s|$)|SESSION_TERMINAL'){throw 'Isolated Jade comparison failed.'}
$trials=@([regex]::Matches($ns,'NS_JADE_TRIAL_PASS trial=(\d+) precise=(True|False) ricochet=(True|False) reflect=(True|False) nativeCollision=True'))
if($trials.Count -ne 4 -or $ns -notmatch 'NS_JADE_COMPARISON_COMPLETE trials=4 equipmentRestored=True saveIO=False'){throw 'Four actual native parries and equipment restoration required.'}
for($i=0;$i -lt 4;$i++){
    $t=$trials[$i];$precise=$i%2 -eq 0;$equipped=$i -ge 2;$reflect=$precise -or $equipped
    if([int]$t.Groups[1].Value -ne $i -or $t.Groups[2].Value -ne "$precise" -or $t.Groups[3].Value -ne "$equipped" -or $t.Groups[4].Value -ne "$reflect"){throw 'Native Ricochet comparison mismatch.'}
}
Write-Output "PASS (scoped): $Session four native collision parries, precise/imprecise with Ricochet off/on, equipment restored, no save I/O. Projectile travel and other Jades remain separate checks."
