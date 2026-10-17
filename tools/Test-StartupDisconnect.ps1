$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw 'Close both games first.'}
& "$PSScriptRoot/Start-CombatPlaytest.ps1" -Mode HubCombat -CheckOnly
$directory=Join-Path $root '.local/playtest-regressions/startup-disconnect'
$null=New-Item -ItemType Directory -Path $directory -Force
$out=Join-Path $directory 'stdout.log'; $err=Join-Path $directory 'stderr.log'
$started=[DateTime]::UtcNow
$helper=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList @('-NoProfile','-File',('"'+$PSScriptRoot+'/Test-InputBridge.ps1"'),'-HubCombat','-Human','-Unlimited') -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
$session=$null; $owned=@(); $closed=$false
try {
 $deadline=[DateTime]::UtcNow.AddSeconds(90)
 while([DateTime]::UtcNow -lt $deadline) {
  if($helper.HasExited){throw 'Launcher exited before disconnect injection.'}
  $output=Get-Content -LiteralPath $out -Raw
  if(-not $session -and $output -match 'Protected input test session=([\w-]+)'){$session=$Matches[1]}
  if($session) {
   $hk="$root/.local/runs/HollowKnight/$session/plugin.log"; $ns="$root/.local/runs/NineSols/$session/plugin.log"
   if((Test-Path -LiteralPath $hk) -and (Test-Path -LiteralPath $ns)) {
    $hostText=Get-Content -LiteralPath $hk -Raw; $nativeText=Get-Content -LiteralPath $ns -Raw
    if($hostText -match 'PLUGIN_READY game=HollowKnight' -and $nativeText -match 'PLUGIN_READY game=NineSols') {
     if($hostText -match 'HK_VISUAL_PRESENTED|IPC_CONNECTED' -or $nativeText -match 'NS_GEOMETRY_IMPORTED|IPC_CONNECTED'){throw 'Preconnection injection window missed; do not call this a preconnection pass.'}
     foreach($game in @('HollowKnight','NineSols')) {
      $manifest=Get-Content -LiteralPath "$root/.local/deployments/$game.json" -Raw | ConvertFrom-Json
      $name=if($game -eq 'HollowKnight'){'hollow_knight'}else{'NineSols'}
      $processes=@(Get-Process -Name $name -ErrorAction Stop)
      if($processes.Count -ne 1){throw 'Ambiguous native process count.'}
      $process=$processes[0]
      $expected=[IO.Path]::GetFullPath((Join-Path $manifest.root $manifest.executable))
      if($process.StartTime.ToUniversalTime() -lt $started -or [IO.Path]::GetFullPath($process.Path) -ne $expected){throw 'Ambiguous native process ownership.'}
      $owned+=$process
     }
     $closeAt=[DateTime]::UtcNow
     if(-not $owned[1].CloseMainWindow()){throw 'Nine Sols did not accept normal window close.'}
     $closed=$true; break
    }
   }
  }
  Start-Sleep -Milliseconds 100
 }
 if(-not $closed){throw 'Protected preconnection startup window timed out.'}
 $exitDeadline=$closeAt.AddSeconds(60)
 while([DateTime]::UtcNow -lt $exitDeadline -and @($owned | Where-Object {-not $_.HasExited}).Count -gt 0){Start-Sleep -Milliseconds 250}
 if(@($owned | Where-Object {-not $_.HasExited}).Count -gt 0){throw 'Native preconnection shutdown exceeded60 seconds; preserve evidence and stop this approach.'}
 $elapsed=([DateTime]::UtcNow-$closeAt).TotalSeconds
 if(-not $helper.WaitForExit(15000)){throw 'Launcher did not collect deliberately disconnected session.'}
 $hostText=Get-Content -LiteralPath $hk -Raw
 if($hostText -notmatch 'SESSION_TERMINAL'){throw 'HK closed without recorded protected terminal failure.'}
 $report=[ordered]@{session=$session;closedUtc=$closeAt.ToString('o');bothExitedSeconds=$elapsed;launcherExit=$helper.ExitCode;deliberatePeerLoss=$true;normalWindowClose=$true;preconnection=$true}
 $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory "$session.json")
 Write-Output "PASS: protected preconnection Nine Sols window close; both native processes exited in $elapsed seconds. Launcher exit=$($helper.ExitCode) is retained as the deliberate failure result, not a gameplay pass. Session=$session"
} finally {
 foreach($process in $owned){if(-not $process.HasExited){$null=$process.CloseMainWindow()}}
}
