param([Parameter(Mandatory)][Guid]$Session,[Guid]$Encounter=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'Uumuu'){throw 'Evidence is not a Uumuu session.'}
$states=@([regex]::Matches($hk,'HK_UUMUU_NATIVE_STATE encounter=([\w-]+) entity=(-?\d+) activation=(\d+) state=(Explode|Wounded) invincible=False health=(\d+) frame=(\d+)'))
if($states.Count -eq 0){throw 'No actual native explosion/wounded shield exposure. Ooma damage alone does not establish Uumuu mechanics.'}
$valid=$false
foreach($s in $states){
    if($Encounter -ne [Guid]::Empty -and $s.Groups[1].Value -ne "$Encounter"){continue}
    $enc=$s.Groups[1].Value; $entity=$s.Groups[2].Value; $activation=$s.Groups[3].Value
    foreach($hit in [regex]::Matches($hk,"HK_SUMMON_HIT entity=$entity generation=$activation kind=\w+ before=(\d+) after=(\d+) sequence=\d+ encounter=$enc frame=(\d+)")){
        if([int]$hit.Groups[1].Value -gt [int]$hit.Groups[2].Value -and [int]$hit.Groups[3].Value -ge [int]$s.Groups[6].Value){$valid=$true}
    }
}
if(-not $valid){throw 'No native Uumuu HP loss following actual shield exposure.'}
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss Uumuu -Encounter $Encounter
if($hk -notmatch 'HK_HUB_COMBAT_ENDED victory=True'){throw 'Exposure/damage occurred, but native Uumuu victory remains unverified.'}
Write-Output "PASS (scoped): Uumuu native exposure, target damage and victory session=$Session. Diagnostic assistance does not prove ordinary Ooma timing or shield recoil."
