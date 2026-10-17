param([Parameter(Mandatory)][Guid]$Session, [switch]$Hits, [switch]$TimedPresentation, [ValidateSet('GruzMother','Xero')][string]$Boss = 'GruzMother')
$ErrorActionPreference = 'Stop'
$scene = if ($Boss -eq 'Xero') { 'GG_Ghost_Xero' } else { 'GG_Gruz_Mother' }
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$markers = @("HK_ENCOUNTER_SELECT tier=0 boss=$scene",'HK_ENCOUNTER_ARENA tier=0 transitioned=True',"HK_PROXY_ARENA_ENTRY_VERIFIED scene=$scene",'HK_PROXY_READY','HK_PROXY_RELEASED')
if (-not $TimedPresentation) { $markers += 'HK_PROXY_FROZEN' }
foreach ($marker in $markers) {
    if (-not $hk.Contains($marker)) { throw "Arena evidence missing: $marker" }
}
if ($hk -match 'HK_ENCOUNTER_FIXTURE_HIT|HK_PROXY_FAILED|HK_GODHOME_PROBE_FAILED' -or $ns -match 'NS_MEMORY_FAILED|NS_INPUT_FAILED') { throw 'Arena fixture failed or synthetic damage remained enabled.' }
if ($ns -notmatch "NS_GEOMETRY_IMPORTED[^\r\n]*scene=$scene" -or $ns -notmatch 'NS_GEOMETRY_VERIFIED') { throw 'Native arena collision unverified.' }
$samples = @([regex]::Matches($hk,'HK_PROXY_SAMPLE[^\r\n]*error=([\d.E+-]+)[^\r\n]*nativeDisabled=True'))
if ($samples.Count -lt 10 -or @($samples | Where-Object { [double]::Parse($_.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture) -gt 0.01 }).Count) { throw 'Arena proxy alignment unverified.' }
$afterReady = $hk.Substring($hk.IndexOf('HK_ENCOUNTER_ARENA'))
$bossSamples = @([regex]::Matches($afterReady,'HK_ENCOUNTER_SAMPLE hp=(\d+) dead=False position=([^\r\n]+) knightHP=(\d+)'))
if ($bossSamples.Count -lt 10 -or (-not $Hits -and @($bossSamples | Where-Object { $_.Groups[1].Value -ne '650' }).Count) -or @($bossSamples | ForEach-Object { $_.Groups[3].Value } | Select-Object -Unique).Count -ne 1) { throw 'Native boss or isolated Knight health changed.' }
if (@($bossSamples | ForEach-Object { $_.Groups[2].Value } | Select-Object -Unique).Count -lt 3) { throw 'Native boss motion missing.' }
Write-Output "PASS: native Attuned arena entry, imported collision, $($samples.Count) accurate proxy samples, live boss motion, Knight health isolated from entry, proxy release. Stale freeze required=$(-not $TimedPresentation). Damage outcomes require the separate combat evidence checks."
