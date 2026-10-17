[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Slot4-Transaction.ps1')
$saveRoot = Join-Path $env:USERPROFILE 'AppData/LocalLow/Team Cherry/Hollow Knight'
$lockPath = Join-Path $workspace '.local/slot4-initializer.lock'
Assert-Slot4PlainPath $lockPath
$lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    Assert-Slot4GamesClosed
    $transactions = Join-Path $workspace '.local/slot4-transactions'
    $pending = @()
    if (Test-Path -LiteralPath $transactions) {
        foreach ($directory in Get-ChildItem -LiteralPath $transactions -Directory) {
            $journalPath = Join-Path $directory.FullName 'journal.json'
            if (-not (Test-Path -LiteralPath $journalPath)) { continue }
            $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
            if ($journal.saveRoot -eq $saveRoot -and $journal.state -in @('pending','installed','restoring')) { $pending += $directory.FullName }
        }
    }
    if ($pending.Count -gt 1) { throw 'Multiple unfinished live transactions require inspection before any launch.' }
    if ($pending.Count -eq 1) {
        Restore-Slot4Transaction $pending[0] $saveRoot $workspace
        Write-Host "Recovered an interrupted slot-4 transaction from its verified backup: $($pending[0])"
    }
    function Invoke-NativeAssessment {
        $output = @(& (Join-Path $PSScriptRoot 'Start-Baseline.ps1') -Game HollowKnight -InspectHollowKnightSlot4 -Seconds 30 -PassThru)
        $run = $output[-1]
        $path = Join-Path $run.Logs 'slot4-plan.json'
        $plan = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($plan.session -ne $run.Session) { throw 'Assessment session mismatch.' }
        Write-Host "Native slot-4 assessment: $($run.Logs)"
        return $path
    }
    $assessment = Invoke-NativeAssessment
    $transaction = Start-Slot4Transaction $assessment $saveRoot $workspace
    if ($transaction.Preserved) {
        Write-Output 'Existing complete Godseeker slot 4 preserved. No save files written.'
        return
    }
    Add-Content -LiteralPath (Join-Path $workspace '.local/DEVELOPMENT-NOTES.txt') -Value "HK slot-4-only verified backup and transaction: $($transaction.Directory)"
    Write-Host "Installed prepared Godseeker slot 4. Verified backup: $($transaction.Directory)"
    try {
        $verification = Invoke-NativeAssessment
        Complete-Slot4Transaction $transaction.Directory $saveRoot $workspace $verification
    }
    catch {
        $failure = $_
        Restore-Slot4Transaction $transaction.Directory $saveRoot $workspace
        throw $failure
    }
    Write-Output "Godseeker slot 4 verified through the native loader; future runs preserve it. Backup: $($transaction.Directory)"
}
finally { $lock.Dispose() }
