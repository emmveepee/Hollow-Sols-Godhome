[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session,
      [Parameter(Mandatory)][ValidateSet('Preparing','Active','Victory','Defeated','Returned')][string]$Boundary)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log"
$ns=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log"
$trigger=@($hk | Where-Object {$_ -match "SESSION_TERMINAL reason=DIAGNOSTIC_DISCONNECT_$Boundary session=$Session "})
$terminal=@($ns | Where-Object {$_ -match 'SESSION_TERMINAL reason=(PEER_DISCONNECTED|HIT_FAILED|CONTACT_CHANNEL_STOPPED|VISUAL_CHANNEL_STOPPED) '})
if($trigger.Count -ne 1 -or $terminal.Count -ne 1){throw 'Exact injected failure and peer terminal response required.'}
$start=[DateTime]::Parse($trigger[0].Split(' ')[0]); $end=[DateTime]::Parse($terminal[0].Split(' ')[0])
if(($end-$start).TotalSeconds -lt -0.1 -or ($end-$start).TotalSeconds -gt 6){throw 'Peer did not fail within the six-second acknowledgement/connection budget.'}
if($trigger[0] -notmatch "phase=$Boundary " -or $terminal[0] -notmatch 'replay=False'){throw 'Wrong boundary or uncertain damage replay policy.'}
foreach($lines in @($hk,$ns)) {
    $after=$false
    foreach($line in $lines){
        if($line -match 'SESSION_TERMINAL'){ $after=$true; continue }
        if($after -and $line -match 'ENCOUNTER_STATE|NS_HIT_OUTCOME|HK_NATIVE_HIT'){throw 'Encounter or damage continued after terminal failure.'}
    }
}
if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw 'Protected games remained alive after fault.'}
Write-Output "PASS: native $Boundary disconnect terminated its peer in $(($end-$start).TotalMilliseconds) ms, without post-terminal transitions or replay. Session=$Session. This does not establish natural combat timing."
