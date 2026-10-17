param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
$boss=(Get-Content "$run/encounter-profile.request" -Raw).Trim()
if($boss -notin @('HornetSentinel','Flukemarm','HiveKnight','BrokenVessel','LostKin','Nosk','WingedNosk','Collector','Xero','Galien','Gorb','ElderHu','Sheo')){throw 'Requested boss is outside the explicit notch-removal list.'}
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog $run
if(($ns+$hk) -match '(?:CONTACT|HIT|NS_VISUAL|HK_VISUAL)_FAILED|CONTACT_STOP reason=(?:InvalidDataException|InvalidOperationException)'){throw 'Native no-notch integration failure.'}
$views=@([regex]::Matches($ns,'NS_PHASE_NOTCH_DISABLED encounter=(?<encounter>[\w-]+) entity=(?<entity>-?\d+) activation=(?<activation>\d+) hostHealth=\d+ maximum=(?<maximum>\d+) markers=0 activeMarkers=0 nativeWidget=True frame=\d+') | Where-Object {[int]$_.Groups['maximum'].Value -gt 1})
if(!$views.Count){throw 'No actual live boss widget with zero markers.'}
foreach($view in $views){
 $enc=$view.Groups['encounter'].Value;$entity=$view.Groups['entity'].Value;$activation=$view.Groups['activation'].Value;$max=$view.Groups['maximum'].Value
 $policy=[regex]::Match($ns,"NS_TARGET_HEALTH_POLICY encounter=$enc entity=$entity activation=$activation hostHealth=(?<health>\d+) maximum=$max notchCount=0 injuryLimit=(?<limit>[\d.E+-]+) fullRemainingLimit=(?<remaining>[\d.E+-]+) nativeInjury=[\d.E+-]+")
 if(!$policy.Success -or [int]$policy.Groups['health'].Value -le 0 -or [double]$policy.Groups['limit'].Value -le 0 -or [double]$policy.Groups['limit'].Value -ne [double]$policy.Groups['remaining'].Value){throw 'Boss injury policy does not expose its complete remaining HP.'}
 $values=@([regex]::Matches($ns,"NS_BOSS_HEALTH entity=$entity hostHp=(?<hp>\d+) max=$max nativeInjury=[\d.E+-]+ hostInjury=[\d.E+-]+ solid=[\d.E+-]+ total=[\d.E+-]+") | Where-Object {[int]$_.Groups['hp'].Value -gt 0 -and [int]$_.Groups['hp'].Value -lt [int]$max})
 if(!$values.Count){throw 'Live normal boss-health projection was not exercised below full HP.'}
}
Write-Output "PASS (scoped): $boss native zero-marker widgets=$($views.Count), empty received notch policy, full remaining-HP injury allowance and actual damaged-health projection. Injury cashout, higher tiers and human appearance remain separate."
