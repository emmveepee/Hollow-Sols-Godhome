param([Parameter(Mandatory)][Guid]$Session, [Parameter(Mandatory)][string]$Boss, [Guid]$Encounter=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($hk -match 'HK_TARGET_FIXTURE_FAILED'){throw 'Diagnostic targeting made no progress; this run cannot certify outgoing damage or encounter completion.'}
if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne $Boss){throw 'Evidence belongs to another requested boss.'}
function Select-Generation([string]$text,[string]$role,[string]$phase,[string]$identity){
        $start=[regex]::Match($text,"ENCOUNTER_STATE role=$role encounter=$identity geometryFrame=\d+ phase=$phase")
        if(-not $start.Success){throw "Missing requested $role generation."}
        $tail=$text.Substring($start.Index)
        $next=[regex]::Match($tail,"ENCOUNTER_STATE role=$role encounter=(?!$identity)[\w-]+ geometryFrame=\d+ phase=$phase")
        if($next.Success){return $tail.Substring(0,$next.Index)}
        return $tail
}
if($Encounter -ne [Guid]::Empty){
    # A completed generation may be assessed separately from a later timer-truncated
    # challenge. Full-session failure checking remains the default.
    $hk=Select-Generation $hk 'HollowKnight' 'Preparing' "$Encounter"
    $ns=Select-Generation $ns 'NineSols' 'Restoring' "$Encounter"
    if($hk -notmatch 'HK_HUB_COMBAT_ENDED victory=True' -or $hk -notmatch 'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True' -or $ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$Encounter geometryFrame=\d+ phase=Ready"){throw 'Requested generation did not complete native victory and visible return.'}
}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL|NS_REST|HK_ENCOUNTER_GATE|HK_PRESENTATION_FIXTURE)_FAILED|CONTACT_STOP reason=(?:InvalidDataException|InvalidOperationException)|CONTACT_CHANNEL_STOPPED|HIT_CHANNEL_STOPPED|SESSION_TERMINAL[^\r\n]*phase=(Preparing|Active|Victory|Defeated|Restoring)'){throw 'Roster runtime failure; preserve session evidence.'}
$targets=@([regex]::Matches($hk,'HK_ROSTER_TARGET entity=(-?\d+) activation=(\d+) name=([^\r\n]+?) max=(\d+) shape=(\w+)'))
if($targets.Count -eq 0){throw 'No native roster receiver discovered.'}
$hits=@([regex]::Matches($hk,'HK_SUMMON_HIT entity=(-?\d+) generation=(\d+) kind=(\w+) before=(\d+) after=(\d+) sequence=(\d+) encounter=([\w-]+) frame=(\d+)'))
$applied=0
$nativeGenerations=@{}
function Index-NativeGeneration([string]$native,[string]$identity){
    $outcomes=@{}; $bindings=@{}; $settlements=@{}
    foreach($m in [regex]::Matches($native,'NS_HIT_OUTCOME sequence=(\d+) frame=\d+ entity=(-?\d+) kind=(\w+) damage=[\d.E+-]+ encounter=([\w-]+) activation=(\d+) attack=\d+')){
        if($m.Groups[4].Value -eq $identity){$outcomes["$($m.Groups[1].Value)/$($m.Groups[2].Value)/$($m.Groups[5].Value)/$($m.Groups[3].Value)"]=$true}
    }
    foreach($m in [regex]::Matches($native,'NS_SUMMON_BOUND entity=(-?\d+) generation=(\d+) owner=(-?\d+) encounter=([\w-]+) ')){
        if($m.Groups[4].Value -eq $identity){$bindings["$($m.Groups[1].Value)/$($m.Groups[2].Value)"]=$m.Groups[3].Value}
    }
    foreach($m in [regex]::Matches($native,'NS_SUMMON_SETTLED sequence=(\d+) generation=(\d+) owner=(-?\d+) result=(Applied|Refused) health=(\d+)(?:\s|$)')){
        $key="$($m.Groups[1].Value)/$($m.Groups[2].Value)/$($m.Groups[3].Value)"
        $value="$($m.Groups[4].Value)/$($m.Groups[5].Value)"
        if($settlements.ContainsKey($key) -and $settlements[$key] -ne $value){throw "Conflicting native settlement $identity/$key"}
        $settlements[$key]=$value
    }
    return @{outcomes=$outcomes;bindings=$bindings;settlements=$settlements}
}
foreach($h in $hits){
    $entity=$h.Groups[1].Value; $gen=$h.Groups[2].Value; $seq=$h.Groups[6].Value; $enc=$h.Groups[7].Value; $hp=$h.Groups[5].Value
    if(-not $nativeGenerations.ContainsKey($enc)){$nativeGenerations[$enc]=Index-NativeGeneration (Select-Generation $ns 'NineSols' 'Restoring' $enc) $enc}
    $native=$nativeGenerations[$enc]
    if(-not $native.outcomes.ContainsKey("$seq/$entity/$gen/$($h.Groups[3].Value)")){throw "Missing matching outgoing outcome $enc/$seq"}
    $owner=$native.bindings["$entity/$gen"]
    if(-not $owner){throw "Missing exact native owner binding $enc/$entity/$gen"}
    $settled=$native.settlements["$seq/$gen/$owner"]
    if($settled -notin @("Applied/$hp","Refused/$hp") -or ([int]$hp -lt [int]$h.Groups[4].Value -and $settled -ne "Applied/$hp")){throw "Missing target-scoped settlement $enc/$seq"}
    if([int]$h.Groups[5].Value -lt [int]$h.Groups[4].Value){$applied++}
}
if($applied -eq 0 -or $ns -notmatch 'NS_CONTACT_RESOLVED id=\d+ kind=(damage|parry)'){throw 'No bidirectional native combat evidence.'}
$returns=[regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count
$victories=[regex]::Matches($hk,'HK_HUB_COMBAT_ENDED victory=True').Count
if($Boss -eq 'WatcherKnights') {if($ns -match 'NS_BOSS_HUD_BOUND'){throw 'Watcher fight acquired a global boss bar.'}}
elseif($ns -notmatch 'NS_BOSS_HUD_BOUND native=True'){throw 'Missing native per-target boss HUD.'}
Write-Output "PASS (scoped): $Boss session=$Session encounter=$Encounter targets=$($targets.Count) acknowledgedDamage=$applied nativeReturns=$returns nativeVictories=$victories. Timer ending, fixture grants and isolated attack verdicts do not establish complete fight coverage or human feel."
