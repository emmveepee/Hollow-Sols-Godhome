param([Parameter(Mandatory)][Guid]$Session,[switch]$ProjectilesOnly,[switch]$RequireLaterSecondShield,[ValidateRange(0,10000)][long]$Action=0)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL|NS_PROJECTILE_BLOCKER)_FAILED'){throw 'Markoth shield native integration failed.'}
if($ns -notmatch 'NS_PROJECTILE_BLOCKED .*nativePoolReturn=True active=False'){throw 'No actual native arrow pool retirement at a shield.'}
if($ProjectilesOnly){Write-Output "PASS (scoped): native Markoth shield blocks a player-owned arrow and returns it to its native pool. No counter, second-shield, all-arrow-type or reflected-blocking claim.";return}
$dispels=@([regex]::Matches($hk,'HK_MARKOTH_SHIELD_DISPEL action=(\d+) shields=([12]) stunSeconds=3 damaging=False frame=(\d+)'))
if($Action){$dispels=@($dispels | Where-Object {[long]$_.Groups[1].Value -eq $Action})}
if($dispels.Count -lt 1){throw 'No native unbound shield counter/dispel.'}
foreach($d in $dispels){$a=$d.Groups[1].Value;$n=$d.Groups[2].Value
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$a sameNativeFrame=\d+ damaging=False"){throw 'No immediate native collision retirement.'}
 if($hk -notmatch "HK_MARKOTH_SHIELD_RESPAWN action=$a shields=$n nativeState=Tween_Out_Init telegraphSeconds=1 damaging=False"){throw 'Respawn count/native telegraph mismatch.'}
 if($hk -notmatch "HK_MARKOTH_SHIELD_READY action=$a damaging=True"){throw 'Shield damage did not resume after the native telegraph.'}
}
if($RequireLaterSecondShield){
 if($Action -or $ProjectilesOnly){throw 'Later second-shield coverage requires the complete counter capture.'}
 $first=@($dispels | Where-Object {$_.Groups[2].Value -eq '1'}) | Select-Object -First 1
 $later=@($dispels | Where-Object {$_.Groups[2].Value -eq '2' -and $first -and $_.Index -gt $first.Index}) | Select-Object -First 1
 if(-not $first -or -not $later){throw 'No one-shield counter followed by a later two-shield counter.'}
 Write-Output 'PASS (scoped): later native second shield remains available after an earlier one-shield dispel; both subsequent shields dispel and respawn. Follow motion and normal timing still require separate verification.'
}
Write-Output "PASS (scoped): Markoth session=$Session selectedAction=$Action arrow blocking, native unbound dispels=$($dispels.Count), same-count respawn and telegraph. Does not establish all arrow types, follow motion, pause, reflected blocking or normal timing."
