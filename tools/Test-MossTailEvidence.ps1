param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'MassiveMossCharger'){throw 'Moss evidence required.'}
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$tails=@([regex]::Matches($hk,'HK_POOLED_ATTACK id=(?<id>-?\d+) activation=(?<activation>\d+) active=True category=Melee name=(Crouch Hit|Burrow Hit) action=(?<action>\d+) frame=\d+'))
if($tails.Count -lt 2){throw 'Need repeated actual native submerge shape activations.'}
if(@($tails | Where-Object {$_.Groups['action'].Value -eq '0'}).Count){throw 'Native landing/submerge shape lost its originating strike identity.'}
$hits=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=-?\d+ activation=\d+ action=(?<action>\d+) kind=damage[^\r\n]*'))
$groups=@($hits | Where-Object {$_.Groups['action'].Value -ne '0'} | Group-Object {"$($_.Groups['encounter'].Value)/$($_.Groups['action'].Value)"})
foreach($group in $groups){
 $previous=$null
 foreach($hit in $group.Group){
  $at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
  if($null -ne $previous -and ($at-$previous).TotalSeconds -lt .98){throw 'Moss originating strike and landing tail caused adjacent injuries before one-second retirement.'}
  $previous=$at
 }
}
if($groups.Count -lt 2){throw 'Need repeated actual unshielded originating-strike injuries.'}
Write-Output "PASS (scoped): Moss native landing/submerge shapes=$($tails.Count) retain positive originating actions, damaging actions=$($groups.Count), no adjacent same-strike damage. Counter-triggered harmlessness is assessed separately."
