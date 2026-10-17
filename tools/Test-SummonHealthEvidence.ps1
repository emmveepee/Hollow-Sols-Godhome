param([Parameter(Mandatory)][Guid]$Session, [Parameter(Mandatory)][Guid]$Encounter)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$rows=(Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session") -split '\r?\n'
$values=@{}; $settled=0; $targets=@{}; $injurySeen=$false
foreach($row in $rows){
    if($row -notmatch "encounter=$Encounter "){continue}
    if($row -match 'NS_SUMMON_HUD_VALUES.*target=(-?\d+) activation=(\d+) health=(\d+) max=(\d+) injury=([\d.E+-]+)'){
        $key="$($Matches[1])/$($Matches[2])"
        $values[$key]=@{health=[int]$Matches[3];maximum=[int]$Matches[4];injury=[double]$Matches[5]}
        $injurySeen=$injurySeen -or [double]$Matches[5] -gt 0
    }
    if($row -match 'NS_SUMMON_HUD_TRAIL.*target=(-?\d+) activation=(\d+) health=(\d+) injury=([\d.E+-]+) stableSeconds=([\d.E+-]+) solidAngle=([\d.E+-]+) trailAngle=([\d.E+-]+) injuryAngle=([\d.E+-]+) settled=(True|False)'){
        $key="$($Matches[1])/$($Matches[2])"; $v=$values[$key]
        if(-not $v -or $v.health -ne [int]$Matches[3] -or $v.injury -ne [double]$Matches[4]){throw 'Trail sample lacks matching unchanged target values.'}
        if([double]$Matches[5] -lt 1.25 -or $Matches[9] -ne 'True'){throw 'Native damage trail did not finish its authored delay/decay.'}
        $maximum=$v.maximum/(21.0/40.0)
        $injury=[Math]::Min($v.injury,$v.health/(21.0/40.0))
        $solid=[Math]::Floor([Math]::Max([double]0,[double]($v.health/(21.0/40.0)-$injury)))
        $expected=[Math]::PI/2*(1-$solid/$maximum)
        $expectedInjury=[Math]::PI/2*(1-($solid+$injury)/$maximum)
        if([Math]::Abs([double]$Matches[6]-$expected) -gt 0.0001 -or [Math]::Abs([double]$Matches[7]-$expected) -gt 0.001 -or [Math]::Abs([double]$Matches[8]-$expectedInjury) -gt 0.0001){throw "Native angles disagree: target=$key health=$($v.health) max=$($v.maximum) injury=$injury actual=$($Matches[6])/$($Matches[7])/$($Matches[8]) expected=$expected/$expectedInjury"}
        $settled++; $targets[$key]=$true
    }
}
if($settled -lt 2 -or $targets.Count -lt 2){throw 'Insufficient independent native trail samples.'}
Write-Output "PASS scoped: session=$Session encounter=$Encounter nativeSettledSamples=$settled independentTargets=$($targets.Count) injuryValueObserved=$injurySeen. Actual Disc angles match native quantization and delay/decay; inventory flashing, complete injury animations and human readability remain separate."
