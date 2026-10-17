[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireSettlement)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-Content -Raw -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log"
$ns=Get-Content -Raw -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log"
if($hk -match 'CONTACT_FAILED|HIT_FAILED' -or $ns -match 'CONTACT_FAILED|HIT_FAILED|NS_MEMORY_FAILED|SUMMON_UI_STOP reason=InvalidDataException'){throw 'Summon runtime failed.'}
$bindings=@([regex]::Matches($ns,'NS_SUMMON_BOUND entity=(-?\d+) generation=(\d+) owner=(-?\d+)'))
if($bindings.Count -lt 4){throw 'Too few live summon bindings.'}
$owners=@([regex]::Matches($ns,'NS_MINION_OWNER_READY owner=(-?\d+) health=(-?\d+) posture=(-?\d+) foo=(-?\d+) independent=True paused=True'))
foreach($field in 1..4){if(@($owners | ForEach-Object {$_.Groups[$field].Value} | Select-Object -Unique).Count -ne $owners.Count){throw 'Native owner identity was shared.'}}
if($ns -notmatch 'NS_SUMMON_OWNER_REUSED'){throw 'Native owner reuse not exercised.'}
if($ns -match 'NS_SUMMON_OWNER_REUSED[^\r\n]*resetVerified=False'){throw 'Dirty receiver reused.'}
$retained=@([regex]::Matches($ns,'NS_SUMMON_(?:BOUND|RETIRED)[^\r\n]*retained=(\d+)') | ForEach-Object {[int]$_.Groups[1].Value})
if($retained.Count -lt 4 -or ($retained | Measure-Object -Maximum).Maximum -gt 64){throw 'Combined live/quarantined receiver budget missing or exceeded.'}
if([regex]::Matches($ns,'NS_SUMMON_DISPOSE retained=0 active=0 pending=0').Count -lt 2){throw 'Each encounter must dispose all native receiver owners.'}
$boundGenerations=@([regex]::Matches($ns,'NS_SUMMON_BOUND entity=(-?\d+) generation=(\d+) owner=(-?\d+) encounter=([^ ]+) frame=(\d+) damage=(\d+) tier=(\d+)'))
if($boundGenerations.Count -ne $bindings.Count){throw 'Binding evidence lacks encounter/frame/damage/tier identity.'}
if($RequireSettlement) {
    foreach($binding in $boundGenerations) {
        $entity=$binding.Groups[1].Value; $activation=$binding.Groups[2].Value; $encounter=$binding.Groups[4].Value
        $damage=$binding.Groups[6].Value; $tier=$binding.Groups[7].Value
        if($ns -notmatch "NS_ATTACK_BEGIN id=-?\d+ activation=$activation encounter=$encounter target=$entity targetActivation=$activation damage=$damage tier=$tier frame=\d+") {throw 'Native summon attack did not retain bound damage/tier/identity.'}
    }
    foreach($kind in @('Melee','Bow','Talisman')) {
        $kills=@([regex]::Matches($hk,"HK_SUMMON_HIT entity=(-?\d+) generation=(\d+) kind=$kind before=[1-9]\d* after=0 sequence=(\d+) encounter=([\w-]+) frame=\d+"))
        $settled=0
        foreach($kill in $kills) {
            $entity=$kill.Groups[1].Value; $activation=$kill.Groups[2].Value; $sequence=$kill.Groups[3].Value; $encounter=$kill.Groups[4].Value
            $scope=[regex]::Match($ns,"(?s)ENCOUNTER_STATE role=NineSols encounter=$encounter geometryFrame=\d+ phase=Restoring.*?(?=ENCOUNTER_STATE role=NineSols encounter=(?!$encounter)[\w-]+|\z)").Value
            if($scope -notmatch "NS_HIT_OUTCOME sequence=$sequence frame=\d+ entity=$entity kind=$kind damage=[\d.E+-]+ encounter=$encounter activation=$activation attack=0"){throw 'Summon host kill lacks matching native target outcome.'}
            if($scope -notmatch "NS_HOST_HIT_RESULT sequence=$sequence result=Applied health=0"){throw 'Summon kill acknowledgement missing.'}
            if($scope -match "NS_SUMMON_SETTLED sequence=$sequence generation=$activation owner=-?\d+ result=Applied health=0"){$settled++}
        }
        if($settled -lt 1){throw "No verified live-owner $kind settlement."}
    }
}
foreach($kind in @('Melee','Bow','Talisman')) { if($hk -notmatch "HK_SUMMON_HIT entity=-?\d+ generation=\d+ kind=$kind before=[1-9]\d* after=0"){throw "No minion $kind kill."} }
$dealers=@([regex]::Matches($ns,'NS_CONTACT_READY owner=(-?\d+) dealer=(-?\d+) parry=-?\d+') | Where-Object {$owner=$_.Groups[1].Value; @($owners | Where-Object {$_.Groups[1].Value -eq $owner}).Count -gt 0} | ForEach-Object {$_.Groups[2].Value})
foreach($kind in @('damage','parry')) { if(-not @($dealers | Where-Object {$ns -match "NS_CONTACT_RESOLVED id=\d+ kind=$kind dealer=$_ "}).Count){throw "No native minion $kind verdict."} }
$images=@([regex]::Matches($ns,'SUMMON_UI_SENT sequence=(\d+) frame=(\d+) hash=(\d+) bytes=4194304'))
$matched=@($images | Where-Object {$hk.Contains("HK_SUMMON_UI_PRESENTED sequence=$($_.Groups[1].Value) frame=$($_.Groups[2].Value) hash=$($_.Groups[3].Value)")})
if($matched.Count -lt 1){throw 'No exact full-size native summon image was presented.'}
if([regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count -lt 2){throw 'Two visible, completed returns required.'}
Write-Output "PASS: $($bindings.Count) identity-scoped summons; independent native owners, pool reuse, melee/bow/talisman kills, native incoming damage/parry, minion artwork transfer and two visible returns. Controller feel remains a separate check."
