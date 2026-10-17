param([Parameter(Mandatory)][Guid]$Session,[Parameter(Mandatory)][Guid]$Before)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$old=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Before"
foreach($id in @($Session,$Before)) {
 if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$id/encounter-profile.request" -Raw).Trim() -ne 'NightmareKingGrimm'){throw 'Wrong encounter profile.'}
}
if(($hk+$ns)-match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Runtime integration failed.'}
$oldGui=[regex]::Matches($old,'HK_WORK_COST stage=gui_\S+ samples=\d+ meanMs=([\d.]+) maxMs=([\d.]+)')
if(!($oldGui | Where-Object {[double]$_.Groups[2].Value -ge 15})){throw 'Missing measured original contour bottleneck.'}
if($old -notmatch 'HK_FIREBAT_ADMISSION [^\r\n]*child=Hero_Hurter [^\r\n]*damage=0 hazard=1 category= objectActive=True'){throw 'Missing native recycled zero-damage bat state.'}
$mesh=[regex]::Matches($hk,'HK_WORK_COST stage=contour_mesh samples=(\d+) meanMs=([\d.]+) maxMs=([\d.]+) totalMs=[\d.]+ units=(\d+)')
$denseMesh=@($mesh | Where-Object {[double]$_.Groups[4].Value/[int]$_.Groups[1].Value -ge 2000})
if($denseMesh.Count -lt 3){throw 'Insufficient dense exact-contour rendering.'}
foreach($m in $denseMesh){if([double]$m.Groups[2].Value -gt 8){throw 'Batched overlay exceeds focused CPU budget.'}}
$denseFrames=@()
$latestCost=$null
foreach($line in ($hk -split "`n")) {
 $cost=[regex]::Match($line,'HK_WORK_COST stage=contacts samples=(\d+).*units=(\d+)')
 if($cost.Success){$latestCost=$cost}
 $frame=[regex]::Match($line,'FRAME_PERF scene=GG_Grimm_Nightmare frames=(\d+) meanMs=([\d.]+)')
 if($frame.Success -and $latestCost -and [double]$latestCost.Groups[2].Value/[int]$latestCost.Groups[1].Value -ge 30){$denseFrames+=[double]$frame.Groups[2].Value}
}
if($denseFrames.Count -lt 5 -or ($denseFrames | Measure-Object -Maximum).Maximum -gt 16.667){throw 'Missing sustained dense host-frame improvement; individual tails remain separate.'}
$returned=[regex]::Match($hk,'ENCOUNTER_STATE role=HollowKnight encounter=\S+ geometryFrame=\d+ phase=Returned')
if(!$returned.Success){throw 'No native return before retry.'}
$retry=$hk.Substring($returned.Index)
if($retry -notmatch 'HK_POOLED_REGISTER name=Hero Hurter scene=DontDestroyOnLoad'){throw 'No flying bat hitbox registered after retry.'}
$damage=@{}
foreach($impact in [regex]::Matches($retry,'HK_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) [^\r\n]*nativeImpact=True root=Nightmare Firebat\(Clone\)')) {
 $id=$impact.Groups[1].Value;$activation=$impact.Groups[2].Value
 if($ns -match "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=damage reflected=False"){$damage["$id/$activation"]=$true}
}
if($damage.Count -lt 2){throw 'Need two actual native bat damage/impact pairs after return/reentry.'}
Write-Output "PASS (scoped): measured original contour cost/recycled zero damage; dense batched mesh windows=$($denseMesh.Count), dense host windows=$($denseFrames.Count) maximumMeanMs=$(($denseFrames|Measure-Object -Maximum).Maximum), post-return native bat damage/impact pairs=$($damage.Count). Individual frame tails, fresh-registry zero-damage reproduction and human control acceptance remain separate."
