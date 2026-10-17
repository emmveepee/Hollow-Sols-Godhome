[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Slot4-Transaction.ps1')
$fixture = Join-Path $workspace ('.local/slot4-transaction-tests/' + [Guid]::NewGuid().ToString())
$null = New-Item -ItemType Directory -Path $fixture
function Require([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Reject([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Require $failed $Message
}
function Make-Plan([string]$Case, [bool]$Absent=$false) {
    $saveRoot = Join-Path $fixture "$Case/save-root"
    $stage = Join-Path $fixture "$Case/stage"
    $null = New-Item -ItemType Directory -Path $saveRoot,$stage
    # Arbitrary fixture text, never a real game save or another live slot.
    Set-Content -LiteralPath (Join-Path $saveRoot 'unrelated-fixture.txt') -Value 'Do not modify this synthetic sentinel.'
    if (-not $Absent) {
        Set-Content -LiteralPath (Join-Path $saveRoot 'user4.dat') -Value 'original primary fixture'
        Set-Content -LiteralPath (Join-Path $saveRoot 'user4_1.5.12620.dat') -Value 'original version fixture'
        Set-Content -LiteralPath (Join-Path $saveRoot 'user4.dat.bak1') -Value 'original backup fixture'
        Set-Content -LiteralPath (Join-Path $saveRoot 'user4.dat.new') -Value 'original pending fixture'
    }
    Set-Content -LiteralPath (Join-Path $stage 'godseeker-user4.dat') -Value 'candidate fixture'
    $hash = Get-Slot4Hash (Join-Path $saveRoot 'user4.dat')
    $plan = [pscustomobject]@{ profileId=4; session=[Guid]::NewGuid().ToString(); sourceSha256=$(if ($Absent) { '' } else { $hash }); sourceAbsent=$Absent; preserve=$false; candidateSha256=(Get-Slot4Hash (Join-Path $stage 'godseeker-user4.dat')); candidateFile='godseeker-user4.dat'; nativeSaveRoot=$saveRoot }
    $path = Join-Path $stage 'slot4-plan.json'
    $plan | ConvertTo-Json | Set-Content -LiteralPath $path
    return [pscustomobject]@{ Root=$saveRoot; Plan=$plan; Path=$path; Sentinel=(Get-Slot4Hash (Join-Path $saveRoot 'unrelated-fixture.txt')) }
}
function Save-Plan($Case) { $Case.Plan | ConvertTo-Json | Set-Content -LiteralPath $Case.Path }

$case = Make-Plan 'replace-and-rollback'
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Require (-not $transaction.Preserved) 'Replacement did not run.'
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $case.Plan.candidateSha256) 'Candidate not installed.'
Restore-Slot4Transaction $transaction.Directory $case.Root $workspace
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $case.Plan.sourceSha256) 'Rollback failed.'
Require ((Get-Slot4Hash (Join-Path $case.Root 'unrelated-fixture.txt')) -eq $case.Sentinel) 'Unrelated fixture changed.'

$case = Make-Plan 'create-and-rollback' $true
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Restore-Slot4Transaction $transaction.Directory $case.Root $workspace
Require ($null -eq (Get-Slot4Hash (Join-Path $case.Root 'user4.dat'))) 'Empty state not restored.'
Require ($null -eq (Get-Slot4Hash (Join-Path $case.Root 'user4_1.5.12620.dat'))) 'Version copy not removed on empty rollback.'

$case = Make-Plan 'preserve'
$case.Plan.preserve = $true; Save-Plan $case
$before = (Get-Item -LiteralPath (Join-Path $case.Root 'user4.dat')).LastWriteTimeUtc
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Require $transaction.Preserved 'Valid existing save not preserved.'
Require ($before -eq (Get-Item -LiteralPath (Join-Path $case.Root 'user4.dat')).LastWriteTimeUtc) 'Preserve path rewrote primary.'

$case = Make-Plan 'changed-source'
Add-Content -LiteralPath (Join-Path $case.Root 'user4.dat') -Value 'changed after assessment'
Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'Source change accepted.'
$case = Make-Plan 'changed-candidate'
Add-Content -LiteralPath (Join-Path (Split-Path $case.Path -Parent) 'godseeker-user4.dat') -Value 'changed after staging'
Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'Candidate change accepted.'
$case = Make-Plan 'wrong-slot'
$case.Plan.profileId = 3; Save-Plan $case
Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'Wrong slot accepted.'
$case.Plan.profileId = 4; $case.Plan.candidateFile = '../escape'; Save-Plan $case
Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'Candidate path escape accepted.'

$case = Make-Plan 'verified'
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Reject { Complete-Slot4Transaction $transaction.Directory $case.Root $workspace $case.Path } 'Unverified candidate accepted.'
$case.Plan.sourceSha256 = $case.Plan.candidateSha256; $case.Plan.preserve = $true; Save-Plan $case
Reject { Complete-Slot4Transaction $transaction.Directory $case.Root $workspace $case.Path } 'Same assessment session accepted as independent verification.'
$case.Plan.session = [Guid]::NewGuid().ToString(); Save-Plan $case
Complete-Slot4Transaction $transaction.Directory $case.Root $workspace $case.Path
Require ((Start-Slot4Transaction $case.Path $case.Root $workspace).Preserved) 'Repeat initialization replaced valid save.'

$case = Make-Plan 'corrupt-backup'
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Add-Content -LiteralPath (Join-Path $transaction.Directory 'backup/user4.dat') -Value 'corrupt'
Reject { Restore-Slot4Transaction $transaction.Directory $case.Root $workspace } 'Corrupt backup restored.'
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $case.Plan.candidateSha256) 'Failed recovery changed live fixture.'

$case = Make-Plan 'external-change'
$transaction = Start-Slot4Transaction $case.Path $case.Root $workspace
Add-Content -LiteralPath (Join-Path $case.Root 'user4.dat') -Value 'independent edit'
$externalHash = Get-Slot4Hash (Join-Path $case.Root 'user4.dat')
Reject { Restore-Slot4Transaction $transaction.Directory $case.Root $workspace } 'Independent edit overwritten.'
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $externalHash) 'Failed recovery changed independent edit.'

$case = Make-Plan 'locked-source'
$held = [IO.File]::Open((Join-Path $case.Root 'user4.dat'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'I/O failure was treated as absence.' }
finally { $held.Dispose() }
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $case.Plan.sourceSha256) 'I/O refusal changed source.'

$case = Make-Plan 'partial-write-failure'
$versionPath = Join-Path $case.Root 'user4_1.5.12620.dat'
[IO.File]::SetAttributes($versionPath, [IO.FileAttributes]::ReadOnly)
try { Reject { Start-Slot4Transaction $case.Path $case.Root $workspace } 'Expected version-file failure did not occur.' }
finally { [IO.File]::SetAttributes($versionPath, [IO.FileAttributes]::Normal) }
Require ((Get-Slot4Hash (Join-Path $case.Root 'user4.dat')) -eq $case.Plan.sourceSha256) 'Partial-write failure did not restore primary automatically.'
$matchingJournal = @(Get-ChildItem -LiteralPath (Join-Path $workspace '.local/slot4-transactions') -Filter journal.json -Recurse -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Where-Object saveRoot -eq $case.Root)
Require ($matchingJournal.Count -eq 1 -and $matchingJournal[0].state -eq 'rolled-back') 'Partial failure left an unresolved journal.'

$journalFixture = Join-Path $fixture 'journal-lock'
$null = New-Item -ItemType Directory -Path $journalFixture
Write-Slot4Journal $journalFixture ([pscustomobject]@{state='original'})
$journalPath = Join-Path $journalFixture 'journal.json'
$journalHash = Get-Slot4Hash $journalPath
$held = [IO.File]::Open($journalPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Reject { Write-Slot4Journal $journalFixture ([pscustomobject]@{state='next'}) } 'A locked journal was replaced.' }
finally { $held.Dispose() }
Require ((Get-Slot4Hash $journalPath) -eq $journalHash) 'Failed journal update damaged its last durable state.'
Write-Slot4Journal $journalFixture ([pscustomobject]@{state='next'})
Require ((Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json).state -eq 'next') 'Journal could not recover after reader released it.'
Write-Output 'PASS: synthetic replacement, empty-slot creation, rollback, preservation, repeat run, changed inputs, scope checks, independent verification, corrupt backup, external-change refusal, I/O refusal, automatic partial-write recovery and locked-journal recovery.'
