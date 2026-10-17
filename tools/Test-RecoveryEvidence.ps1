param([Parameter(Mandatory)][Guid]$Session, [double]$ExpectedFloor = 10)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match '(CONTACT|HIT|NS_MEMORY|HK_PROXY|NS_VISUAL|HK_VISUAL|LIFECYCLE)_FAILED') { throw 'Recovery adapter failed.' }
$frames = @{}
$native = @([regex]::Matches($ns, 'NS_RECOVERY_FRAME frame=(\d+) geometry=(\d+) recovering=(True|False) fade=([^ ]+) state=([^ ]+) sprite=([^\r\n]+?) x=([^ ]+) y=([^\r\n]+)'))
foreach ($m in $native) { $frames[$m.Groups[1].Value] = $m }
$respawn = @($native | Where-Object { $_.Groups[5].Value -eq 'Respawn' })
if ($respawn.Count -lt 3 -or [double]$respawn[0].Groups[4].Value -lt 0.99 -or [Math]::Abs([double]$respawn[0].Groups[8].Value - $ExpectedFloor) -gt 0.01) { throw 'Native relocation was not covered at the imported safe ground.' }
if (-not @($respawn | Where-Object { [double]$_.Groups[4].Value -gt 0 -and [double]$_.Groups[4].Value -lt 0.99 }).Count) { throw 'Native recovery fade-in missing.' }
if (-not @($native | Where-Object { $_.Groups[3].Value -eq 'False' -and [double]$_.Groups[4].Value -eq 0 }).Count) { throw 'Native recovery did not release its cover.' }
$presented = @{}
$hostCovered = $false
foreach ($m in [regex]::Matches($hk, 'HK_RECOVERY_PRESENTED sequence=(\d+) frame=(\d+) geometry=(\d+) recovering=(True|False) fade=([^\r\n]+)')) {
    $source = $frames[$m.Groups[2].Value]
    if (-not $source -or $source.Groups[2].Value -ne $m.Groups[3].Value -or $source.Groups[3].Value -ne $m.Groups[4].Value -or [double]$source.Groups[4].Value -ne [double]$m.Groups[5].Value) { throw 'Recovery alpha/state detached from its native image frame.' }
    $presented[$m.Groups[1].Value] = [double]$m.Groups[5].Value
    if ($source.Groups[5].Value -eq 'Respawn' -and [double]$m.Groups[5].Value -ge 0.99) { $hostCovered = $true }
}
if ($presented.Count -lt 12 -or -not $hostCovered) { throw 'Insufficient covered host recovery presentation.' }
$drawn = @([regex]::Matches($hk, 'HK_RECOVERY_DRAWN sequence=(\d+) fade=([^\r\n]+)'))
if ($drawn.Count -lt 6) { throw 'Recovery overlay was not drawn.' }
foreach ($m in $drawn) { if (-not $presented.ContainsKey($m.Groups[1].Value) -or $presented[$m.Groups[1].Value] -ne [double]$m.Groups[2].Value) { throw 'Drawn recovery opacity differs from the native frame.' } }
Write-Output "PASS: native covered relocation, fade-in/release, $($presented.Count) exact native/host recovery frames and $($drawn.Count) matching overlay draws. This does not establish a get-up animation; animator evidence and player presentation feedback remain separate."
