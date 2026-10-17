param([Parameter(Mandatory)][Guid]$Session, [ValidateSet('GruzMother','Xero')][string]$Boss = 'GruzMother')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk + $ns) -match 'CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED') { throw 'Incoming contact bridge failed.' }
$markers = @('NS_CONTACT_OWNER_READY','NS_CONTACT_DAMAGE nativeAmount=27','CONTACT_RECEIVED')
if ($Boss -eq 'GruzMother') { $markers += @('NS_HOST_CONTACT_SAMPLE','NS_HOST_CONTACT_SEPARATED') }
else { $markers += @('NS_ATTACK_BOUND','NS_ATTACK_BEGIN') }
foreach ($marker in $markers) { if (-not $ns.Contains($marker)) { throw "Missing contact evidence: $marker" } }
$received = @([regex]::Matches($ns, 'CONTACT_RECEIVED sequence=(\d+) frame=(\d+) entity=(-?\d+) collider=(-?\d+) active=(True|False) health=(\d+) geometryFrame=(\d+) x=([-\d.E+]+) y=([-\d.E+]+)'))
if ($received.Count -lt 10) { throw 'Insufficient contact samples.' }
foreach ($m in $received) { if (-not $hk.Contains($m.Value.Replace('CONTACT_RECEIVED','CONTACT_SENT'))) { throw 'Contact sample did not match host.' } }
$active = $ns
if ($ns -notmatch 'NS_CONTACT_OWNER_READY[^\r\n]*health=240') { throw 'Yi entered the translated encounter already damaged.' }
if ($hk.Contains('HK_INPUT_SOURCE mode=scripted') -and -not $ns.Contains('NS_CONTACT_DUPLICATE_VERIFIED')) { throw 'Explicit native duplicate callback rejection missing.' }
$verdicts = @([regex]::Matches($ns,'NS_CONTACT_RESOLVED id=(\d+) kind=(damage|parry) dealer=(-?\d+)'))
if ($verdicts.Count -lt 2 -or @($verdicts | ForEach-Object { ($_.Groups[3].Value + ':' + $_.Groups[1].Value) } | Select-Object -Unique).Count -ne $verdicts.Count) { throw 'Contact verdict identities missing or repeated.' }
if ($active -notmatch 'phase=after method=PlayerHealth.ReceiveDamage[^\r\n]*result=True') { throw 'Native damage from translated host contact missing.' }
if ($active -notmatch 'phase=after method=PlayerParryState.Parried[^\r\n]*result=True[^\r\n]*accurate=True') { throw 'Native accurate parry against host contact missing.' }
Write-Output "PASS: $($received.Count) exact host contact samples, native incoming damage and accurate parry, unique contact generations. Outgoing damage requires the separate hit evidence check."
