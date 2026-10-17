param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -notin @('FalseKnight','FailedChampion')){throw 'Native armor-family evidence required.'}
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL|HK_TARGET_FIXTURE)_FAILED'){throw 'Debris integration failure.'}
$hits=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_REFLECTED_HIT id=(?<id>-?\d+) activation=(?<activation>\d+) before=(?<before>\d+) after=(?<after>\d+) collision=True frame=(?<frame>\d+)'))
$successful=0;$reused=0
foreach($hit in $hits){
 $id=$hit.Groups['id'].Value;$activation=$hit.Groups['activation'].Value;$frame=$hit.Groups['frame'].Value
 $at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
 $parries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=parry reflected=True") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -le $at -and ($at-$t).TotalSeconds -lt 3.5})
 if($parries.Count -ne 1){throw 'Debris collision lacks a unique actual native parry.'}
 $start=[DateTimeOffset]::Parse($parries[0].Groups['time'].Value)
 $flight=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_DEBRIS_NATIVE_FLIGHT id=$id activation=$activation originalGravity=(?<gravity>[\d.]+) counterGravity=[\d.]+ authoredCounterSpeed=(?<speed>[\d.]+) rotationNative=True") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $start.AddMilliseconds(-50) -and $t -le $at})
 if($flight.Count -ne 1){throw 'Missing exact authored debris flight contract.'}
 $speed=$flight[0].Groups['speed'].Value;$gravity=$flight[0].Groups['gravity'].Value
 if($hk -notmatch "HK_PROJECTILE_REFLECTED id=$id activation=$activation speed=$([regex]::Escape($speed)) nativeBody=True damage=21"){throw 'Rebound did not use native authored speed.'}
 if($hk -notmatch "HK_DEBRIS_GRAVITY_RESTORED id=$id activation=$activation gravity=$([regex]::Escape($gravity)) frame=$frame"){throw 'Native gravity was not restored on collision.'}
 if($hk -notmatch "HK_DEBRIS_NATIVE_CLEANUP id=$id activation=$activation state=(?:Break|Pause|Recycle) colliderEnabled=False frame=$frame"){throw 'Reflected debris retained its collider after native cleanup.'}
 if($hk -notmatch "HK_REFLECTED_ARMOR_OBSERVED entity=-?\d+ nativeMinimum=$($hit.Groups['after'].Value) nativeAfter=\d+ nativeSubtracted=(?:True|False) frame=$frame"){throw 'Armor hit lacks pre-reset native observation.'}
 $injuries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=[\w-]+ collider=$id activation=$activation action=0 kind=damage") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $start -and $t -le $at.AddMilliseconds(20)})
 if($injuries.Count){throw 'Reflected debris also injured Yi.'}
 $before=[int]$hit.Groups['before'].Value;$delta=$before-[int]$hit.Groups['after'].Value
 if($delta -eq 0){continue} # Native refusals remain authoritative; never replay.
 if($delta -ne [Math]::Min(21,$before)){throw 'Unexpected reflected native armor damage.'}
 $successful++
 $next=[long]$activation+1
 if($hk -match "HK_POOLED_ATTACK id=$id activation=$next active=True category=Projectile name=Falling Barrel\(Clone\)"){$reused++}
}
if($successful -lt 2 -or $reused -lt 2){throw "Need repeated actual debris hits and fresh pooled activations: successful=$successful reused=$reused."}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
Write-Output "PASS (scoped): native debris reflected hits=$successful fresh pooled activations=$reused; authored velocity, actual armor subtraction, gravity restoration and harmless native cleanup verified. Other tiers and encounter-return coverage remain separate."
