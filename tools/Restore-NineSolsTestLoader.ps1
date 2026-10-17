[CmdletBinding()]
param([switch]$CheckOnly, [switch]$RefreshBuiltComponents)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$root = 'C:\Steam\steamapps\common\Nine Sols'
$manifestPath = Join-Path $workspace '.local/deployments/NineSols.json'
if (Test-Path -LiteralPath $manifestPath) { return }
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games before starting the mod test.' }
$archive = Get-ChildItem -LiteralPath (Split-Path $manifestPath -Parent) -Filter 'NineSols.json.uninstalled-*' -File | Sort-Object Name -Descending | Select-Object -First 1
if (-not $archive) { throw 'No verified Nine Sols uninstall manifest is available for restoration.' }
$manifest = Get-Content -LiteralPath $archive.FullName -Raw | ConvertFrom-Json
if ($manifest.game -ne 'NineSols' -or [IO.Path]::GetFullPath($manifest.root) -ne $root) { throw 'Unexpected archived deployment.' }
$assembly = Join-Path $root 'NineSols_Data/Managed/Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $assembly).Hash -ne $manifest.assemblyHash) { throw 'Nine Sols version changed; restoration blocked.' }
$built = @{
    'BepInEx\core\YiGodhome.Bootstrap.dll' = 'src/Bootstrap/bin/Release/netstandard2.1/YiGodhome.Bootstrap.dll'
    'BepInEx\patchers\YiGodhome.SaveGuard.dll' = 'src/SaveGuard/bin/Release/netstandard2.1/YiGodhome.SaveGuard.dll'
    'BepInEx\plugins\YiGodhome.NineSols.dll' = 'src/NineSols/bin/Release/netstandard2.1/YiGodhome.NineSols.dll'
}
if ($RefreshBuiltComponents) {
    $stamp = Get-Content -LiteralPath (Join-Path $workspace '.local/build-NineSols.json') -Raw | ConvertFrom-Json
    $plugin = Join-Path $workspace $built['BepInEx\plugins\YiGodhome.NineSols.dll']
    if (-not $stamp.success -or $stamp.sha256 -ne (Get-FileHash -LiteralPath $plugin).Hash) { throw 'Latest Nine Sols build did not succeed or artifact changed. Run tools/Build.ps1 first.' }
}
$plan = foreach ($file in $manifest.files) {
    if ($RefreshBuiltComponents -and $built.ContainsKey($file.relative)) {
        # Explicit developer deployment only, after Build.ps1 succeeds. The user
        # launcher never opts into new binaries; it restores pinned hashes.
        $file.sha256 = (Get-FileHash -LiteralPath (Join-Path $workspace $built[$file.relative])).Hash
    }
    $target = [IO.Path]::GetFullPath((Join-Path $root $file.relative))
    if (-not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Out-of-root restoration target.' }
    # Check existing ancestors, including retained loader directories.
    for ($parent = $target; $parent; $parent = Split-Path $parent -Parent) {
        if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Redirected restoration path: $parent" }
    }
    if (Test-Path -LiteralPath $target) {
        if ($file.relative -eq 'BepInEx\config\BepInEx.cfg') { continue } # Native-generated config is retained; Start-Baseline validates safety settings.
        if ((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256) { throw "Changed retained file: $target" }
        continue
    }
    $source = if ($built.ContainsKey($file.relative)) { Join-Path $workspace $built[$file.relative] } else { Join-Path $workspace ('.local/staging/NineSols/' + $file.relative) }
    if (-not (Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Verified restoration source unavailable: $($file.relative)" }
    [pscustomobject]@{ Source=$source; Target=$target; Hash=$file.sha256; Entry=($file.relative -eq 'winhttp.dll') }
}
if ($CheckOnly) { Write-Output 'PASS: archived Nine Sols deployment and every restoration source verified.'; return }
$created = @()
try {
    # Activate the loader last, after its complete verified guard graph exists.
    foreach ($file in ($plan | Sort-Object Entry)) {
        $null = New-Item -ItemType Directory -Path (Split-Path $file.Target -Parent) -Force
        if (Test-Path -LiteralPath $file.Target) { throw "Target appeared during restoration: $($file.Target)" }
        [IO.File]::Copy($file.Source, $file.Target, $false)
        $created += $file
        if ((Get-FileHash -LiteralPath $file.Target).Hash -ne $file.Hash) { throw 'Restoration hash mismatch.' }
    }
    $stagedManifest = $manifestPath + '.restore-' + [Guid]::NewGuid().ToString('N')
    try {
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stagedManifest -Encoding utf8
        [IO.File]::Move($stagedManifest, $manifestPath) # Refuse concurrent deployment, never overwrite it.
    } finally {
        if (Test-Path -LiteralPath $stagedManifest) { Remove-Item -LiteralPath $stagedManifest }
    }
} catch {
    foreach ($file in ($created | Sort-Object Entry -Descending)) {
        if ((Test-Path -LiteralPath $file.Target) -and (Get-FileHash -LiteralPath $file.Target).Hash -eq $file.Hash) { Remove-Item -LiteralPath $file.Target }
    }
    throw
}
Write-Output 'Restored the exact verified Nine Sols test loader. No saves accessed.'
