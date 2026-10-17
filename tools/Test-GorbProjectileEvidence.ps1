param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Gorb'){throw 'Wrong encounter.'}
$hits=@([regex]::Matches($hk,'HK_REFLECTED_HIT id=(-?\d+) activation=(\d+) before=(\d+) after=(\d+) collision=True'))
if($hits.Count -lt 1){throw 'No actual native reflected collision hit.'}
$damaged=0;$refused=0
foreach($hit in $hits) {
 $id=$hit.Groups[1].Value;$activation=$hit.Groups[2].Value
 if($ns -notmatch "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=parry reflected=True"){throw 'Native hit lacks a real Yi projectile parry.'}
 $before=[int]$hit.Groups[3].Value;$delta=$before-[int]$hit.Groups[4].Value
 if($delta -eq [Math]::Min(21,$before) -and $delta -gt 0){$damaged++}
 elseif($delta -eq 0){$refused++} # Native Hit owns blocking/evasion; never replay a refusal.
 else {throw 'Unexpected native reflected damage.'}
 if($hk -notmatch "HK_PROJECTILE_REFLECTED id=$id activation=$activation speed=[\d.]+ nativeBody=True damage=21"){throw 'Native projectile flight was not redirected.'}
}
if($damaged -lt 1){throw 'Only refused native collisions; successful reflected damage remains unproven.'}
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss Gorb
Write-Output "PASS (scoped): Gorb native projectile parry/redirection/collisions=$($hits.Count), successful damage=$damaged, native refusals=$refused; repeated difficulty coverage separate."
