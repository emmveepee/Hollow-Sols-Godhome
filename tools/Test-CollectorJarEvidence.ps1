param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$breaks=@([regex]::Matches($hk,'HK_JAR_NATIVE_BREAK action=(\d+) root=(-?\d+) generation=(\d+) parried=(True|False) contentsNull=(True|False) nativeEnemySpawns=(\d+) nativeCoroutine=True'))
$setters=@([regex]::Matches($hk,'HK_JAR_PARRY_CONTENTS_CLEARED action=(\d+) root=(-?\d+) generation=(\d+) nativeSetter=True nativeFallContinues=True contentsNull=True'))
$starts=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing'))
$proven=0;$baseline=0
foreach($break in $breaks) {
 if($break.Groups[4].Value -eq 'False') {if($break.Groups[5].Value -eq 'False' -and $break.Groups[6].Value -eq '1'){$baseline++};continue}
 if($break.Groups[5].Value -ne 'True' -or $break.Groups[6].Value -ne '0'){throw 'A parried native jar still spawned an enemy or retained its contents.'}
 $action=$break.Groups[1].Value;$jar=$break.Groups[2].Value;$generation=$break.Groups[3].Value
 $start=@($starts|Where-Object {$_.Index -lt $break.Index}|Select-Object -Last 1)
 if($start.Count -ne 1){throw 'Jar break lacks native encounter scope.'}
 $enc=$start[0].Groups[1].Value
 $clear=@($setters|Where-Object {$_.Index -gt $start[0].Index -and $_.Index -lt $break.Index -and $_.Groups[1].Value -eq $action -and $_.Groups[2].Value -eq $jar -and $_.Groups[3].Value -eq $generation})
 if($clear.Count -ne 1){throw 'Native jar break lacks exactly one generation-matched contents-clearing receipt.'}
 if($ns -notmatch "NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=(?:parry|counter)\b"){throw 'Jar contents were cleared without an actual native defense verdict.'}
 $proven++
}
if($proven -lt 2 -or !$baseline){throw 'Repeated parried no-spawn jars and ordinary spawning baseline were not exercised.'}
Write-Output "PASS (scoped): native parried jar breaks=$proven enemySpawns=0 ordinary native spawning baselines=$baseline; exact encounter/root/generation, actual defense and native contents setter/coroutine. Late unacknowledged receipts, all tiers and other summons remain separate."
