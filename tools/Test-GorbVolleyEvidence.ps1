param([Parameter(Mandatory)][Guid]$Session,[switch]$DamageOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'Gorb'){throw 'Gorb evidence required.'}
if(Test-Path "$run/invulnerable-probe.request"){throw 'Invulnerability cannot prove volley damage.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$births=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_NATIVE_VOLLEY_GROUP group=(?<group>\d+) root=Shot_Slug_Spear[^ ]* rootId=(?<root>-?\d+) generation=\d+ frame=(?<frame>\d+) defenseIndependent=True'))
$radial=@($births | Group-Object {$_.Groups['group'].Value+' '+$_.Groups['frame'].Value} | Where-Object Count -eq 8)
if($radial.Count -lt 2){throw 'Need repeated exact eight-projectile native volley lineage.'}
$verdicts=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<collider>-?\d+) activation=(?<activation>\d+) action=0 kind=(?<kind>damage|parry) hostFrame=\d+ nativeFrame=(?<frame>\d+)[^\r\n]* damageGroup=(?<group>[1-9]\d*)'))
$injuries=@($verdicts | Where-Object {$_.Groups['kind'].Value -eq 'damage'})
if($injuries.Count -lt 2){throw 'Need repeated actual grouped volley injuries.'}
foreach($injury in $injuries){
 $group=$injury.Groups['group'].Value;$frame=$injury.Groups['frame'].Value
 if($ns -notmatch "NS_VOLLEY_DAMAGE_RETIRED group=$group sameNativeFrame=$frame defenseIndependent=True"){throw 'Actual injury did not synchronously retire its volley.'}
}
$parries=@($verdicts | Where-Object {$_.Groups['kind'].Value -eq 'parry'})
if($parries.Count -lt 1){throw 'No actual individual projectile defense in a grouped volley.'}
$independent=0
foreach($parry in $parries){
 $group=$parry.Groups['group'].Value;$frame=$parry.Groups['frame'].Value;$at=[DateTimeOffset]::Parse($parry.Groups['time'].Value)
 $sibling=@($verdicts | Where-Object {
  $_.Groups['group'].Value -eq $group -and $_.Groups['encounter'].Value -eq $parry.Groups['encounter'].Value -and $_.Groups['collider'].Value -ne $parry.Groups['collider'].Value -and
  ([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds -gt 0 -and ([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds -lt 3
 })
 if($sibling.Count -gt 0){$independent++}
 if($sibling.Count -eq 0 -and $ns -match "NS_VOLLEY_PARRY_INDEPENDENT encounter=$($parry.Groups['encounter'].Value) group=$group collider=$($parry.Groups['collider'].Value) siblingsDamaging=[1-9]\d* nativeFrame=$frame"){ $independent++ }
}
if(-not $DamageOnly -and $independent -lt 1){throw 'Need an actual later sibling verdict or retained enabled native sibling dealer after individual reflection; independent runtime behavior remains unexercised.'}
Write-Output "PASS (scoped): native radial volleys=$($radial.Count), actual grouped injuries=$($injuries.Count), individual parries=$($parries.Count), live native siblings after parry=$independent; no simultaneous injury. DamageOnly=$DamageOnly."
