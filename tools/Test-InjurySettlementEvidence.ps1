param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_VISUAL_FAILED') { throw 'Runtime failure during injury settlement.' }
$fixture = [regex]::Match($hk, 'HK_INJURY_REFUSAL_FIXTURE sequence=(\d+) explicitDiagnostic=True health=(\d+)')
if (-not $fixture.Success) { throw 'No deliberately refused native talisman.' }
$id = $fixture.Groups[1].Value
$restored = [regex]::Matches($ns, "NS_INJURY_RESTORED sequence=$id reason=Refused before=([\d.]+) consumed=([\d.]+) after=([\d.]+) hostDamageRetried=False")
if ($restored.Count -ne 1) { throw 'Injury was not restored exactly once.' }
$m = $restored[0]
$before = [double]$m.Groups[1].Value; $consumed = [double]$m.Groups[2].Value; $after = [double]$m.Groups[3].Value
if ($consumed -le 0 -or [Math]::Abs($after-$before-$consumed) -gt 0.01) { throw 'Native posture setter did not preserve refused injury.' }
if ($hk -notmatch 'HK_NATIVE_HIT[^\r\n]*kind=Talisman[^\r\n]*applied=True') { throw 'No later applied native talisman.' }
Write-Output "PASS: refused talisman $id restored $consumed native injury exactly once; later native talisman applied. Host invulnerability retained, no hit retry."
