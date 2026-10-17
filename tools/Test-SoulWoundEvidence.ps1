param([Parameter(Mandatory)][Guid]$Session,[ValidateSet('SoulMaster','SoulTyrant','BrokenVessel','LostKin')][string]$Boss='SoulMaster',[switch]$WindowOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native runtime failure invalidates wound evidence.'}
$windows=@([regex]::Matches($hk,'HK_SOUL_WOUND_WINDOW encounter=([\w-]+) entity=(-?\d+) activation=(\d+) collider=(\w+) health=\d+ active=True frame=(\d+)'))
$damaged=$false
$child=if($Boss -in @('BrokenVessel','LostKin')){'Stun_Hitbox'}else{'Wound_Box'}
$body=switch($Boss){'BrokenVessel' {'Infected_Knight'} 'LostKin' {'Lost_Kin'} 'SoulTyrant' {'Dream_Mage_Lord'} default {'Mage_Lord'}}
for($i=0;$i -lt $windows.Count-1;$i++){
 $w=$windows[$i];$next=$windows[$i+1]
 if($w.Groups[4].Value -ne $child -or $next.Groups[4].Value -ne $body){continue}
 $enc=$w.Groups[1].Value;$entity=$w.Groups[2].Value;$activation=$w.Groups[3].Value
 if($next.Groups[1].Value -ne $enc -or $next.Groups[2].Value -ne $entity -or $next.Groups[3].Value -ne $activation){continue}
 foreach($h in [regex]::Matches($hk,"HK_SUMMON_HIT entity=$entity generation=$activation kind=\w+ before=(\d+) after=(\d+) sequence=\d+ encounter=$enc frame=(\d+)")){
  $f=[int]$h.Groups[3].Value
  if($f -gt [int]$w.Groups[5].Value -and $f -lt [int]$next.Groups[5].Value -and [int]$h.Groups[2].Value -lt [int]$h.Groups[1].Value){$damaged=$true}
 }
}
if(!$damaged){throw 'No actual damage strictly inside a wound window followed by same-target recovery.'}
if($ns -notmatch 'NS_TARGET_GEOMETRY_REBOUND'){throw 'No native receiver rebound evidence.'}
if(!$WindowOnly -and $hk -notmatch 'HK_HUB_COMBAT_ENDED victory=True'){throw 'No native victory after wound/recovery.'}
$completion=if($WindowOnly){'window-only; encounter completion is not assessed'}else{'native victory observed'}
Write-Output "PASS (scoped): session=$Session boss=$Boss native stagger-child damage and same-target geometry recovery; $completion. Fixture assistance does not establish human timing, another boss or Phase2 unbound counter behavior."
