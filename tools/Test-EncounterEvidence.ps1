[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [ValidateSet('GruzMother','Xero','BroodingMawlek')][string]$Boss = 'GruzMother')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$root = "$workspace/.local/runs/HollowKnight/$Session"
$log = Get-Content -LiteralPath "$root/plugin.log" -Raw
$boot = Get-Content -LiteralPath "$root/bootstrap.log" -Raw
if ($boot -notmatch "BOOTSTRAP_GUARD_VERIFIED session=$Session" -or $boot -match 'FATAL' -or $log -match 'PROBE_FAILED') { throw 'Protected encounter session failed.' }
$scene = if ($Boss -eq 'Xero') { 'GG_Ghost_Xero' } elseif ($Boss -eq 'BroodingMawlek') { 'GG_Brooding_Mawlek' } else { 'GG_Gruz_Mother' }
$previous = -1
foreach ($marker in @('HK_PROXY_DISMOUNT_VERIFIED','HK_GODHOME_PRESENTATION scene=GG_Atrium fade=Normal state=PLAYING',
    'HK_ENCOUNTER_ENTER event=UP PRESSED',"HK_ENCOUNTER_WORKSHOP boss=$scene",
    'HK_ENCOUNTER_INSPECT event=UP PRESSED',"HK_ENCOUNTER_SELECT tier=0 boss=$scene",
    'HK_ENCOUNTER_ARENA tier=0 transitioned=True return=DREAM RETURN','HK_ENCOUNTER_RETURN native=True bossDead=True','SMOKE_EXIT_REQUEST')) {
    $index = $log.IndexOf($marker)
    if ($index -le $previous) { throw "Missing/out-of-order native lifecycle: $marker" }
    $previous = $index
}
$hits = @([regex]::Matches($log,'HK_ENCOUNTER_FIXTURE_HIT before=(\d+) after=(-?\d+) dead=(True|False) nativeHit=True'))
$hp = if ($Boss -eq 'BroodingMawlek') { 1050 } else { 650 }
if ($hits.Count -ne [Math]::Ceiling($hp / 65.0)) { throw 'Unexpected fixture hit count.' }
foreach ($hit in $hits) {
    $expected = [Math]::Max(-50, $hp - 65)
    if ([int]$hit.Groups[1].Value -ne $hp -or [int]$hit.Groups[2].Value -ne $expected) { throw 'Native health continuity failed.' }
    $hp -= 65
}
if ($hits[-1].Groups[3].Value -ne 'True') { throw 'Native death was not observed.' }
$samples = @([regex]::Matches($log,'HK_ENCOUNTER_SAMPLE hp=\d+ dead=False position=\(([^)]+)\) knightHP=(\d+)'))
if (($samples | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique).Count -lt 5) { throw 'Boss motion was not observed.' }
if (-not ($samples | Where-Object { [int]$_.Groups[2].Value -lt 9 })) { throw 'Native contact damage was not observed.' }
Write-Output 'PASS: native bench exit, door, statue selection, Attuned arena, moving boss/contact damage, native fixture hits, death and return. This is an isolated lifecycle fixture, not Yi combat.'
