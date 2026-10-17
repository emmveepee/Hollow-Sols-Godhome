param([string]$Project = 'src/SaveGuard/SaveGuard.csproj', [switch]$PublicRelease, [switch]$NoRestore,
    [string]$BepCore, [string]$HollowKnightManaged, [string]$NineSolsManaged)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $workspace '.local/dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $workspace '.local/nuget'
$buildGame = if ($Project -match '^src[/\\](NineSols|HollowKnight)[/\\]Plugin\.csproj$') { $Matches[1] } else { $null }
$flavorSuffix = if ($PublicRelease) { '-public' } else { '' }
$buildState = if ($buildGame) { Join-Path $workspace ".local/build-$buildGame$flavorSuffix.json" } else { $null }
if ($buildState) { @{ success = $false } | ConvertTo-Json | Set-Content -LiteralPath $buildState }
$buildArguments = @('build', (Join-Path $workspace $Project), '-c', 'Release', '--nologo')
$sourceRevision = (& git -C $workspace rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Build source must be in a Git checkout.' }
$sourceTree = (& git -C $workspace rev-parse 'HEAD^{tree}')
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify build source tree.' }
$sourceStatus = @(& git -C $workspace status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect build source status.' }
$buildArguments += "-p:SourceRevisionId=$sourceRevision"
foreach ($propertyName in @('BepCore','HollowKnightManaged','NineSolsManaged')) {
    $propertyValue = Get-Variable -Name $propertyName -ValueOnly
    if ($propertyValue) { $buildArguments += "-p:$propertyName=$propertyValue" }
}
if ($NoRestore) { $buildArguments += '--no-restore' }
if ($PublicRelease) {
    if (!$buildGame) { throw 'PublicRelease requires one of the two plugin projects.' }
    $publicOutput = Join-Path $workspace ".local/public-build/$buildGame"
    $buildArguments += @('-p:HollowSolsPublicRelease=true', "-p:OutputPath=$publicOutput/")
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed: $Project" }
if ($buildState) {
    $artifact = if ($PublicRelease) { Join-Path $publicOutput "YiGodhome.$buildGame.dll" } else { Join-Path $workspace "src/$buildGame/bin/Release/netstandard2.1/YiGodhome.$buildGame.dll" }
    @{ success = $true; sha256 = (Get-FileHash -LiteralPath $artifact).Hash; flavor = $(if ($PublicRelease) { 'public' } else { 'development' }); artifact = $artifact; sourceRevision = $sourceRevision; sourceTree = $sourceTree; sourceClean = ($sourceStatus.Count -eq 0) } | ConvertTo-Json | Set-Content -LiteralPath $buildState
}
