param([Parameter(Mandatory)][Guid]$Session,[ValidateRange(3,8)][int]$MinimumEncounters=6)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$begins=@([regex]::Matches($hk,'HK_HUB_COMBAT_BEGIN scene=GG_Collector geometry=(\d+) nativeStatue=True'))
$wins=[regex]::Matches($hk,'HK_HUB_COMBAT_ENDED victory=True').Count
$returns=[regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count
if($begins.Count -lt $MinimumEncounters -or $wins -lt $MinimumEncounters -or $returns -lt $MinimumEncounters){throw 'Repeated native Collector fights/complete returns are unexercised.'}
$bounds=@([regex]::Matches($ns,'NS_SUMMON_BOUND entity=(-?\d+) generation=(\d+) owner=(-?\d+) encounter=([\w-]+)[^\r\n]* health=(\d+) maxHealth=(\d+) hiddenHealthBar=False nativeHealthWidget=True'))
$encounters=@{};$owners=@{};$damaged=0;$reused=0
foreach($bound in $bounds){
 $entity=$bound.Groups[1].Value;$generation=$bound.Groups[2].Value;$owner=$bound.Groups[3].Value;$enc=$bound.Groups[4].Value
 if([int]$bound.Groups[5].Value -le 0){throw 'A dead minion was bound as a live widget.'}
 $visibility="NS_SUMMON_HUD_VISIBILITY encounter=$enc geometry=\d+ target=$entity activation=$generation visible=True hurtActive=True hp=[1-9]\d* frame=\d+"
 if($ns -notmatch $visibility){throw "Minion $entity/$generation in $enc never presented an active native HP widget."}
 if($ns -notmatch "NS_SUMMON_HUD_VALUES encounter=$enc target=$entity activation=$generation health=[1-9]\d* max=[1-9]\d*"){throw 'Visible minion widget lacks actual health values.'}
 $encounters[$enc]=$true
 $ownerKey=$enc+':'+$owner
 if($owners.ContainsKey($ownerKey)){$reused++}
 $owners[$ownerKey]=$true
 $hits=@([regex]::Matches($hk,"HK_SUMMON_HIT entity=$entity generation=$generation kind=\w+ before=(\d+) after=(\d+) sequence=\d+ encounter=$enc frame=\d+"))
 if(@($hits|Where-Object {[int]$_.Groups[2].Value -lt [int]$_.Groups[1].Value}).Count){$damaged++}
}
if($encounters.Count -lt $MinimumEncounters -or $reused -lt $MinimumEncounters -or $damaged -lt $MinimumEncounters){throw 'Repeated minion widget, pool reuse or actual native damage coverage is insufficient.'}
$disposals=[regex]::Matches($ns,'NS_SUMMON_DISPOSE retained=0 active=0 pending=0 settlementMetadata=0').Count
if($disposals -lt $MinimumEncounters){throw 'Native minion contexts did not completely drain after repeated fights.'}
Write-Output "PASS (scoped): session=$Session Collector encounters=$($encounters.Count) wins=$wins returns=$returns; every $($bounds.Count) admitted live minion widget became visible with native health; reused owners=$reused actual damaged minions=$damaged fully drained contexts=$disposals. Other families/tiers and physical HUD pixel QA remain separate."
