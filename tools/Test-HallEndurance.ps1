[CmdletBinding()]
param([string[]]$Boss=@(),[ValidateRange(300,600)][int]$Seconds=340,[switch]$RestartEachFight,[string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
# The canonical production profiles, in their declared order. No arena variants
# or audit-only profiles are silently substituted for Hall statue identities.
$source=[IO.File]::ReadAllText((Join-Path $root 'src/Bridge/EncounterProfile.cs'))
$declared=@{}
foreach($m in [regex]::Matches($source,'public static readonly EncounterProfile (\w+) = (?:new EncounterProfile|Rough)\("(\w+)"')){$declared[$m.Groups[1].Value]=$m.Groups[2].Value}
$members=[regex]::Match($source,'EncounterProfile\[\] All = \{([^}]+)\}').Groups[1].Value.Split(',')
$allowed=@($members | ForEach-Object { $key=$declared[$_.Trim()];if(-not$key){throw 'Cannot resolve a production Hall profile.'};$key })
$allowed += "VengeflyKingAscended"
if(-not$Boss.Count){$Boss=$allowed}
if(@($Boss | Select-Object -Unique).Count -ne $Boss.Count -or @($Boss | Where-Object {$_ -notin $allowed}).Count){throw 'Expected unique production Hall profiles.'}
$folder=Join-Path $root ('.local/hall-endurance/'+[Guid]::NewGuid())
$null=New-Item -ItemType Directory -Path $folder
$rows=@()
if(-not$RestartEachFight){
    Start-Sleep -Seconds 5
    Write-Output "HALL_ENDURANCE_CONTINUOUS_START count=$($Boss.Count) seconds=$($Boss.Count*240+180) reports=$folder"
    $failure=$null
    try {
        & "$PSScriptRoot/Test-NativeMenuFoundation.ps1" -Scenario combat-Endurance-Sweep -EndurancePlan $Boss -ExistingFixture current -Seconds ($Boss.Count*240+180) 2>&1 | Tee-Object -FilePath "$folder/continuous.txt" | ForEach-Object {Write-Output "$_"}
    }catch{$failure=$_.Exception.Message;Write-Output "HALL_ENDURANCE_CONTINUOUS_FAILED reason=$failure"}
    $latest=Get-ChildItem "$root/.local/pre-v01-native" -Directory | Sort-Object CreationTime -Descending | Select-Object -First 1
    $plan=Get-Content -LiteralPath "$($latest.FullName)/HollowKnight/endurance-plan.request"
    if((($plan | Select-Object -Skip 1) -join ',') -ne ($Boss -join ',')){throw 'Continuous evidence plan does not match.'}
    & $Python -X utf8 "$PSScriptRoot/Measure-RadiancePerformance.py" $latest.FullName > "$folder/continuous-performance.txt"
    if($LASTEXITCODE -eq 0){$performance=Get-Content "$($latest.FullName)/radiance-performance.json" -Raw | ConvertFrom-Json}else{$performance=$null}
    for($index=0;$index -lt $Boss.Count;$index++){
        $name=$Boss[$index];$receipt="$($latest.FullName)/HollowKnight/endurance-$index.complete"
        $completed=(Test-Path -LiteralPath $receipt) -and ([IO.File]::ReadAllText($receipt) -eq "$($latest.Name)`n$name")
        $sample=if($performance -and $performance.PSObject.Properties[$name]){$performance.$name}else{$null}
        $rows += [pscustomobject]@{boss=$name;fixture=$latest.Name;nativeFixturePass=$completed;failure=$(if($completed){$null}else{$failure});performance=$sample;continuousSession=$true;playerAcceptanceChanged=$false}
    }
    $rows | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$folder/results.json"
    Write-Output "HALL_ENDURANCE_CONTINUOUS_RECORDED fixture=$($latest.Name) completed=$(@($rows | Where-Object nativeFixturePass).Count) reports=$folder"
    if($failure){throw $failure}
    return
}
foreach($name in $Boss){
    if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw "Games remain open. Stop sweep and preserve evidence: $folder"}
    # Give Steam time to finish observing both process exits/cloud teardown.
    Start-Sleep -Seconds 5
    Write-Output "HALL_ENDURANCE_START boss=$name utc=$([DateTime]::UtcNow.ToString('O')) reports=$folder"
    $row=[ordered]@{boss=$name;fixture=$null;nativeFixturePass=$false;failure=$null;performance=$null;playerAcceptanceChanged=$false}
    try {
        & "$PSScriptRoot/Test-NativeMenuFoundation.ps1" -Scenario "combat-Endurance-$name" -ExistingFixture current -Seconds $Seconds 2>&1 | Tee-Object -FilePath "$folder/$name.txt" | ForEach-Object {Write-Output "$_"}
        $row.nativeFixturePass=$true
    }catch{
        $row.failure=$_.Exception.Message
        Add-Content -LiteralPath "$folder/$name.txt" -Value $row.failure
        Write-Output "HALL_ENDURANCE_FAILED boss=$name reason=$($row.failure)"
    }
    $latest=Get-ChildItem "$root/.local/pre-v01-native" -Directory | Sort-Object CreationTime -Descending | Select-Object -First 1
    $fixture=Get-Content -LiteralPath "$($latest.FullName)/HollowKnight/native-menu.fixture"
    if($fixture[2] -ne "combat-Endurance-$name"){throw 'Evidence identity does not match this sweep entry.'}
    $row.fixture=$latest.Name
    if($row.nativeFixturePass){
        & $Python -X utf8 "$PSScriptRoot/Measure-RadiancePerformance.py" $latest.FullName > "$folder/$name-performance.txt"
        if($LASTEXITCODE -ne 0){throw 'Performance evidence analysis failed.'}
        $row.performance=Get-Content "$($latest.FullName)/radiance-performance.json" -Raw | ConvertFrom-Json
        if(-not$row.performance.PSObject.Properties[$name]){throw 'Missing three-minute workload markers.'}
    }
    $rows += [pscustomobject]$row
    $rows | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$folder/results.json"
    if($row.failure -like '*fingerprints changed*'){throw 'Protected save fingerprint failure; do not continue.'}
    Write-Output "HALL_ENDURANCE_RECORDED boss=$name fixture=$($row.fixture) nativeFixturePass=$($row.nativeFixturePass) reports=$folder"
    if($row.failure -and ($row.failure -match 'did not start|launch ticket|deployment changed|not deployed|Steam did not acknowledge|finite deadline')){
        throw "Infrastructure failure; stop rather than consume the remaining roster without tests. Preserve $folder"
    }
}
Write-Output "HALL_ENDURANCE_COMPLETE reports=$folder attempted=$($rows.Count) nativeCompleted=$(@($rows | Where-Object nativeFixturePass).Count)"
