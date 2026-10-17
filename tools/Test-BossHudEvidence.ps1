param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireInjury)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$nsRoot = "$workspace/.local/runs/NineSols/$Session"
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns = Get-BoundedRuntimeLog $nsRoot
$hk = Get-BoundedRuntimeLog "$workspace/.local/runs/HollowKnight/$Session"
if ($ns -notmatch 'NS_BOSS_HUD_BOUND native=True') { throw 'Native boss widget was not bound.' }
$samples = [regex]::Matches($ns, 'NS_BOSS_HEALTH entity=(-?\d+) hostHp=(\d+) max=(\d+) nativeInjury=([\d.E+-]+) hostInjury=([\d.E+-]+) solid=([\d.E+-]+) total=([\d.E+-]+)')
if ($samples.Count -lt 2) { throw 'Insufficient boss health samples.' }
$hadInjury = $false
$injuryCleared = $false
foreach ($sample in $samples) {
    $hp = [double]::Parse($sample.Groups[2].Value, [cultureinfo]::InvariantCulture)
    $max = [double]::Parse($sample.Groups[3].Value, [cultureinfo]::InvariantCulture)
    $native = [double]::Parse($sample.Groups[4].Value, [cultureinfo]::InvariantCulture)
    $injury = [double]::Parse($sample.Groups[5].Value, [cultureinfo]::InvariantCulture)
    $solid = [double]::Parse($sample.Groups[6].Value, [cultureinfo]::InvariantCulture)
    $total = [double]::Parse($sample.Groups[7].Value, [cultureinfo]::InvariantCulture)
    if ($max -le 0 -or $hp -gt $max -or [Math]::Abs($injury - [Math]::Min($hp, $native * 21 / 40)) -gt 0.001 -or
        [Math]::Abs($solid - ($hp - $injury) / $max) -gt 0.00001 -or [Math]::Abs($total - $hp / $max) -gt 0.00001) { throw 'Boss widget projection disagrees with authoritative health.' }
    if ($hadInjury -and $injury -eq 0) { $injuryCleared = $true }
    if ($injury -gt 0) { $hadInjury = $true }
}
$matched = 0
foreach ($sent in [regex]::Matches($ns, 'BOSS_HUD_SENT sequence=(\d+) frame=(\d+) hash=(\d+) bytes=(\d+)')) {
    if ([long]$sent.Groups[4].Value -le 4) { continue }
    $signature = $sent.Value.Replace('BOSS_HUD_SENT', 'BOSS_HUD_RECEIVED')
    if ($hk.Contains($signature)) { $matched++ }
}
if ($matched -lt 2) { throw 'Insufficient matching native boss image transfers.' }
if ($RequireInjury -and (-not $hadInjury -or -not $injuryCleared -or -not (Test-Path -LiteralPath "$nsRoot/native-boss-injury.png"))) { throw 'Integrated injury appearance/clear evidence missing.' }
Write-Output "PASS: $($samples.Count) native boss health projections and $matched exact image transfers. Injury source attribution and visual acceptance remain separate."
