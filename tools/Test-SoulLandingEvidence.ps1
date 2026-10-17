param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Not a native Soul boss capture.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$bindings=@([regex]::Matches($hk,'HK_SOUL_FINAL_LANDING_BOUND entity=(-?\d+) dealer=(-?\d+) collider=(-?\d+) body=(-?\d+) retainedDiveStrike=True'))
if(!$bindings.Count){throw 'Missing exact native final landing/body ownership binding.'}
$ids=@{};$activations=0;$paired=0;$physical=@{}
foreach($binding in $bindings){
 $landing=$binding.Groups[3].Value;$body=$binding.Groups[4].Value
 # Tyrant has two native dealers on one collider: count physical activations once.
 $shapeKey=$landing+':'+$body
 if($physical.ContainsKey($shapeKey)){continue}
 $physical[$shapeKey]=$true
 $ids[$landing]=$true;$ids[$body]=$true
 $seen=@{};$authored=@{};$entity=$binding.Groups[1].Value
 foreach($line in $hk -split "`n"){
  $window=[regex]::Match($line,"HK_ROSTER_ATTACK_WINDOW entity=$entity state=Quake_(?:Antic|Down) action=(\d+) kick=False crimson=True frame=\d+")
  if($window.Success){$authored[$window.Groups[1].Value]=$true}
  $v=[regex]::Match($line,'HK_POOLED_ATTACK id=(-?\d+) activation=\d+ active=True category=\w+ name=.+ action=(\d+) frame=\d+')
  if(!$v.Success){continue}
  $id=$v.Groups[1].Value;$action=$v.Groups[2].Value
  if($id -eq $body -and [long]$action -gt 0){$seen[$action]=$true}
  if($id -eq $landing){
   if([long]$action -le 0 -or !$authored.ContainsKey($action)){throw 'Landing is active without the preceding authored native dive strike.'}
   # The native phase entrance has a landing but no enabled body dealer.
   # Require repeated actual enabled-body pairs separately; never count entrance as one.
   if($seen.ContainsKey($action)){$paired++}
   $activations++
  }
 }
}
if($paired -lt 3){throw 'Need at least three actual enabled-body dive-to-landing activations.'}
$injuries=@{}
foreach($v in [regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=\d+ action=(\d+) kind=damage ')){
 if(!$ids.ContainsKey($v.Groups[2].Value)){continue}
 if([long]$v.Groups[3].Value -le 0){throw 'A native dive/landing injury has no shared strike identity.'}
 $key=$v.Groups[1].Value+':'+$v.Groups[3].Value
 if($injuries.ContainsKey($key)){throw 'Native dive and landing settled more than one injury for the same strike.'}
 $injuries[$key]=$true
}
if(!$injuries.Count){throw 'No actual native dive/landing injury exercised.'}
Write-Output "PASS (scoped): Soul final landing activations=$activations enabled body pairs=$paired actual injury strikes=$($injuries.Count), no duplicate injury per native dive. Other attacks, tiers and pixels remain separate."
