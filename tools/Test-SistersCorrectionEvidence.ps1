param([Parameter(Mandatory)][Guid]$Session, [Guid]$HiddenSession = $Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'reason=(gameplay-frame-timeout)|CONTACT_FAILED|HIT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED'){throw 'Sisters regression contains gameplay failure.'}
# Accept only the bounded fixture's ordered host exit while the peer is Ready.
$exit=[regex]::Match($hk,'(?m)^(\S+) SMOKE_EXIT_REQUEST game=HollowKnight frame=\d+')
foreach($terminal in [regex]::Matches($hk+$ns,'(?m)^(\S+) SESSION_TERMINAL ([^\r\n]+)')) {
 if(-not $exit.Success -or $terminal.Groups[2].Value -notmatch 'reason=(VISUAL_CHANNEL_STOPPED|RAGE_MASK_CHANNEL_STOPPED|PEER_DISCONNECTED) .*phase=Ready ' -or [DateTimeOffset]::Parse($terminal.Groups[1].Value) -lt [DateTimeOffset]::Parse($exit.Groups[1].Value)){throw 'Sisters terminal gameplay failure.'}
}
$returns=[regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True')
$wins=[regex]::Matches($hk,'HK_HUB_COMBAT_ENDED victory=True')
if($wins.Count -lt 2 -or $returns.Count -lt 2){throw 'Two victories and visible returns required.'}
if([regex]::Matches($hk,'HK_SISTERS_POOL_BEGIN encounter=').Count -lt 2){throw 'Two multi-body phases required.'}
if($hk -match 'HK_LOCAL_AURA_STYLE') {
 if($hk -notmatch 'HK_LOCAL_AURA_DASH_CAPTURE parts=[23] ' -or $hk -notmatch 'HK_LOCAL_AURA_RETIRED owners=0 shapes=0 cameras=0 textures=0') {throw 'Concurrent live-pose aura or complete teardown absent.'}
} elseif($hk -notmatch 'HK_RAGE_MASK [^\r\n]*parts=[23] '){throw 'Concurrent-body visual mask absent.'}
$hiddenNs=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$HiddenSession"
$hiddenHk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$HiddenSession"
$hidden=[regex]::Matches($hiddenNs,'NS_ATTACHED_HIDDEN_TALISMAN encounter=(\S+) entity=(-?\d+) activation=(\d+) damage=(\S+) frame=(\d+)')
$hiddenSettled=0
foreach($hit in $hidden){
 $enc=$hit.Groups[1].Value;$entity=$hit.Groups[2].Value;$activation=$hit.Groups[3].Value;$frame=$hit.Groups[5].Value
 $outcome=[regex]::Match($hiddenNs,"NS_HIT_OUTCOME sequence=(\d+) frame=$frame entity=$entity kind=Talisman damage=\S+ encounter=$enc activation=$activation ")
 if(-not $outcome.Success){throw 'Hidden native talisman lost before submission.'}
 $seq=$outcome.Groups[1].Value
 $loss=[regex]::Match($hiddenHk,"HK_SUMMON_HIT entity=$entity generation=$activation kind=Talisman before=(\d+) after=(\d+) sequence=$seq encounter=$enc ")
 if($loss.Success -and [int]$loss.Groups[2].Value -lt [int]$loss.Groups[1].Value -and $hiddenNs -match "NS_SUMMON_SETTLED sequence=$seq generation=$activation owner=-?\d+ result=Applied "){$hiddenSettled++}
}
if($hiddenSettled -lt 1){throw 'No actual off-screen attached talisman/native HK loss/ack.'}
# Match actual native verdicts to exact host root collider/activation/action.
$downDamage=0
foreach($m in [regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=(\S+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=(\d+) nativeFrame=(\d+) health=(\d+)')){
 $id=$m.Groups[2].Value;$activation=$m.Groups[3].Value;$action=$m.Groups[4].Value;$hostFrame=[long]$m.Groups[5].Value
 $roots=[regex]::Matches($hk,"HK_POOLED_ATTACK id=$id activation=$activation active=True category=Melee name=Mantis Lord[^\r\n]* action=$action frame=(\d+)")
 foreach($r in $roots){
  $start=[long]$r.Groups[1].Value
  if($start -gt $hostFrame -or $hostFrame-$start -gt 150){continue}
  $windows=[regex]::Matches($hk,"HK_ROSTER_ATTACK_WINDOW entity=-?\d+ state=Dstab_Attack action=$action kick=False crimson=False frame=(\d+)")
  if(@($windows | Where-Object { [long]$_.Groups[1].Value -le $start -and $start-[long]$_.Groups[1].Value -lt 3 }).Count -gt 0){$downDamage++;break}
 }
}
if($downDamage -lt 1){throw 'No downward-window native Yi damage callback.'}
if([regex]::Matches($ns,'NS_SUMMON_DISPOSE retained=0 active=0 pending=0').Count -lt 2){throw 'Two clean receiver teardowns required.'}
Write-Output "PASS (scoped): Sisters session=$Session wins=$($wins.Count) returns=$($returns.Count) hiddenSession=$HiddenSession hiddenTalismanSettlements=$hiddenSettled downwardNativeDamage=$downDamage concurrent presentation and repeated clean receiver teardown. Controlled grants do not prove normal timing or flicker-free human presentation."
