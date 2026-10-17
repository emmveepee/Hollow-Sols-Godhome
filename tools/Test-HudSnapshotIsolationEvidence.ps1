param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/NineSols/$Session"
if(!(Test-Path "$run/trace-probe.request") -or (Test-Path "$run/hud-reference-probe.request")){throw 'Ordinary bounded trace without HUD reference capability required.'}
$ns=Get-BoundedRuntimeLog $run
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
& "$PSScriptRoot/Test-DetailedLoggingEvidence.ps1" -Session $Session
if([regex]::Matches($ns,'NS_NATIVE_HURT_HUD source=Health.OnTakeDamageEvent').Count -lt 2){throw 'Repeated actual injury HUD events are unexercised.'}
if($ns -notmatch 'NS_NATIVE_HURT_RENDER nativeMethods=True[^\r\n]*nativeStrength=True dualMatte=True'){throw 'Native damage HUD rendering is unproven.'}
if($ns -match 'NS_NATIVE_DAMAGE_REFERENCE'){throw 'Ordinary trace still performed heavyweight native HUD reference rendering.'}
$exports=@(Get-ChildItem -LiteralPath $run -Filter '*.png' -File | Where-Object {$_.Name -match '^hud-\d+\.png$|^native-(damage|hurt-hud|fire-hud)'})
if($exports.Count){throw 'Ordinary trace still performed heavyweight HUD PNG exports.'}
Write-Output 'PASS (scoped): repeated actual native injury HUD rendering remains active, HUD reference exports absent, both paired captures closed normally. Reference-only capture performance and human hit-time flicker remain separate.'
