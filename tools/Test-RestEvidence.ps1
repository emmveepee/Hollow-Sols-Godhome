param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk = Get-BoundedRuntimeLog "$workspace/.local/runs/HollowKnight/$Session"
$ns = Get-BoundedRuntimeLog "$workspace/.local/runs/NineSols/$Session"
if (($hk+$ns) -match 'NS_REST_FAILED|NS_VISUAL_FAILED|HK_VISUAL_FAILED|NS_MEMORY_FAILED') { throw 'Native rest regression failed.' }
foreach ($marker in @('NS_REST_FAR_VISIBLE','NS_REST_ENTER native=True','NS_REST_EXIT nativeControlRestored=True','NS_MENU_PRESENTATION open=True','NS_REST_RESOURCE_FIXTURE explicitDiagnostic=True','NS_REST_RESOURCE_RESTORED native=True')) {
    if (-not $ns.Contains($marker)) { throw "Missing rest evidence: $marker" }
}
if ($ns -notmatch 'NS_REST_TRANSITION[^\r\n]*clips=SP_Playersit[^\r\n]*menu=True') { throw 'Native rest menu did not open while seated.' }
if (-not $hk.Contains('HK_REST_BENCH_REPLACED nativeKnightDismounted=True')) { throw 'Host bench was not replaced.' }
if (-not (Test-Path -LiteralPath "$workspace/.local/runs/NineSols/$Session/native-rest.png")) { throw 'Native rest render not captured.' }
if (-not (Test-Path -LiteralPath "$workspace/.local/runs/NineSols/$Session/native-rest-menu.png")) { throw 'Native rest menu render not captured.' }
& "$PSScriptRoot/Test-HudEvidence.ps1" -Session $Session -HighResolution
Write-Output 'PASS: native rest entry/exit and host bench replacement. Equipment mutation and visual acceptance require separate verification.'
