param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$ns=Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if($ns -match 'NS_ROOM_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|LIFECYCLE_FAILED'){throw 'Native room isolation failed.'}
foreach($marker in @('NS_ROOM_QUARANTINE_READY triggers=','NS_ROOM_CUTSCENE_CHECK nativeEntry=True controlUnchanged=True triggersDisabled=True','NS_ROOM_SOUND_BLOCKED event=EnvirSFX_A2S5_HorsemanChamber','NS_ROOM_SOUND_BLOCKED event=EnvirSFX_LotusStatic_L','NS_ROOM_SOUND_ALLOWED event=Player_SFX_Sword_Swing','NS_ROOM_SOUND_ALLOWED event=CharSFX_Bow_')){if(-not $ns.Contains($marker)){throw "Missing room isolation evidence: $marker"}}
if($ns -notmatch 'NS_ROOM_TRIGGER_ISOLATED path=[^\r\n]*夸伏演出[^\r\n]*/PlayerSensor\r?\n'){throw 'Kuafu player sensor was not isolated.'}
if($ns -match 'NS_ROOM_TRIGGER_ISOLATED path=[^\r\n]*(StealthGameMonster_SpearHorseMan|YiGodhome|PPlayer)'){throw 'Combat/player/imported trigger entered native room quarantine.'}
if($ns -match 'NS_ROOM_SOUND_ALLOWED event=EnvirSFX_A2S5_HorsemanChamber' -or $ns -match 'NS_AMBIENCE_SUPPRESSED event=BGM_A2_Cutscene_Kuafu'){throw 'Native room sound or story sequence escaped quarantine.'}
if([regex]::Matches($ns,'NS_ROOM_QUARANTINE_RESET triggers=').Count -lt 2){throw 'Quarantine not checked across both warm resets.'}
Write-Output 'PASS: native Kuafu entry blocked without taking control; authored room triggers isolated across two resets, chamber/lotus loops blocked, sword/bow sound paths retained. Audible result requires player confirmation.'
