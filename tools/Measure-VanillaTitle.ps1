[CmdletBinding()]
param([ValidateRange(30,600)][int]$Seconds=60, [switch]$WithNineSols, [switch]$Slot4Fight, [Guid]$NativeCompanion=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if ($NativeCompanion -ne [Guid]::Empty) {
 if($WithNineSols -or $Slot4Fight -or $Seconds -gt 120 -or (Get-Process NineSols -ErrorAction SilentlyContinue)){throw 'Native companion is a bounded idle Nine Sols title only.'}
 $nativeRun=Join-Path $root ".local/runs/HollowKnight/$NativeCompanion"
 $nativeInfo=Get-Content -LiteralPath "$nativeRun/run.json" -Raw | ConvertFrom-Json
 if(!(Test-Path -LiteralPath "$nativeRun/native-perf.request") -or (Get-Content -LiteralPath "$nativeRun/bootstrap.log" -Raw) -notmatch "BOOTSTRAP_GUARD_VERIFIED session=$NativeCompanion"){throw 'Companion requires a verified live native NKG benchmark.'}
 $nativeProcesses=@(Get-Process hollow_knight -ErrorAction SilentlyContinue)
 if($nativeProcesses.Count -ne 1 -or $nativeProcesses[0].StartTime.ToUniversalTime() -lt ([datetime]$nativeInfo.utc).ToUniversalTime().AddSeconds(-1)){throw 'Native benchmark process identity changed.'}
} elseif (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games before a vanilla comparison.' }
if ($env:YIGODHOME_SESSION) { throw 'Protected environment is present; refusing to label this vanilla.' }
if ($Seconds -gt 120 -and -not $Slot4Fight) { throw 'Long comparisons require the explicitly requested slot4 native fight.' }
if ($Slot4Fight) {
 $backup=Get-Content -LiteralPath "$root/.local/hk-spikes/vanilla-slot4-backup/manifest.json" -Raw | ConvertFrom-Json
 if($backup.profileId -ne 4 -or !$backup.files){throw 'Verified slot4 backup missing.'}
 foreach($file in $backup.files){if((Get-FileHash -LiteralPath $file.backup).Hash -ne $file.sha256){throw 'Slot4 backup changed.'}}
}
$games=if($NativeCompanion -ne [Guid]::Empty){@('NineSols')}elseif($WithNineSols){@('HollowKnight','NineSols')}else{@('HollowKnight')}
$owned=@(); $manifests=@{}; $loaderHashes=@{}
$run=Join-Path $root ('.local/hk-spikes/vanilla-title-'+[Guid]::NewGuid())
New-Item -ItemType Directory -Path $run | Out-Null
try {
 foreach($game in $games){
  $manifest=Get-Content -LiteralPath "$root/.local/deployments/$game.json" -Raw | ConvertFrom-Json
  $manifests[$game]=$manifest
  if(Test-Path -LiteralPath (Join-Path $manifest.root 'BepInEx/core/YiGodhome.session')){throw 'Unexpected protected ticket; do not consume it in a vanilla run.'}
  $loader=Join-Path $manifest.root 'BepInEx/LogOutput.log'
  $loaderHashes[$game]=if(Test-Path -LiteralPath $loader){(Get-FileHash -LiteralPath $loader).Hash}else{'absent'}
  $before=[DateTime]::UtcNow
  Start-Process -FilePath 'C:/Steam/steam.exe' -ArgumentList @('-applaunch',$(if($game -eq 'HollowKnight'){'367520'}else{'1809540'})) -WindowStyle Hidden
  $deadline=[DateTime]::UtcNow.AddSeconds(40); $process=$null
  while([DateTime]::UtcNow -lt $deadline){
   $candidates=@(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($manifest.executable)) -ErrorAction SilentlyContinue | Where-Object {$_.StartTime.ToUniversalTime() -ge $before.AddSeconds(-1)})
   if($candidates.Count -gt 1){throw 'Ambiguous vanilla process.'}
   if($candidates.Count -eq 1){$process=$candidates[0];break}
   Start-Sleep -Milliseconds 250
  }
  if(!$process){throw "Steam did not launch $game."}
  $owned+=,$process
  @{game=$game;pid=$process.Id;utc=$before.ToString('O');noTicket=$true;scope=$(if($Slot4Fight){'User-requested genuine vanilla NKG fight; Computer Use selects only slot4'}else{'unattended title only; no input/save selection'});seconds=$Seconds} | ConvertTo-Json | Set-Content -LiteralPath "$run/$game.json"
 }
 Write-Output "Vanilla title run=$run PIDs=$($owned.Id -join ',') duration=$Seconds"
 $until=[DateTime]::UtcNow.AddSeconds($Seconds)
 while([DateTime]::UtcNow -lt $until){
  foreach($process in $owned){$process.Refresh();if($process.HasExited){throw 'Vanilla game exited early.'};@{utc=[DateTime]::UtcNow.ToString('O');pid=$process.Id;cpuMs=$process.TotalProcessorTime.TotalMilliseconds;privateBytes=$process.PrivateMemorySize64;workingBytes=$process.WorkingSet64;threads=$process.Threads.Count} | ConvertTo-Json -Compress | Add-Content -LiteralPath "$run/resources.jsonl"}
  Start-Sleep -Milliseconds 1000
 }
 foreach($game in $games){
  $loader=Join-Path $manifests[$game].root 'BepInEx/LogOutput.log'
  $after=if(Test-Path -LiteralPath $loader){(Get-FileHash -LiteralPath $loader).Hash}else{'absent'}
  if($after -ne $loaderHashes[$game]){throw 'Loader log changed; vanilla bypass is not verified.'}
 }
 Write-Output "PASS: no-ticket Steam launch, unchanged BepInEx log. Slot4 fight=$Slot4Fight. External frame trace and observed phase evidence are separate."
} finally {
 foreach($process in $owned){if(!$process.HasExited){$process.Kill();$null=$process.WaitForExit(5000)}}
}
