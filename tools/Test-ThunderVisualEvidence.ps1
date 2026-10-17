param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/NineSols/$Session"
if(!(Test-Path "$run/markoth-projectile-probe.request")){throw 'Requires bounded native arrow-cycle evidence.'}
$ns=Get-BoundedRuntimeLog $run
if($ns -notmatch 'NS_ARROW_TRIAL[^\r\n]*prefab=ExplodingArrow Shooter'){throw 'Native Thunder weapon was not equipped.'}
$admitted=[regex]::Matches($ns,'NS_THUNDER_RENDERER frame=\d+ renderer=[^\r\n]+? enabled=True active=True selected=True insideCapture=True[^\r\n]*shader=BlendModes/SpriteDefault/Grab')
if($admitted.Count -lt 2){throw 'Native Thunder Grab-phase renderer admission unproven.'}
$captures=[regex]::Matches($ns,'NS_THUNDER_NATIVE_CAPTURE frame=(\d+) nativeShader=True cornerAlpha=0,0,0,0 path=([^\r\n]+)')
if($captures.Count -lt 3){throw 'Insufficient native Grab-stage transparent captures.'}
foreach($capture in $captures){
 $name=[IO.Path]::GetFileName($capture.Groups[2].Value)
 if($name -notmatch '^yi-thunder-grab-[0-3]\.png$'){throw 'Unexpected capture filename.'}
 $file=Join-Path $run $name
 $bytes=[IO.File]::ReadAllBytes($file)
 if($bytes.Length -lt 33 -or $bytes[0] -ne 137 -or [Text.Encoding]::ASCII.GetString($bytes,1,3) -ne 'PNG' -or $bytes[25] -ne 6){throw 'Native capture is not an RGBA PNG.'}
}
& "$PSScriptRoot/Test-VisualEvidence.ps1" -Session $Session
Write-Output "PASS (scoped): native Thunder Grab-phase renderers admitted, $($captures.Count) RGBA captures with transparent corners, captured/received/presented transport verified. Native appearance requires separate image inspection; other arrow variants and shield collisions are separate."
