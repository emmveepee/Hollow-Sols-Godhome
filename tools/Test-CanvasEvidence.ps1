param([Parameter(Mandatory)][Guid]$Session, [switch]$Combat, [switch]$NoDonors)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/NineSols/$Session"
$ns=Get-BoundedRuntimeLog -Run $run
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
if(($ns+$hk) -match '_FAILED(?:\s|$)|SESSION_TERMINAL[^\r\n]*phase=(Preparing|Active|Victory|Defeated|Restoring)'){throw 'Canvas session has a failed gameplay boundary.'}
foreach($pattern in @(
 'NS_CANVAS_READY scene=YiGodhomeCanvas serviceScene=YiGodhomeServices nativeLevel=\d+ player=-?\d+ receiver=-?\d+ rest=-?\d+ excludedRoomRenderers=\d+ bootstrapUnloaded=True',
 'NS_MEMORY_SAMPLE .*scene=YiGodhomeCanvas',
 'NS_REPOSITORY_DISPOSED templates=\d+ leases=0',
 'NS_CANVAS_DISPOSED transferred=3 nativeServicesRetained=True bootstrapUnloaded=True')){
 if($ns -notmatch $pattern){throw "Missing actual canvas evidence: $pattern"}
}
if($NoDonors -and ($ns -match 'NS_REPOSITORY_REQUEST|NS_REPOSITORY_DONOR' -or $ns -notmatch 'NS_REPOSITORY_DISPOSED templates=0 leases=0')){throw 'On-demand idle session loaded a donor.'}
$identity='scene=YiGodhomeCanvas serviceScene=YiGodhomeServices nativeLevel=\d+ player=-?\d+ receiver=-?\d+ rest=-?\d+ excludedRoomRenderers=\d+'
$transferred=@([regex]::Matches($ns,"NS_CANVAS_TRANSFERRED ($identity) bootstrapUnloaded=False"))
$ready=@([regex]::Matches($ns,"NS_CANVAS_READY ($identity) bootstrapUnloaded=True"))
if($transferred.Count -ne 1 -or $ready.Count -ne 1 -or $transferred[0].Groups[1].Value -ne $ready[0].Groups[1].Value -or $ready[0].Index -le $transferred[0].Index){throw 'Native owner identity or ordered bootstrap retirement is unproven.'}
if($Combat){
 $boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
 if($boss -eq 'PureVessel'){ & "$PSScriptRoot/Test-PureVesselEvidence.ps1" -Session $Session } else { & "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss $boss }
 if(@([regex]::Matches($ns,'NS_WARM_RESET_COMPLETED reloaded=False')).Count -lt 2){throw 'Two native canvas warm resets required.'}
 & "$PSScriptRoot/Test-ReturnPresentation.ps1" -Session $Session
 & "$PSScriptRoot/Measure-EncounterHealth.ps1" -Session $Session
}
Write-Output "PASS: primary canvas ownership, native service identity/teardown; noDonors=$NoDonors combat=$Combat. This is scoped evidence, not arbitrary content or all boss families."
