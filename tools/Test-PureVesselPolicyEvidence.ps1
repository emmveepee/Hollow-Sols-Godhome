param([Parameter(Mandatory)][Guid]$Session,
      [ValidateSet('Cue','Slash','Explosion','Slam')][string[]]$Categories=@('Cue','Slash','Explosion','Slam'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$hostLines=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log"
$native=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log"
$paired=0
$cueMeasurements=[Collections.Generic.List[object]]::new()
foreach($line in $native) {
 if($line -notmatch '^(\S+) NS_DASH_CUE_COMPLETE action=(\d+) encounter=(\S+) animationProgress=([\d.E+-]+) nativeReturn=(True|False) frame='){continue}
 $complete=[DateTime]$Matches[1]; $action=$Matches[2]; $encounter=$Matches[3]; if([double]$Matches[4] -lt 1){throw 'Unfinished native animation claimed complete'}
 $start=@($native | Where-Object {$_ -match "NS_DASH_CUE_TIMING action=$action encounter=$encounter " -and [DateTime](($_ -split ' ')[0]) -lt $complete}) | Select-Object -Last 1
 if($null -eq $start){continue}
 $dash=@($hostLines | Where-Object {$_ -match "HK_PV_DASH_START action=$action encounter=$encounter " -and [DateTime](($_ -split ' ')[0]) -gt [DateTime](($start -split ' ')[0])}) | Select-Object -First 1
 if($null -eq $start -or $null -eq $dash){continue}
 $lead=([DateTime](($dash -split ' ')[0])-$complete).TotalSeconds
 # Native animation scheduling and diagnostic PNG capture can extend wall time.
 # The gameplay requirement is actual completion BEFORE this native dash;
 # preserve/report wall time rather than assuming a 100ms target is a deadline.
 if($lead -le 0){throw 'Dash preceded completed accelerated cue'}
 $cueMeasurements.Add(@{encounter=$encounter;action=$action;wallSeconds=($complete-[DateTime](($start -split ' ')[0])).TotalSeconds;leadSeconds=$lead})
 $paired++
}
if($Categories -contains 'Cue' -and $paired -eq 0){throw 'No actual native cue completion before an authored dash exercised.'}
$policies=@($native | Where-Object {$_ -match 'NS_ATTACK_POLICY .*traits=LargeParryPush.*Push(Right|Left)'})
if($Categories -contains 'Slash' -and $policies.Count -eq 0){throw 'No facing-directed native slash policy exercised.'}
foreach($line in $policies){if(($line -match 'PushRight' -and $line -notmatch 'direction=1 ') -or ($line -match 'PushLeft' -and $line -notmatch 'direction=-1 ')){throw 'Native push direction disagrees with host facing'}}
$blasts=0
foreach($line in $hostLines){
 if($line -notmatch 'HK_POOLED_ATTACK id=(-?\d+) activation=(\d+) active=True category=Melee name=(Focus Hit|hero_damager) action=(\d+)'){continue}
 $id=$Matches[1];$activation=$Matches[2];$action=$Matches[4]
 if($native -match "NS_ATTACK_POLICY .*id=$id activation=$activation action=$action traits=Crimson parriable=False "){$blasts++}
}
if($Categories -contains 'Explosion' -and $blasts -eq 0){throw 'No actual white explosion reached a non-parriable crimson native dealer.'}
if($Categories -contains 'Slam' -and -not ($hostLines -match 'HK_PV_SLAM_BODY .*damaging=True')){throw 'No descending native body bounds exercised.'}
$partial=[bool](($hostLines+$native) -match 'SESSION_TERMINAL.*phase=Active')
@{session=$Session;checkedCategories=$Categories;activeTerminal=$partial;completedCuesBeforeDash=$paired;cueMeasurements=@($cueMeasurements);nativeSlashPolicies=$policies.Count;crimsonBlasts=$blasts;scope='Only requested categories established. Actual cue animation completion before native dash, native facing/push configuration, observed white explosion defense flags and descending geometry. Does not prove unbounded counter outcomes, every collision, session completion or normal combo placement.'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$root/.local/input-tests/$Session/pv-policy.json"
Write-Output "PASS categories=$($Categories -join ','): observed $paired completed cues, $($policies.Count) directed slash policies, $blasts crimson explosion activations. Active-terminal/partial=$partial; human defense/feel remains separate."
