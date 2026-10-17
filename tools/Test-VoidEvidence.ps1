param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '_FAILED(?:\s|$)'){throw 'Void session had an adapter failure.'}
$recovered=$ns.IndexOf('NS_VOID_RECOVERED')
foreach($m in [regex]::Matches($ns,'SESSION_TERMINAL reason=([^ ]+)')) {
 if($recovered -lt 0 -or $m.Index -lt $recovered -or ($m.Groups[1].Value -notin @('VISUAL_CHANNEL_STOPPED','PEER_DISCONNECTED')) -or -not $hk.Contains('SMOKE_EXIT_REQUEST game=HollowKnight frame=')){throw 'Unexpected terminal failure before or after recovery.'}
}
# Host timer exit stops the peer image channel. This scopes completed recovery,
# rather than misrepresenting a timer-ended session as an unlimited-session pass.
foreach($m in [regex]::Matches($hk,'SESSION_TERMINAL reason=([^ ]+)')) {throw 'Host had an unexpected terminal failure.'}
foreach($marker in @('NS_VOID_FIXTURE_PLACED','NS_VOID_NATIVE_TRAP','NS_RECOVERY_ANCHOR nativeSequence=True recorded=False arena=True','NS_VOID_RECOVERED')) {
 if(-not $ns.Contains($marker)){throw "Missing native void evidence: $marker"}
}
$recoveries=[regex]::Matches($ns,'NS_VOID_RECOVERED scene=([^ ]+) geometry=(\d+) x=([^ ]+) y=([^ ]+) nativeNormal=True grounded=True elapsed=([^\r\n]+)')
foreach($r in $recoveries) {
 $geometry=$r.Groups[2].Value
 if($ns -notmatch "NS_VOID_FIXTURE_PLACED scene=$([regex]::Escape($r.Groups[1].Value)) encounter=[\w-]+ geometry=$geometry "){throw 'Recovery has no matching encounter/hazard placement.'}
 if($hk -notmatch "HK_VOID_PRESENTED geometry=$geometry nativeFrame=\d+ onScreen=True opaque=True"){throw 'Recovered geometry was not visibly presented inside the host camera.'}
 if([double]$r.Groups[5].Value -gt 8){throw 'Recovery deadline exceeded.'}
}
Write-Output "PASS $Session`: real hazard collision, native trap/entry respawn, grounded Normal release and opaque on-screen host presentation. One controlled hazard sample; no claim about every pit or natural fall timing."
