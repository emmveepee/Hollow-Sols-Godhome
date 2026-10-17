param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireCounterOnly, [ValidateSet(0.11,0.18)][double]$ExpectedHold=0.18)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hostLines=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session" -Lines
$nativeLines=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session" -Lines
$errors=[Collections.Generic.List[string]]::new()
$count=0
foreach($line in $hostLines) {
 if($line -notmatch 'HK_PARRY_PAUSE sequence=(\d+) encounter=(\S+) nativeFreeze=1 frame=(\d+)'){continue}
 $seq=$Matches[1];$encounter=$Matches[2]
 $blockTime=[DateTime]$line.Split(' ')[0]
 $bossResume=@($hostLines | Where-Object {$_ -match "HK_COUNTER_HOLD_END sequence=$seq " -and [DateTime]$_.Split(' ')[0] -ge $blockTime -and [DateTime]$_.Split(' ')[0] -le $blockTime.AddSeconds(1)})
 if($bossResume.Count -ne 1 -or $bossResume[0] -notmatch 'spriteChanged=False stateChanged=False'){$errors.Add("Boss counter changed sprite/controller or failed to resume $seq/$encounter");continue}
 $duration=$ExpectedHold.ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)
 $stop=@($nativeLines | Where-Object {$_ -match "NS_HOST_PARRY_STOP sequence=$seq encounter=$encounter .*seconds=$([regex]::Escape($duration)) animationSpeed=0 "})
 $resumes=@($nativeLines | Where-Object {$_ -match "NS_HOST_PARRY_RESUME sequence=$seq encounter=$encounter "})
 if($stop.Count -ne 1 -or $resumes.Count -ne 1){$errors.Add("Missing/duplicate frame-hold pair $seq/$encounter");continue}
 if($resumes[0] -notmatch 'pausedDisplacement=([\d.E+-]+) animationProgress=([\d.E+-]+) spriteChanged=False stateChanged=False elapsed=([\d.E+-]+)(?: held=([\d.E+-]+))? frame=(\d+)'){$errors.Add("Incomplete animation oracle $seq");continue}
 $held=if($Matches[4]){[double]$Matches[4]}else{[double]$Matches[3]}
 if([double]$Matches[1] -gt 0.000001 -or [double]$Matches[2] -gt 0.000001 -or $held -lt $ExpectedHold -or $held -gt $ExpectedHold+0.09){$errors.Add("Frame hold changed pose/animation or exceeded duration $seq");continue}
 $count++
}
if($count -eq 0){$errors.Add('No real native HK blocking hit exercised.')}
if($RequireCounterOnly) {
 $enable=@($hostLines | Where-Object {$_ -match 'HK_PV_MANUAL_COUNTER_ONLY enabled=True encounter=(\S+) '}) | Select-Object -First 1
 if(-not $enable){$errors.Add('Counter-only mode never enabled.')}
 else {
  $null=$enable -match 'encounter=(\S+) '; $enc=$Matches[1]
  $disable=@($hostLines | Where-Object {$_ -match "HK_PV_MANUAL_COUNTER_ONLY enabled=False encounter=$enc " -and [DateTime]$_.Split(' ')[0] -gt [DateTime]$enable.Split(' ')[0]}) | Select-Object -First 1
  if(-not $disable){$errors.Add('Counter-only mode never disabled.')}
  else {
   $from=[DateTime]$enable.Split(' ')[0]; $until=[DateTime]$disable.Split(' ')[0]
   $window=@($hostLines | Where-Object {$_ -match '^\d{4}-\d\d-\d\dT[^ ]+Z ' -and [DateTime]$_.Split(' ')[0] -ge $from -and [DateTime]$_.Split(' ')[0] -le $until})
   if(@($window | Where-Object {$_ -match 'HK_PV_MANUAL_COUNTER_CYCLE cycle=\d+ nativeAntic=True'}).Count -lt 2 -or -not($window -match "HK_PARRY_PAUSE sequence=\d+ encounter=$enc ")){$errors.Add('Counter-only window lacks repeated selection and a real native blocking hit.')}
  }
 }
}
@{session=$Session;nativeBlockHolds=$count;errors=@($errors);passed=$errors.Count -eq 0;scope='Real HK FreezeMoment(1) during a translated hit, paired native Yi position/all animator layers/sprite and bounded resume. Does not establish every boss, focus/menu interruption or normal input feel.'} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$root/.local/input-tests/$Session/counter-freeze.json"
if($errors.Count){throw ($errors -join '; ')}
Write-Output "PASS: $count native counter blocks with exact Yi position, animation and sprite hold, stable boss sprite/controller and bounded resume. Session=$Session"
