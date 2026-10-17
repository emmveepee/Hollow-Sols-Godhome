param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'Xero'){throw 'Xero evidence required.'}
$hk=Get-BoundedRuntimeLog $run;$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL|HK_TARGET_FIXTURE)_FAILED'){throw 'Xero integration failure.'}
$hits=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_REFLECTED_HIT id=(?<id>-?\d+) activation=(?<activation>\d+) before=(?<before>\d+) after=(?<after>\d+) collision=True'))
$successful=0;$rearmed=0
foreach($hit in $hits){
 $id=$hit.Groups['id'].Value;$activation=$hit.Groups['activation'].Value;$at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
 $parries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=parry reflected=True") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -le $at -and ($at-$t).TotalSeconds -lt 3.5})
 if($parries.Count -ne 1){throw 'Reflected hit has no unique actual native parry.'}
 if($hk -notmatch "HK_PROJECTILE_REFLECTED id=$id activation=$activation speed=[\d.]+ nativeBody=True damage=21"){throw 'Native rigidbody redirection missing.'}
 $before=[int]$hit.Groups['before'].Value;$delta=$before-[int]$hit.Groups['after'].Value
 if($delta -eq 0){continue} # Native immunity is authoritative; never replay it.
 if($delta -ne [Math]::Min(21,$before)){throw 'Unexpected reflected native health delta.'}
 $successful++
 $returns=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_XERO_RECOVERY_COMPLETE id=$id activation=$activation state=(?:Home|Antic Spin|Antic Point) [^\r\n]*nativeRunning=True") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $at -and ($t-$at).TotalSeconds -lt 5})
 if(!$returns.Count -and [int]$hit.Groups['after'].Value -gt 0){throw 'Live sword failed its native return.'}
 if(!$returns.Count){continue}
 $returned=[DateTimeOffset]::Parse($returns[0].Groups['time'].Value)
 $injuries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=[\w-]+ collider=$id activation=$activation action=0 kind=damage") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge [DateTimeOffset]::Parse($parries[0].Groups['time'].Value) -and $t -le $returned})
 if($injuries.Count){throw 'Reflected/recovering sword also injured Yi.'}
 $next=[long]$activation+1
 if($hk -match "HK_ATTACK_VOLUME id=$id activation=$next active=True category=Projectile name=[^\r\n]+ state=Shoot"){$rearmed++}
}
if($successful -lt 4 -or $rearmed -lt 2){throw "Insufficient repeated reflection/return/rearm: successful=$successful rearmed=$rearmed."}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$victories=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=(?<encounter>[\w-]+) geometryFrame=\d+ phase=Victory'))
if($victories.Count -lt 2){throw 'Need two native Xero victories and visible returns.'}
foreach($victory in $victories){
 $encounter=$victory.Groups['encounter'].Value
 if($ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$encounter geometryFrame=\d+ phase=Ready"){throw 'Victory lacks paired native return readiness.'}
}
if(@([regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True')).Count -lt 2){throw 'Missing repeated visible native returns.'}
Write-Output "PASS (scoped): Xero actual native reflected hits=$successful, later native Shoot activations=$rearmed; native return and harmless recovery verified. Other tiers and human presentation remain separate."
