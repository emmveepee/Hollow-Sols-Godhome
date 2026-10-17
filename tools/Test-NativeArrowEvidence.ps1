param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if(($ns+$hk)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL|NS_PROJECTILE_BLOCKER)_FAILED'){throw 'Native arrow runtime failure; partial markers cannot pass.'}
foreach($type in 1..3){if($ns -notmatch "NS_ARROW_TRIAL .*weapon=\(Weapon\)$type .*nativeEquip=True"){throw "Native arrow choice $type was not exercised."}}
if($ns -match 'NS_PROJECTILE_BLOCKED .*weapon=\(Weapon\)1_'){throw 'Cloud Piercer was incorrectly blocked.'}
if($ns -notmatch 'NS_PROJECTILE_SHIELD_IMPACT .*weapon=\(Weapon\)2_[^\r\n]+nativeSolidEvent=True'){throw 'No Thunder Buster flight impact; explosion-only markers are insufficient.'}
if($ns -notmatch 'NS_PROJECTILE_BLOCKED .*prefab=Yee_Chasing_Arrow[^\r\n]+weapon=\(Weapon\)3_[^\r\n]+nativePoolReturn=True active=False'){throw 'No Shadow Hunter retirement.'}
$connected=$false
foreach($m in [regex]::Matches($ns,'NS_NATIVE_ARROW_OUTCOME encounter=([\w-]+) entity=(-?\d+) activation=(\d+) dealer=-?\d+ weapon=\(Weapon\)1_[^ ]+ damage=([\d.E+-]+) nativeFrame=(\d+)')){
 $enc=$m.Groups[1].Value;$entity=$m.Groups[2].Value;$generation=$m.Groups[3].Value;$frame=$m.Groups[5].Value
 $out=[regex]::Match($ns,"NS_HIT_OUTCOME sequence=(\d+) frame=$frame entity=$entity kind=Bow damage=[\d.E+-]+ encounter=$enc activation=$generation")
 if(!$out.Success){continue};$seq=$out.Groups[1].Value
 $hit=[regex]::Match($hk,"HK_SUMMON_HIT entity=$entity generation=$generation kind=Bow (?:nativeAttackType=Spell )?before=(\d+) after=(\d+) sequence=$seq encounter=$enc")
 $spell=$hk -match "HK_NATIVE_ARROW_ROUTE encounter=$enc entity=$entity activation=$generation sequence=$seq attackType=Spell" -or $hit.Value -match 'nativeAttackType=Spell'
 if($hit.Success -and $spell -and [int]$hit.Groups[2].Value -lt [int]$hit.Groups[1].Value){$connected=$true;break}
}
if(!$connected){throw 'No identified Cloud Piercer native loss -> Bow outcome -> HK spell damage chain.'}
Write-Output "PASS (scoped): session=$Session three native Lv3 choices exercised; Cloud Piercer bypass/spell damage; Thunder Buster native solid-impact dispatch and Shadow Hunter native pool retirement. Does not establish lower upgrade tiers, every direction, explosion timing/damage/tail cleanup, nail-immune windows or normal timing."
