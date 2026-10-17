param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if (($ns + $hk) -match '(CONTACT|HIT|NS_MEMORY|HK_GODHOME_PROBE)_FAILED') { throw 'Crimson run contains a runtime failure.' }
foreach ($pattern in @('NS_CRIMSON_WARNING attack=\d+ native=True', 'NS_VISUAL_CRIMSON sample=', 'NS_CRIMSON_SOUND attack=\d+ event=UISFX_DangerHint_Boss accepted=True playingId=[1-9]\d*')) {
    if ($ns -notmatch $pattern) { throw "Missing native warning evidence: $pattern" }
}
$interruptions = [regex]::Matches($hk, 'HK_CRIMSON_INTERRUPTED attack=(\d+) from=([^\r\n]+) to=Slam End')
if ($interruptions.Count -lt 1) { throw 'No native-counter-driven bounce interruption verified.' }
foreach ($item in $interruptions) {
    $attack = $item.Groups[1].Value
    if ($ns -notmatch "NS_CRIMSON_COUNTER attack=$attack native=True") { throw "Interruption without native counter: $attack" }
    $submitted = [regex]::Match($ns, "NS_COUNTER_SUBMITTED sequence=(\d+) attack=$attack(?:\r?\n)")
    if (-not $submitted.Success) { throw "Counter was not transmitted: $attack" }
    $sequence = $submitted.Groups[1].Value
    if ($hk -notmatch "HK_COUNTER_RESULT sequence=$sequence attack=$attack applied=True" -or $ns -notmatch "NS_HOST_HIT_RESULT sequence=$sequence result=Applied") { throw "Counter acknowledgement missing: $attack" }
}
Write-Output "PASS: native crimson warning image/audio, $($interruptions.Count) native unbounded counters, matching attack IDs, early native slam recovery and host acknowledgements. Human timing/readability remains a separate check."
