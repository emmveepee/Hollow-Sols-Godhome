param([Parameter(Mandatory)][Guid]$Session,[Parameter(Mandatory)][ValidateSet('MantisLords','SistersOfBattle','WatcherKnights')][string]$Boss)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne $Boss){throw 'Kick evidence belongs to a different boss.'}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native kick integration failed.'}
$success=@([regex]::Matches($ns,'NS_TAICHI_NATIVE attack=(\d+) weakness=True bounce=True frame=(\d+)'))
if($success.Count -lt 2){throw 'Need at least two actual native kicks, not fixture placement markers.'}
$state=if($Boss -eq 'WatcherKnights'){'Charge_Recover'}else{'Dash_Recover'}
foreach($hit in $success){
  $action=$hit.Groups[1].Value
  if($hk -notmatch "HK_ROSTER_KICK_RECOVERY entity=-?\d+ action=$action state=$state "){
    $late=[regex]::Match($hk,"HK_RETIRED_DEFENSE_REFUSED encounter=([\w-]+) entity=(-?\d+) activation=(\d+) sequence=(\d+) kind=TaiChiKick action=$action replay=False nativeTransitionPreserved=True")
    if(-not $late.Success){throw "Missing exact native recovery or retired-body refusal for action $action"}
    $enc=$late.Groups[1].Value; $entity=$late.Groups[2].Value; $activation=$late.Groups[3].Value; $sequence=$late.Groups[4].Value
    if($hk -notmatch "HK_ROSTER_NATIVE_DEATH entity=$entity activation=$activation frame=\d+ encounter=$enc" -or
      $ns -notmatch "NS_HOST_HIT_RESULT sequence=$sequence result=Refused health=0"){throw "Retired-body kick lacks exact death/ack evidence for action $action"}
  }
  if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=\d+ damaging=False"){throw "Missing immediate collision retirement for action $action"}
}
Write-Output "PASS (scoped): $Boss session=$Session nativeKicks=$($success.Count), exact recovery and collision retirement. Fixture placements/grants and a timer-ended session do not establish normal timing, every body or complete fight coverage."
