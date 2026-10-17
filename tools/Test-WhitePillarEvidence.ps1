param([Parameter(Mandatory)][Guid]$Session,[switch]$GroundOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'WhiteDefender'){throw 'White Defender evidence required.'}
if(Test-Path "$run/invulnerable-probe.request"){throw 'Invulnerability cannot prove pillar defenses.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|LIFECYCLE|NS_TARGET|HK_HUB|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Pillar integration failure.'}
$ground=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_WHITE_PILLAR_COUNTER entity=-?\d+ action=(?<action>\d+) state=\S+ nativeSequenceContinues=True frame=\d+'))
$aerial=@([regex]::Matches($hk,'(?m)^(?<time>\S+) (?:HK_NATIVE_SPAWN_CRIMSON_COUNTER action=(?<action>\d+) source=Dung_Pillar[^ ]*|HK_AUTHORED_SEQUENCE_COUNTER entity=-?\d+ action=(?<action>\d+) state=(?:Air_Dive|AD_In|Under|Pillar|Pillar_Increment)) nativeSequenceContinues=True frame=\d+'))
if($ground.Count -lt 2){throw 'Need repeated actual ground-pillar counters.'}
if(-not $GroundOnly -and $aerial.Count -lt 2){throw 'Need repeated actual aerial-sequence counters; no proof from unexercised waves.'}
foreach($counter in @($ground)+@($aerial)){
 $action=$counter.Groups['action'].Value;$stamp=[DateTimeOffset]::Parse($counter.Groups['time'].Value)
 $verdict=@([regex]::Matches($ns,"(?m)^(\S+) NS_VOLUME_CRIMSON_COUNTER action=$action frame=(\d+) encounter=[\w-]+ collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups[1].Value)-$stamp).TotalMilliseconds) -le 100})
 $ack=@([regex]::Matches($hk,"(?m)^(\S+) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups[1].Value)-$stamp).TotalMilliseconds) -le 100})
 if($verdict.Count -ne 1 -or $ack.Count -ne 1){throw 'Noninterrupting counter lacks exact native Yi verdict/host acknowledgement.'}
 $frame=$verdict[0].Groups[2].Value
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$frame damaging=False"){throw 'Pillar siblings did not retire in the actual defense frame.'}
}
Write-Output "PASS (scoped): White ground-pillar counters=$($ground.Count), aerial-sequence counters=$($aerial.Count); native unbound/acknowledgement and same-frame group retirement, boss sequence preserved."
