param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireParry)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if (($ns+$hk) -match 'HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_HUB_FAILED') { throw 'Pooled combat adapter failed.' }
$bindings = @([regex]::Matches($ns, 'NS_ATTACK_BOUND id=(-?\d+) dealer=(-?\d+) category=Projectile'))
$dealers = @($bindings | ForEach-Object { $_.Groups[2].Value })
if (@($bindings | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique).Count -lt 25) { throw 'Native dense projectile bindings missing.' }
$bursts = @([regex]::Matches($hk, '(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})[^\r\n]*HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=True category=Projectile'))
if (-not @($bursts | Group-Object {$_.Groups[1].Value} | Where-Object Count -ge 25).Count) { throw 'Native volley activation burst missing.' }
$verdicts = @([regex]::Matches($ns, 'NS_CONTACT_RESOLVED id=\d+ kind=(damage|parry) dealer=(-?\d+)'))
foreach ($kind in $(if ($RequireParry) { @('damage','parry') } else { @('damage') })) {
    if (-not @($verdicts | Where-Object { $_.Groups[1].Value -eq $kind -and $_.Groups[2].Value -in $dealers }).Count) { throw "Native projectile $kind verdict missing." }
}
$impacts = @([regex]::Matches($hk, 'HK_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) nativeImpact=True'))
if ($impacts.Count -lt 2) { throw 'Native host projectile impacts missing.' }
foreach ($impact in $impacts) {
    $id=$impact.Groups[1].Value; $activation=$impact.Groups[2].Value
    if ($ns -notmatch "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+") { throw 'Host impact has no matching native verdict.' }
}
Write-Output "PASS: dense native volley bindings/activation, native projectile verdicts and $($impacts.Count) matching host impacts. Physical parry feel and sustained performance remain separate."
