param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    ./tools/Build.ps1 -Project tests/PairedSaveChecks/PairedSaveChecks.csproj
    dotnet tests/PairedSaveChecks/bin/Release/net9.0/PairedSaveChecks.dll .local/paired-save-checks
    if ($LASTEXITCODE -ne 0) { throw 'Paired-save foundation checks failed.' }
} finally { Pop-Location }
