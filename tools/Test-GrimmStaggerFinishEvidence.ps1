param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireFireHud)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$transfers=@($hk | Where-Object {$_ -match 'HK_ACCUMULATOR_LETHAL_IMMEDIATE .*health=-?\d+ nativeDeath=True damageReplayed=False'})
if(!$transfers.Count){throw 'No actual lethal native accumulator transfer; exact finisher remains unverified.'}
foreach($transfer in $transfers) {
    if($transfer -notmatch '^(\S+) .*entity=(-?\d+) receiver=-?\d+ before=\d+ debit=\d+ health=(-?\d+).*frame=(\d+)'){throw 'Malformed transfer.'}
    $time=[DateTime]$Matches[1]; $entity=$Matches[2]; $frame=[int]$Matches[4]
    if([int]$Matches[3] -gt 0){throw 'Positive health incorrectly relayed.'}
    $hits=@($hk | Where-Object {$_ -match "HK_SUMMON_HIT entity=$entity .*kind=Talisman .*after=0 .*frame=$frame$"})
    if(!$hits.Count){throw 'Lethal transfer lacks a recent actual talisman acknowledgement.'}
    $death=@($hk | Where-Object {$_ -match "HK_GRIMM_DEATH_REVEALED entity=$entity .*visible=True nativeDeathState=True" -and [DateTime]($_.Split(' ')[0]) -ge $time -and [DateTime]($_.Split(' ')[0]) -lt $time.AddSeconds(2)})
    $victory=@($hk | Where-Object {$_ -match 'HK_HUB_COMBAT_ENDED victory=True' -and [DateTime]($_.Split(' ')[0]) -ge $time -and [DateTime]($_.Split(' ')[0]) -lt $time.AddSeconds(15)})
    if(!$death.Count -or !$victory.Count){throw 'Lethal transfer did not reach visible native death and native encounter victory.'}
    if($death[0] -notmatch 'frame=(\d+)$' -or [int]$Matches[1] -gt $frame+1){throw 'Visible death was delayed beyond the lethal frame.'}
    if(!($hk -match "HK_GRIMM_DEATH_BATS_RETIRED entity=$entity fakeBats=[1-9]\d* realBatActive=False exactNativeOwners=True frame=$frame$")){throw 'Native stagger bats did not retire in the lethal frame.'}
    if(!($hk -match "HK_GRIMM_DEATH_STAGGER_AUDIO_STOPPED entity=$entity nativeOwners=[1-9]\d* exactCallBatsActions=True frame=$frame$")){throw 'Native stagger audio did not stop in the lethal frame.'}
}
if($RequireFireHud -and $ns -notmatch 'NS_FIRE_HUD_OPACITY_BOUND nativeGlow=-?[1-9]\d* gain=4 exactPath=Damage_DOT_Fire/Red_Glow material=\S+ tintProperty=_Color nativeUnchanged=True'){throw 'Exact native glow private-material tint owner missing.'}
Write-Output "PASS (scoped): session=$Session actual talisman and terminal native debit in the same frame, visible native death within one frame and native victory. Private fire tint required=$RequireFireHud; does not certify HUD appearance, whole-session completion or144 FPS."
