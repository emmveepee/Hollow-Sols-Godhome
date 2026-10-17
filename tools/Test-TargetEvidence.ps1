param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk + $ns) -match 'CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED') { throw 'Target projection failed.' }
if ($ns -notmatch 'NS_TARGET_RECEIVER' -or @([regex]::Matches($ns,'NS_TARGET_SAMPLE')).Count -lt 10) { throw 'Insufficient target projection evidence.' }
$hits = @([regex]::Matches($ns,'NS_TARGET_NATIVE_HIT[^\r\n]*available=True[^\r\n]*kind=(\w+)[^\r\n]*hostDamage=False'))
if ($hits.Count -lt 3) { throw 'Insufficient native hits against projected host target.' }
$kinds = @($hits | ForEach-Object {$_.Groups[1].Value} | Select-Object -Unique)
if ($kinds.Count -lt 2) { throw 'Multiple native attack categories not yet verified.' }
Write-Output "PASS: native receiver projection and $($hits.Count) native hit results ($($kinds -join ', ')). Host damage remains disabled."
