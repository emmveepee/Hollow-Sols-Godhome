param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$hostText=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$nativeText=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log" -Raw
$sizes=[regex]::Matches($hostText,'HK_PROMPT_SIZE camera=([^ ]+) viewportHeight=(\d+) projectedPixels=([\d.E+-]+) orthographicSize=([\d.E+-]+) expectedPixels=([\d.E+-]+)')
if(-not $sizes.Count){throw 'No measured prompt projection.'}
foreach($sample in $sizes){
    $height=[double]::Parse($sample.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture)
    $actual=[double]::Parse($sample.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
    $expected=$height*56/1080
    if($height -le 0 -or [double]::IsNaN($actual) -or [double]::IsInfinity($actual) -or [Math]::Abs($actual-$expected) -gt 0.5){throw "Incorrect prompt projection: actual=$actual expected=$expected"}
}
$received=[regex]::Match($hostText,'PROMPT_RECEIVED sequence=(\d+) frame=(\d+) hash=(\d+) bytes=262144 geometryFrame=(\d+)')
if(-not $received.Success -or $nativeText -notmatch ('PROMPT_SENT sequence='+$received.Groups[1].Value+' frame='+$received.Groups[2].Value+' hash='+$received.Groups[3].Value+' bytes=262144 geometryFrame='+$received.Groups[4].Value)){throw 'No exact native prompt transfer.'}
Write-Output "PASS: native prompt pixels/identity and $($sizes.Count) projected-size measurements match 56/1080 viewport height. Player visual acceptance remains separate. Session=$Session"
