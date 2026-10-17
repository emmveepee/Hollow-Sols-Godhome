param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$native=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log"
$hostLines=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log"
$count=0
for($i=0;$i -lt $native.Count;$i++) {
    $line=$native[$i]
    if($line -notmatch 'NS_STRIKE_PARRY_RETIRED encounter=(\S+) target=(-?\d+) targetActivation=(\d+) id=(-?\d+) activation=(\d+) hostFrame=(\d+) nativeFrame=(\d+) damaging=False'){continue}
    $encounter=$Matches[1];$target=$Matches[2];$generation=$Matches[3];$id=$Matches[4];$activation=$Matches[5];$frame=$Matches[7]
    $binding=@($native[0..$i] | Where-Object {$_ -match "NS_ATTACK_BOUND id=$id dealer=(-?\d+) category=Melee"}) | Select-Object -Last 1
    if($null -eq $binding -or $binding -notmatch 'dealer=(-?\d+)'){throw 'Retirement has no native melee dealer binding.'}
    $dealer=$Matches[1]
    if(-not ($native[0..$i] -match "NS_CONTACT_RESOLVED .*kind=parry dealer=$dealer frame=$frame ")){throw 'Retirement did not occur in its accepted native parry callback.'}
    if(-not ($hostLines -match "HK_POOLED_ATTACK id=$id activation=$activation active=True category=Melee name=Slash[123] ")){throw 'Retired strike lacks an actual authored slash activation.'}
    # Scan until the next native activation of this same collider. Other strikes
    # are independent; their verdicts and later pool reuse are allowed.
    for($j=$i+1;$j -lt $native.Count;$j++) {
        if($native[$j] -match "NS_ATTACK_BEGIN id=$id activation=(\d+) encounter=$encounter target=$target targetActivation=$generation ") {
            if([long]$Matches[1] -eq [long]$activation){throw 'A consumed strike rearmed after its parry.'}
            break
        }
        if($native[$j] -match "NS_CONTACT_RESOLVED .*dealer=$dealer frame=\d+ "){throw 'Consumed native dealer resolved again before its next attack activation.'}
    }
    $count++
}
if($count -eq 0){throw 'No accepted native slash retirement was exercised.'}
$partial=[bool](($hostLines+$native) -match 'SESSION_TERMINAL.*phase=Active')
@{session=$Session;sampledParries=$count;activeTerminal=$partial;scope='Same-callback retirement and no rearming before fresh activation. Does not establish all three swings, normal positioning or session completion.'} |
    ConvertTo-Json | Set-Content -LiteralPath "$root/.local/input-tests/$Session/strike-retirement.json"
Write-Output "PASS: $count sampled native slash parries retire in the same callback and cannot rearm before a fresh activation. Active-terminal/partial=$partial. Full combo feel remains unverified."
