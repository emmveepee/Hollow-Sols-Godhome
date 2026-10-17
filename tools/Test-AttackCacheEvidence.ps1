param([Parameter(Mandatory)][Guid]$Session, [Parameter(Mandatory)][string]$Boss)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
# Require complete native combat/settlement evidence before interpreting cache counts.
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss $Boss
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($ns -match 'NS_ATTACK_CACHE_EVICTED'){throw 'This workload exceeds the retained cache; no plateau claim.'}
$bindings=@([regex]::Matches($ns,'NS_ATTACK_CACHE_BOUND id=(-?\d+) retained=(\d+) limit=(\d+) encounter=([a-f0-9-]{36})'))
if($bindings.Count -eq 0){throw 'Missing identified cache bindings.'}
$seen=@{}; $peak=0
foreach($m in $bindings){
    $key="$($m.Groups[4].Value)/$($m.Groups[1].Value)"
    if($seen.ContainsKey($key)){throw "Native collider graph recreated within encounter: $key"}
    $seen[$key]=$true
    $count=[int]$m.Groups[2].Value; $limit=[int]$m.Groups[3].Value
    if($limit -ne 256 -or $count -lt 1 -or $count -gt $limit){throw 'Invalid retained cache count.'}
    $peak=[Math]::Max($peak,$count)
}
$encounters=@($bindings | ForEach-Object {$_.Groups[4].Value} | Sort-Object -Unique)
foreach($encounter in $encounters){
    $created=@($bindings | Where-Object {$_.Groups[4].Value -eq $encounter}).Count
    $disposal=[regex]::Match($ns,"NS_ATTACK_CACHE_DISPOSE encounter=$encounter released=(\d+) retained=0")
    if(-not $disposal.Success -or [int]$disposal.Groups[1].Value -ne $created){throw "Incomplete cache teardown for $encounter"}
}
if($encounters.Count -lt 2){throw 'Repeated completed encounters required.'}
$begins=[regex]::Matches($ns,'NS_ATTACK_BEGIN ').Count
if($begins -le $bindings.Count){throw 'No actual pulsed activation reuse demonstrated.'}
Write-Output "PASS: $Boss session=$Session nativeGraphs=$($bindings.Count) activationBegins=$begins peakRetained=$peak limit=256 encounters=$($encounters.Count); no recreated collider graph or eviction, all released. This is fixture churn/teardown evidence, not frame-time or endurance proof."
