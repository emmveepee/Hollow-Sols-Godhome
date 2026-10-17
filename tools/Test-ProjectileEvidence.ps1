param([Parameter(Mandatory)][Guid]$Session, [switch]$RequirePit)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'CONTACT_FAILED|NS_MEMORY_FAILED|HK_PROXY_FAILED|LIFECYCLE_FAILED') { throw 'Projectile adapter failed.' }
$bindings = @([regex]::Matches($ns, 'NS_ATTACK_BOUND id=(-?\d+) dealer=(-?\d+) category=Projectile'))
if ($bindings.Count -ne 4) { throw 'Expected four independently bound native projectile graphs in one encounter.' }
$parried = 0
foreach ($binding in $bindings) {
    $id = $binding.Groups[1].Value; $dealer = $binding.Groups[2].Value
    if ($hk -notmatch "HK_ATTACK_VOLUME id=$id activation=\d+ active=True category=Projectile") { throw "Projectile $id never activated, including the four-sword phase." }
    if ($ns -notmatch "NS_ATTACK_BEGIN id=$id activation=\d+") { throw "Projectile $id never reached the native adapter." }
    if ($ns -match "NS_CONTACT_RESOLVED id=\d+ kind=parry dealer=$dealer ") { $parried++ }
}
if ($parried -lt 2) { throw 'Independent native parry evidence requires at least two sword identities.' }
if ($RequirePit) {
    $hazard = [regex]::Match($ns, 'NS_ATTACK_BOUND id=(-?\d+) dealer=(-?\d+) category=Hazard')
    if (-not $hazard.Success) { throw 'Native hazard graph missing.' }
    $id = $hazard.Groups[1].Value; $dealer = $hazard.Groups[2].Value
    $hit = [regex]::Match($ns, "NS_CONTACT_RESOLVED id=\d+ kind=damage dealer=$dealer frame=\d+ health=([\d.]+)")
    if (-not $hit.Success -or [double]$hit.Groups[1].Value -le 0) { throw 'Nonfatal native pit damage missing.' }
    $afterHit = $ns.Substring($hit.Index)
    $respawn = [regex]::Match($afterHit, 'NS_INPUT_NATIVE[^\r\n]*state=Respawn[^\r\n]*grounded=True')
    if (-not $respawn.Success -or $afterHit -notmatch "NS_HAZARD_SEPARATED id=$id ") { throw 'Native pit recovery and separation missing.' }
    $landings = [regex]::Matches($afterHit.Substring($respawn.Index), 'IPC_POSE_SENT[^\r\n]* y=([-\d.E+]+) [^\r\n]*grounded=True health=([\d.]+)')
    if (-not @($landings | Where-Object { [Math]::Abs([double]$_.Groups[1].Value - 10) -lt 0.01 -and [double]$_.Groups[2].Value -gt 0 }).Count) { throw 'Yi did not return alive to the imported platform.' }
}
Write-Output "PASS: four native sword identities activated, $parried independent swords parried; requested pit damage/native respawn checks passed."
