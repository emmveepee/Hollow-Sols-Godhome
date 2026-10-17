param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Grimm'){throw 'Wrong encounter.'}
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Integration failure; retain evidence.'}
if($hk -notmatch 'HK_HEALTH_NOTCHES .*name=Grimm Boss max=1000 thresholds=750,500,250 .*nativeComparisons=True' -or $ns -notmatch 'NS_PHASE_NOTCH_VIEW .*thresholds=750,500,250 '){throw 'Authored regular Grimm notches/view missing.'}
if($hk -notmatch 'HK_NATIVE_CIRCLE_VERIFIED .*nativeBounds=True' -or $hk -notmatch 'HK_BALLOON_EXPORT_CULL .*above=[1-9]\d* ceiling=[\d.]+ .*wholeBounds=True margin=True upperCutoff=True nativeLifetime=True'){throw 'Native circle/upper balloon pruning missing.'}
if($hk -notmatch 'HK_LOCAL_AURA_STYLE .*crimsonBlackRed=True crimsonSize=1.12 sharedAllBosses=True legacyCrimson=False'){throw 'Shared crimson standard missing.'}
if($ns -notmatch 'NS_GRIMM_GRANT nativeHealth=True addedImmunity=False'){throw 'Test refill could mask damage.'}
$damage=0;$facings=@{}
foreach($impact in [regex]::Matches($hk,'HK_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) nativeImpact=True root=Grimm Firebat\(Clone\) state=Impact siblingRetired=True')){
 $id=$impact.Groups[1].Value;$activation=$impact.Groups[2].Value
 $v=[regex]::Match($ns,"NS_VOLUME_VERDICT encounter=\S+ collider=$id activation=$activation action=\d+ kind=damage [^\r\n]*incomingDirection=(-?[\d.]+) facing=(Left|Right) playerX=(-?[\d.]+) dealerX=(-?[\d.]+)")
 if(!$v.Success){continue}
 $incoming=[double]$v.Groups[1].Value;$facing=$v.Groups[2].Value
 $expected=if($incoming -lt 0){'Right'}elseif($incoming -gt 0){'Left'}elseif([double]$v.Groups[4].Value -gt [double]$v.Groups[3].Value){'Right'}else{'Left'}
 if($facing -ne $expected){throw 'Actual bat damage faces away from its incoming source.'}
 if($ns -notmatch "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=damage reflected=False"){throw 'Damage lacks native projectile retirement.'}
 $damage++;$facings[$facing]=$true
}
if($damage -lt 2 -or $facings.Count -lt 2){throw "Need actual bat damage/impact facing both directions: hits=$damage sides=$($facings.Count)."}
foreach($sign in @('-1','1')){if($ns -notmatch "NS_ATTACK_POLICY .*traits=.*Push.* direction=$sign knockback=80 "){throw "Missing outward balloon push $sign."}}
Write-Output "PASS (scoped): regular Grimm native bat damage/impact facing both sides ($damage hits), authored notches, circle bounds/upper culling, outward push80 and shared crimson standard. Does not certify every tier, human appearance or displayed144FPS."
