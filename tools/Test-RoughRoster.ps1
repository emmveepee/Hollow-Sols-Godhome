[CmdletBinding()]
param(
    [string[]]$Boss=@('BroodingMawlek','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm'),
    [ValidateRange(200,600)][int]$Seconds=200,
    [switch]$TraceProbe,
    [ValidateRange(0,2)][int]$Tier=0,
    [switch]$VictoryOnly
)
$ErrorActionPreference='Stop'
if($VictoryOnly -and $Tier -ne 2){throw 'VictoryOnly requires Radiant difficulty.'}
$root=Split-Path $PSScriptRoot -Parent
$allowed=@('BroodingMawlek','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','MassiveMossCharger','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm','MantisLords','SistersOfBattle','Grimm','NightmareKingGrimm','NailsageSly','WatcherKnights','FalseKnight','FailedChampion','Uumuu')
if($Boss.Count -eq 0 -or @($Boss | Select-Object -Unique).Count -ne $Boss.Count -or @($Boss | Where-Object {$_ -notin $allowed}).Count){throw 'Expected unique admitted rough profiles.'}
$folder=Join-Path $root ('.local/broad-roster/batch-'+[Guid]::NewGuid())
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$results=@()
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
foreach($name in $Boss){
    if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw "Games remain open; preserve evidence and stop batch. Reports: $folder"}
    Write-Output "ROSTER_TEST_START boss=$name utc=$([DateTime]::UtcNow.ToString('o')) reports=$folder"
    $runOutput=New-Object System.Collections.Generic.List[string];$problem=$null;$passed=$false
    try { & "$PSScriptRoot/Test-InputBridge.ps1" -HubCombat -CheatProbe -Boss $name -Tier $Tier -InvulnerableProbe:$VictoryOnly -TraceProbe:$TraceProbe -DiagnosticSeconds $Seconds 2>&1 | ForEach-Object { $runOutput.Add("$_"); Write-Output "$_" }; $passed=$true }
    catch {$problem=$_.Exception.Message; Write-Output "ROSTER_TEST_FAILED boss=$name reason=$problem"}
    $log=($runOutput | ForEach-Object {"$_"}) -join "`n"
    $log | Set-Content -LiteralPath "$folder/$name.txt"
    $match=[regex]::Match($log+' '+$problem,'(?:session=|input-tests[\\/])([a-f0-9-]{36})')
    $row=[ordered]@{boss=$name;session=$null;tier=$Tier;victoryOnly=[bool]$VictoryOnly;fullSessionPass=$passed;failure=$problem;completedEncounters=@();targets=0;damagingHits=0;victories=0;returns=0}
    if($match.Success){
        $identity=$match.Groups[1].Value;$row.session=$identity
        $hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$identity"
        $row.targets=[regex]::Matches($hk,'HK_ROSTER_TARGET ').Count
        $row.damagingHits=@([regex]::Matches($hk,'HK_SUMMON_HIT[^\r\n]*before=(\d+) after=(\d+)') | Where-Object {[int]$_.Groups[2].Value -lt [int]$_.Groups[1].Value}).Count
        $row.victories=[regex]::Matches($hk,'HK_HUB_COMBAT_ENDED victory=True').Count
        $row.returns=[regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count
        foreach($generation in [regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([a-f0-9-]{36}) geometryFrame=\d+ phase=Preparing')){
            $enc=$generation.Groups[1].Value
            try { & "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $identity -Boss $name -Encounter $enc | Add-Content -LiteralPath "$folder/$name.txt"; $row.completedEncounters+= $enc } catch { }
        }
    }
    $results+=[pscustomobject]$row
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$folder/results.json"
    Write-Output "ROSTER_TEST_RESULT boss=$name fullSessionPass=$passed targets=$($row.targets) damagingHits=$($row.damagingHits) victories=$($row.victories) returns=$($row.returns) scopedCompleted=$($row.completedEncounters.Count)"
}
Write-Output "ROSTER_TEST_COMPLETE reports=$folder"
