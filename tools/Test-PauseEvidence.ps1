param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireAirborne)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns = Get-BoundedRuntimeLog "$workspace/.local/runs/NineSols/$Session"
$hk = Get-BoundedRuntimeLog "$workspace/.local/runs/HollowKnight/$Session"
$samples = @([regex]::Matches($ns,'NS_PAUSE_SAMPLE scale=([^ ]+) gameplay=([^ ]+) animation=([\d.E+-]+)'))
if ($samples.Count -lt 15 -or @($samples | ForEach-Object {$_.Groups[3].Value} | Select-Object -Unique).Count -ne 1) { throw 'Native animation did not stay frozen during host pause.' }
if (@($samples | Where-Object {$_.Groups[2].Value -ne '0'}).Count -gt 0) { throw 'Native gameplay time advanced during pause.' }
if ($ns -notmatch 'NS_HOST_PAUSE paused=False' -or $hk -notmatch 'HK_PAUSE_FIXTURE resumed=True') { throw 'Pause did not release.' }
if($RequireAirborne){
    $air=@([regex]::Matches($ns,'NS_PAUSE_SAMPLE[^\r\n]+ frame=(\d+) x=([\d.E+-]+) y=([\d.E+-]+) vx=([\d.E+-]+) vy=([\d.E+-]+) grounded=False'))
    if($hk -notmatch 'HK_PAUSE_FIXTURE requested=True airborne=True' -or $air.Count -lt 15){throw 'Insufficient airborne pause samples.'}
    foreach($group in 2..5){if(@($air | ForEach-Object {$_.Groups[$group].Value} | Select-Object -Unique).Count -ne 1){throw 'Airborne position/velocity advanced during pause.'}}
}
$dim = @([regex]::Matches($hk,'HK_HUD_PAUSE_DIM brightness=([^ ]+) nativeCanvasAlpha=([^\r\n]+)'))
if ($dim.Count -lt 3) { throw 'Missing pause HUD dimming evidence.' }
foreach ($m in $dim) { if ([Math]::Abs([double]$m.Groups[1].Value + [double]$m.Groups[2].Value - 1) -gt 0.001) { throw 'HUD dimming differs from native modal alpha.' } }
if (-not @($dim | Where-Object {[double]$_.Groups[1].Value -lt 0.5}).Count) { throw 'HUD did not darken.' }
foreach ($mode in @('True','False')) {
    if ($ns -notmatch "NS_ACTION_GLYPH keyboard=$mode parry=[^ ]+ talisman=[^\r\n]+" -or $ns -notmatch "NS_PROMPT_DEVICE keyboard=$mode") { throw "Missing native glyph mode $mode." }
}
Write-Output "PASS: $($samples.Count) frozen native animation samples, zero gameplay clock, native modal HUD dimming and both native glyph modes. Physical controller/Jade GUI acceptance remains separate."
