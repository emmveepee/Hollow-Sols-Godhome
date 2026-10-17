param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if(($ns+$hk) -match '(?:NS_VISUAL|HK_VISUAL|CONTACT|HIT)_FAILED|CONTACT_STOP reason=(?:InvalidDataException|InvalidOperationException)'){throw 'Native HUD integration failed.'}
$scope=@([regex]::Matches($ns,'NS_NATIVE_HURT_CAPTURE_SCOPE hudCamera=\S+ hudCameraId=(?<ui>-?\d+) hudDepth=(?<uiDepth>[\d.]+) hurtOwner=[^\r\n]+? hurtCameraId=(?<hurt>-?\d+) hurtDepth=(?<hurtDepth>[\d.]+) sameCamera=False nativeHudSize=(?<width>\d+)x(?<height>\d+)'))
if(!$scope.Count){throw 'Missing actual distinct native camera ownership.'}
foreach($s in $scope){
 if($s.Groups['ui'].Value -eq $s.Groups['hurt'].Value -or [float]$s.Groups['uiDepth'].Value -le [float]$s.Groups['hurtDepth'].Value){throw 'Native camera order does not support late HUD rendering.'}
 # This capture proves current installed source density; higher-source sizing
 # needs separate evidence rather than assuming the transport cap is sufficient.
 if($s.Groups['width'].Value -ne '1920' -or $s.Groups['height'].Value -ne '1080'){throw 'Source-density coverage differs from the verified 1080p native HUD.'}
}
if($ns -notmatch 'NS_NATIVE_HURT_HUD_ORDER nativeDepthOrder=True hudAfterPostprocess=True widgetShaderUnchanged=True depthStencil=True capture=1920x1080 frame=\d+'){throw 'Missing executed native late-HUD pass.'}
if($ns -notmatch 'NS_NATIVE_HURT_RENDER nativeMethods=True lut=True blood=True sourceBlend=1 captureBlend=1 nativeStrength=True dualMatte=True'){throw 'Native hurt image methods/strength were not exercised.'}
if($ns -notmatch 'NS_DAMAGE_VISIBILITY[^\r\n]*state=Hurt sourceEnabled=True sourceActive=True[^\r\n]*capturedOpaque=True[^\r\n]*bodyRegionOpaquePixels=[1-9]\d*'){throw 'Actual hurt-body visibility was not exercised.'}
if($hk -notmatch 'HK_NATIVE_DAMAGE_HUD_SCREENSHOT sequence=\d+ file=native-damage-host-1.png fullResolution=True'){throw 'No actual damage presentation screenshot.'}
$path="$root/.local/runs/HollowKnight/$Session/native-damage-host-1.png"
$file=Get-Item -LiteralPath $path
if($file.Length -lt 1024){throw 'Incomplete host presentation image.'}
$stream=[IO.File]::OpenRead($path)
try {
 $header=New-Object byte[] 24
 if($stream.Read($header,0,24) -ne 24 -or [BitConverter]::ToString($header,0,8) -ne '89-50-4E-47-0D-0A-1A-0A'){throw 'Invalid PNG presentation evidence.'}
 $width=([int]$header[16] -shl 24) -bor ([int]$header[17] -shl 16) -bor ([int]$header[18] -shl 8) -bor $header[19]
 $height=([int]$header[20] -shl 24) -bor ([int]$header[21] -shl 16) -bor ([int]$header[22] -shl 8) -bor $header[23]
 if($width -lt 1920 -or $height -lt 1080){throw 'Host screenshot lacks native-scale presentation coverage.'}
} finally {$stream.Dispose()}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
Write-Output "PASS (scoped): native hurt camera precedes distinct HUD camera; executed native late-HUD/depth-stencil pass, unchanged native effect strength, opaque hurt body and ${width}x${height} presentation evidence. PNG pixels need visual review; higher-source resolution, human flicker and performance remain separate."
