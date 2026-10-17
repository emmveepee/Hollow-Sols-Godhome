param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$root=Split-Path $PSScriptRoot -Parent
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ticks=[regex]::Matches($ns,'NS_BURN_NATIVE_DOT amount=18 healthBefore=([\d.]+) health=([\d.]+)') | Where-Object { [Math]::Abs([double]$_.Groups[1].Value-[double]$_.Groups[2].Value-18) -lt .001 }
if($ticks.Count -lt 3 -or $ns -notmatch 'NS_BURN_NATIVE_APPLIED .*new=False tick=18 masks=2' -or $ns -notmatch 'NS_BURN_NATIVE_EXPIRED .*damageRestored=True'){throw 'Native burn dose/refresh/pool restoration not verified.'}
foreach($sign in @('-1','1')) { if($ns -notmatch "NS_ATTACK_POLICY .*traits=.*Push.* direction=$sign knockback=80 "){throw "Missing reduced outward balloon push direction $sign."} }
if($ns -notmatch 'NS_FIRE_HUD_BOUND .*layer=' -or $ns -notmatch 'NS_FIRE_HUD_CAPTURE fullScreen=True' -or $ns -match 'NS_FIRE_HUD_UNAVAILABLE'){throw 'Native fire HUD binding/full-screen capture missing.'}
if($ns -notmatch 'NS_HUD_ASYNC_VERIFIED channel=player_hud exact=True'){throw 'Native HUD readback comparison missing.'}
if($hk -notmatch 'HK_GRIMM_DEATH_REVEALED .*visible=True nativeDeathState=True state=Death_Start'){throw 'Native death visibility missing.'}
Write-Output "PASS (scoped): session=$Session native burn ticks18 refresh/restore, balloon push80 in both directions, authored fire HUD/full-screen/async pixels, native Death Start visibility. Does not certify exact hidden-talisman finishing hit, full session, tier sweep, perceived HUD appearance or frame-budget neutrality."
