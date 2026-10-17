param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match '(CONTACT|HIT|NS_MEMORY|HK_PROXY|NS_VISUAL|HK_VISUAL|LIFECYCLE)_FAILED') { throw 'Kick adapter failed.' }
$success = @([regex]::Matches($ns, 'NS_TAICHI_NATIVE attack=(\d+) weakness=True bounce=True'))
foreach ($route in @('AerialParry','SkullKick')) {
    $trials = @([regex]::Matches($ns, "NS_KICK_FIXTURE attack=(\d+) nativeState=True requested=$route"))
    $matched = @($trials | Where-Object { $attack = $_.Groups[1].Value; @($success | Where-Object { $_.Groups[1].Value -eq $attack }).Count -gt 0 })
    if ($matched.Count -lt 2) { throw "Insufficient native $route counters." }
}
$stuns = @([regex]::Matches($hk, 'HK_TAICHI_STUN attack=(\d+)'))
if ($stuns.Count -ne $success.Count -or $stuns.Count -lt 4) { throw 'Native counters and host stuns disagree.' }
if ([regex]::Matches($hk, 'HK_TAICHI_RETREAT_ART clip=Fly source=Idle').Count -lt $stuns.Count - 1) { throw 'Ordinary fly retreat not verified.' }
$retired = @([regex]::Matches($ns, 'NS_ATTACK_RETIRED attack=(\d+) damaging=False sameNativeFrame=\d+'))
foreach ($kick in $success) {
    if (-not @($retired | Where-Object {$_.Groups[1].Value -eq $kick.Groups[1].Value}).Count) { throw 'Native kick did not retire its damaging dealer.' }
}
if ($hk -match 'HK_BODY_STATE Big Buzzer:Swoop Rise attackWindow=True') { throw 'Countered swoop regained damage during retreat.' }
& "$PSScriptRoot/Test-RecoveryEvidence.ps1" -Session $Session -ExpectedFloor 12
Write-Output "PASS: $($success.Count) native weakness counters across both aerial routes, matching host stuns and ordinary Fly retreat. Timing/appearance still need player testing; this is not a victory regression."
