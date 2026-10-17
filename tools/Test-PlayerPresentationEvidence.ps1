[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [switch]$SummonLease, [switch]$VictoryHold)
$ErrorActionPreference='Stop'
if(-not $SummonLease -and -not $VictoryHold){throw 'Select an evidence category.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '_FAILED(?:\s|$)' -or $hk -match 'SESSION_TERMINAL'){throw 'Presentation session had a runtime failure.'}
if($SummonLease) {
 $states=@{}
 $hidden=0; $awake=0
 foreach($m in [regex]::Matches($ns,'NS_SUMMON_HUD_VISIBILITY encounter=([\w-]+) geometry=(\d+) target=(-?\d+) activation=(\d+) visible=(True|False) hurtActive=(True|False) hp=(\d+)')) {
  $key="$($m.Groups[1].Value)/$($m.Groups[3].Value)/$($m.Groups[4].Value)"
  if($m.Groups[5].Value -eq 'True'){$states[$key]=$true; $awake++}
  elseif([int]$m.Groups[7].Value -gt 0){if($states[$key]){throw 'Live awakened target bar blinked off.'};$hidden++}
 }
 if($hidden -lt 1 -or $awake -lt 1 -or $ns -notmatch 'NS_SUMMON_HUD_LEASE_IMMUNITY[^\r\n]+visible=True hurtActive=False'){throw 'Missing dormant/awake/immunity separation.'}
 if($ns -notmatch 'NS_SUMMON_HUD_SCALE[^\r\n]+worldWidth=36 ' -or $hk -notmatch 'SUMMON_UI_RECEIVED[^\r\n]+bytes=4194304'){throw 'Larger native arc not captured/transferred.'}
 if(-not (Test-Path -LiteralPath "$root/.local/runs/NineSols/$Session/native-summon-hud.png")){throw 'Actual native arc image absent.'}
}
if($VictoryHold) {
 $holds=[regex]::Matches($hk,'HK_VICTORY_IMAGE_VISIBLE scene=([^ ]+) geometry=(\d+) nativeFrame=(\d+) enabled=True active=True sprite=True opaque=True onScreen=True sequence=(\d+)')
 if($holds.Count -eq 0){throw 'No visible opaque victory hold.'}
 foreach($hold in $holds){
  $geometry=$hold.Groups[2].Value
  if($hk -notmatch "ENCOUNTER_STATE role=HollowKnight encounter=[\w-]+ geometryFrame=$geometry phase=Victory"){throw 'Image hold has no matching native victory generation.'}
 }
}
# A timed end may leave a later challenge Active. This proves only the selected
# presentation categories, never a full combat or unlimited-session pass.
foreach($m in [regex]::Matches($ns,'SESSION_TERMINAL reason=([^ ]+)')) {
 if($m.Groups[1].Value -notin @('VISUAL_CHANNEL_STOPPED','PEER_DISCONNECTED') -or -not $hk.Contains('SMOKE_EXIT_REQUEST game=HollowKnight')){throw 'Unexpected peer terminal.'}
}
Write-Output "PASS ${Session}: scoped presentation summonLease=$SummonLease victoryHold=$VictoryHold. Full combat/timing/controller acceptance is separate."
