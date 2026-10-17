param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'NS_VISUAL_FAILED|HK_VISUAL_FAILED|NS_MEMORY_FAILED|InvalidDataException') { throw 'Menu runtime failure.' }
foreach ($marker in @('NS_MENU_ROUTE_BLOCKED panel=MemoryChallenge','NS_MENU_REPLAY_BLOCKED beforeCallbacks=True','NS_PROMPT_GLYPH native=True action=Action2')) {
    if (-not $ns.Contains($marker)) { throw "Missing native evidence: $marker" }
}
foreach ($marker in @('HK_INTERACT_EDGE nativeUpAction=True','HK_HUB_MENU_FIXTURE opened=True','HK_INTERACT_PROMPTS visible=1')) {
    if (-not $hk.Contains($marker)) { throw "Missing host evidence: $marker" }
}
$captures = [regex]::Matches($ns, 'NS_MENU_CADENCE frames=(\d+) seconds=([\d.]+)')
$sustained = @($captures | Where-Object { [double]$_.Groups[1].Value / [double]$_.Groups[2].Value -ge 45 })
if ($sustained.Count -lt 2) { throw 'Fewer than two menu capture windows above 45 FPS.' }
$native = @{}
foreach ($m in [regex]::Matches($ns, 'MENU_SENT sequence=(\d+) frame=(\d+) hash=(\d+) bytes=8294400 geometryFrame=(\d+)')) {
    $native[$m.Groups[1].Value] = ($m.Groups[2..4].Value -join ':')
}
$matches = 0
foreach ($m in [regex]::Matches($hk, 'MENU_RECEIVED sequence=(\d+) frame=(\d+) hash=(\d+) bytes=8294400 geometryFrame=(\d+)')) {
    if ($native.ContainsKey($m.Groups[1].Value)) {
        if ($native[$m.Groups[1].Value] -ne ($m.Groups[2..4].Value -join ':')) { throw 'Menu pixels or identity changed during transfer.' }
        $matches++
    }
}
if ($matches -lt 10) { throw 'Insufficient exact full-resolution menu records.' }
Write-Output "PASS: forbidden Battle route and replay Submit rejected; native input edge opens statue; B glyph received; $matches matching 1920x1080 menu records and $($sustained.Count) capture windows above 45 FPS. Physical buttons and prompt placement require player acceptance."
