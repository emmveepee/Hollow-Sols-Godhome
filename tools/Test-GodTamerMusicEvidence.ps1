param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'GodTamer'){throw 'Wrong encounter.'}
if([regex]::Matches($hk,'HK_GOD_TAMER_MUSIC_RESTORED cue=GG_Heavy nativeSceneAsset=True nativeAudioManager=True nativeSnapshotPreserved=True').Count -lt 2){throw 'Need repeated native encounter music activation.'}
if([regex]::Matches($hk,'HK_GOD_TAMER_MUSIC sample=\d+ scene=GG_God_Tamer authored=none current=GG Heavy').Count -lt 8){throw 'Native authored cue not sustained.'}
if([regex]::Matches($hk,'HK_GOD_TAMER_MUSIC_SOURCE owner=-?\d+ clip=GG3_part_B2 playing=True mute=False volume=1 active=True').Count -lt 8){throw 'Native music source playback is unproven.'}
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss GodTamer
Write-Output 'PASS (scoped): repeated native fight music cue and sustained unmuted native clip playback; audible user acceptance separate.'
