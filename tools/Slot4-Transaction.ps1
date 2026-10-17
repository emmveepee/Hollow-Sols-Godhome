# Shared by the initializer and synthetic filesystem tests. Never accepts another live save root.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Slot4PlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        try {
            $attributes = [IO.File]::GetAttributes($cursor)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Redirected path refused: $cursor" }
        }
        catch [IO.FileNotFoundException] { }
        catch [IO.DirectoryNotFoundException] { }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Get-Slot4Hash([string]$Path) {
    Assert-Slot4PlainPath $Path
    try { $null = [IO.File]::GetAttributes($Path) }
    catch [IO.FileNotFoundException] { return $null }
    catch [IO.DirectoryNotFoundException] { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash
}

function Assert-Slot4Root([string]$SaveRoot, [string]$Workspace) {
    $actual = [IO.Path]::GetFullPath($SaveRoot).TrimEnd('\','/')
    $live = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'AppData/LocalLow/Team Cherry/Hollow Knight'))
    $fixtures = [IO.Path]::GetFullPath((Join-Path $Workspace '.local/slot4-transaction-tests')) + [IO.Path]::DirectorySeparatorChar
    if ($actual -ne $live -and -not $actual.StartsWith($fixtures, [StringComparison]::OrdinalIgnoreCase)) { throw 'Only the fixed HK save root or excluded synthetic fixtures are permitted.' }
    Assert-Slot4PlainPath $actual
}

function Assert-Slot4Name([string]$Name) {
    if ($Name -notmatch '^user4(?:\.dat(?:\.bak\d*|\.new|\.del)?|_1\.5\.12620\.dat)$') { throw "Not an audited slot-4 filename: $Name" }
}

function Assert-Slot4GamesClosed {
    if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games before a slot-4 transaction.' }
}

function Get-Slot4Files([string]$SaveRoot) {
    Assert-Slot4PlainPath $SaveRoot
    # Exact allowed family only. An unreadable directory throws rather than meaning absent.
    $names = @([IO.Directory]::EnumerateFiles($SaveRoot, 'user4.dat*', [IO.SearchOption]::TopDirectoryOnly))
    $versioned = Join-Path $SaveRoot 'user4_1.5.12620.dat'
    if ($null -ne (Get-Slot4Hash $versioned)) { $names += $versioned }
    foreach ($path in $names) {
        Assert-Slot4Name ([IO.Path]::GetFileName($path))
        Assert-Slot4PlainPath $path
        Get-Item -LiteralPath $path -ErrorAction Stop
    }
}

function Write-Slot4Journal([string]$Directory, $Journal) {
    Assert-Slot4PlainPath $Directory
    $temporary = Join-Path $Directory 'journal.json.tmp'
    $destination = Join-Path $Directory 'journal.json'
    $json = $Journal | ConvertTo-Json -Depth 8
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    # Windows readers can briefly deny replacement. Keep the last durable journal intact while retrying.
    for ($attempt = 0; ; $attempt++) {
        try { [IO.File]::Move($temporary, $destination, $true); break }
        catch [IO.IOException] { if ($attempt -ge 5) { throw }; Start-Sleep -Milliseconds 100 }
        catch [UnauthorizedAccessException] { if ($attempt -ge 5) { throw }; Start-Sleep -Milliseconds 100 }
    }
}

function Install-Slot4File([string]$Source, [string]$Destination, [string]$ExpectedHash) {
    # Only the primary and the known native version copy are installed. The temporary is also slot 4.
    $name = [IO.Path]::GetFileName($Destination)
    if ($name -notin @('user4.dat','user4_1.5.12620.dat')) { throw 'Unexpected installation target.' }
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Destination)) 'user4.dat.new'
    Assert-Slot4PlainPath $Destination
    Assert-Slot4PlainPath $temporary
    if ((Get-Slot4Hash $Source) -ne $ExpectedHash) { throw 'Candidate or restore source hash mismatch.' }
    $bytes = [IO.File]::ReadAllBytes($Source)
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    if ((Get-Slot4Hash $temporary) -ne $ExpectedHash) { throw 'Staged slot-4 bytes differ.' }
    if ($null -ne (Get-Slot4Hash $Destination)) { [IO.File]::Replace($temporary, $Destination, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Destination) }
    if ((Get-Slot4Hash $Destination) -ne $ExpectedHash) { throw 'Installed slot-4 bytes differ.' }
}

function Assert-Slot4Journal([string]$Directory, [string]$SaveRoot, [string]$Workspace) {
    Assert-Slot4Root $SaveRoot $Workspace
    $parent = [IO.Path]::GetFullPath((Join-Path $Workspace '.local/slot4-transactions'))
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Directory)) -ne $parent) { throw 'Transaction must be directly inside the excluded transaction directory.' }
    Assert-Slot4PlainPath $Directory
    $journal = Get-Content -LiteralPath (Join-Path $Directory 'journal.json') -Raw | ConvertFrom-Json
    if ($journal.slot -ne 4 -or [IO.Path]::GetFullPath($journal.saveRoot) -ne [IO.Path]::GetFullPath($SaveRoot)) { throw 'Transaction scope mismatch.' }
    if ($journal.candidateSha256 -notmatch '^[A-F0-9]{64}$') { throw 'Invalid transaction hash.' }
    $seen = @{}
    foreach ($record in $journal.files) {
        Assert-Slot4Name $record.name
        if ($seen.ContainsKey($record.name)) { throw 'Duplicate backup filename.' }
        $seen[$record.name] = $true
        if ($record.sha256 -notmatch '^[A-F0-9]{64}$' -or (Get-Slot4Hash (Join-Path $Directory "backup/$($record.name)")) -ne $record.sha256) { throw 'Verified backup is missing or changed.' }
    }
    return $journal
}

function Restore-Slot4Transaction([string]$Directory, [string]$SaveRoot, [string]$Workspace) {
    Assert-Slot4GamesClosed
    $journal = Assert-Slot4Journal $Directory $SaveRoot $Workspace
    if ($journal.state -notin @('pending','installed','restoring')) { throw 'Only unfinished transactions may be restored.' }
    # Do not overwrite independently changed saves during recovery.
    foreach ($name in @('user4.dat','user4_1.5.12620.dat')) {
        $original = @($journal.files | Where-Object name -eq $name)
        $current = Get-Slot4Hash (Join-Path $SaveRoot $name)
        if ($null -ne $current -and $current -ne $journal.candidateSha256 -and ($original.Count -eq 0 -or $current -ne $original[0].sha256)) { throw 'Live slot 4 changed independently; automatic recovery blocked.' }
    }
    $journal.state = 'restoring'; Write-Slot4Journal $Directory $journal
    foreach ($name in @('user4.dat','user4_1.5.12620.dat')) {
        $original = @($journal.files | Where-Object name -eq $name)
        $destination = Join-Path $SaveRoot $name
        if ($original.Count -eq 1) {
            if ((Get-Slot4Hash $destination) -ne $original[0].sha256) { Install-Slot4File (Join-Path $Directory "backup/$name") $destination $original[0].sha256 }
        }
        elseif ($null -ne (Get-Slot4Hash $destination)) { Remove-Item -LiteralPath $destination }
    }
    # Restore any previously existing temporary last; other backups were never modified.
    $oldTemporary = @($journal.files | Where-Object name -eq 'user4.dat.new')
    $temporary = Join-Path $SaveRoot 'user4.dat.new'
    Assert-Slot4PlainPath $temporary
    if ($oldTemporary.Count -eq 1) { Copy-Item -LiteralPath (Join-Path $Directory 'backup/user4.dat.new') -Destination $temporary }
    elseif ($null -ne (Get-Slot4Hash $temporary)) { Remove-Item -LiteralPath $temporary }
    foreach ($record in $journal.files) {
        if ((Get-Slot4Hash (Join-Path $SaveRoot $record.name)) -ne $record.sha256) { throw 'Restored backup verification failed.' }
    }
    $journal.state = 'rolled-back'; Write-Slot4Journal $Directory $journal
}

function Start-Slot4Transaction([string]$PlanPath, [string]$SaveRoot, [string]$Workspace) {
    Assert-Slot4GamesClosed
    Assert-Slot4Root $SaveRoot $Workspace
    Assert-Slot4PlainPath $PlanPath
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    if ($plan.profileId -ne 4 -or [IO.Path]::GetFullPath($plan.nativeSaveRoot) -ne [IO.Path]::GetFullPath($SaveRoot)) { throw 'Preparation plan scope mismatch.' }
    if ($plan.preserve -isnot [bool] -or $plan.sourceAbsent -isnot [bool]) { throw 'Invalid preparation policy.' }
    $null = [Guid]::ParseExact($plan.session, 'D')
    if ($plan.candidateFile -ne 'godseeker-user4.dat') { throw 'Unexpected candidate filename.' }
    if ($plan.sourceAbsent) {
        if ($plan.sourceSha256 -ne '' -or $plan.preserve) { throw 'Invalid absent-source plan.' }
        $expectedSource = $null
    }
    else {
        if ($plan.sourceSha256 -notmatch '^[A-F0-9]{64}$') { throw 'Invalid source hash.' }
        $expectedSource = $plan.sourceSha256
    }
    if ((Get-Slot4Hash (Join-Path $SaveRoot 'user4.dat')) -ne $expectedSource) { throw 'Slot 4 changed since native assessment.' }
    if ($plan.preserve) { return [pscustomobject]@{ Preserved=$true; Directory=''; Sha256=$expectedSource } }
    $candidate = Join-Path (Split-Path $PlanPath -Parent) 'godseeker-user4.dat'
    if ($plan.candidateSha256 -notmatch '^[A-F0-9]{64}$' -or (Get-Slot4Hash $candidate) -ne $plan.candidateSha256) { throw 'Prepared candidate hash mismatch.' }
    $directory = Join-Path $Workspace ('.local/slot4-transactions/' + [Guid]::NewGuid().ToString())
    Assert-Slot4PlainPath $directory
    $null = New-Item -ItemType Directory -Path (Join-Path $directory 'backup')
    $records = @()
    foreach ($file in @(Get-Slot4Files $SaveRoot)) {
        $hash = Get-Slot4Hash $file.FullName
        $backup = Join-Path $directory "backup/$($file.Name)"
        Copy-Item -LiteralPath $file.FullName -Destination $backup
        if ((Get-Slot4Hash $backup) -ne $hash -or (Get-Slot4Hash $file.FullName) -ne $hash) { throw 'Backup verification failed.' }
        $records += [pscustomobject]@{ name=$file.Name; sha256=$hash; bytes=$file.Length }
    }
    if ((Get-Slot4Hash (Join-Path $SaveRoot 'user4.dat')) -ne $expectedSource) { throw 'Source changed while backing up.' }
    Copy-Item -LiteralPath $candidate -Destination (Join-Path $directory 'candidate.dat')
    if ((Get-Slot4Hash (Join-Path $directory 'candidate.dat')) -ne $plan.candidateSha256) { throw 'Staged candidate changed.' }
    $journal = [pscustomobject]@{ slot=4; state='pending'; saveRoot=$SaveRoot; sourceAbsent=$plan.sourceAbsent; candidateSha256=$plan.candidateSha256; session=$plan.session; files=@($records); createdUtc=[DateTime]::UtcNow.ToString('O') }
    Write-Slot4Journal $directory $journal
    try {
        Assert-Slot4GamesClosed
        # Full reset through atomic replacement: no interval with a deleted primary.
        foreach ($name in @('user4.dat','user4_1.5.12620.dat')) { Install-Slot4File (Join-Path $directory 'candidate.dat') (Join-Path $SaveRoot $name) $plan.candidateSha256 }
        $journal.state = 'installed'; Write-Slot4Journal $directory $journal
        return [pscustomobject]@{ Preserved=$false; Directory=$directory; Sha256=$plan.candidateSha256 }
    }
    catch {
        $failure = $_
        Restore-Slot4Transaction $directory $SaveRoot $Workspace
        throw $failure
    }
}

function Complete-Slot4Transaction([string]$Directory, [string]$SaveRoot, [string]$Workspace, [string]$VerificationPlan) {
    Assert-Slot4GamesClosed
    $journal = Assert-Slot4Journal $Directory $SaveRoot $Workspace
    if ($journal.state -ne 'installed') { throw 'Transaction is not waiting for verification.' }
    Assert-Slot4PlainPath $VerificationPlan
    $verified = Get-Content -LiteralPath $VerificationPlan -Raw | ConvertFrom-Json
    if ($verified.profileId -ne 4 -or $verified.preserve -ne $true -or $verified.sourceAbsent -ne $false -or $verified.session -eq $journal.session -or [IO.Path]::GetFullPath($verified.nativeSaveRoot) -ne [IO.Path]::GetFullPath($SaveRoot) -or $verified.sourceSha256 -ne $journal.candidateSha256) { throw 'Independent native verification failed.' }
    foreach ($name in @('user4.dat','user4_1.5.12620.dat')) {
        if ((Get-Slot4Hash (Join-Path $SaveRoot $name)) -ne $journal.candidateSha256) { throw 'Installed save changed during verification.' }
    }
    $journal.state = 'verified'; Write-Slot4Journal $Directory $journal
}
