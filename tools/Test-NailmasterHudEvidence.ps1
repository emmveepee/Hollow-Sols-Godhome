param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'OroMato'){throw 'Wrong native fight.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$solo=@([regex]::Matches($hk,'(?m)^(\S+) HK_NAILMASTER_PRESENTATION_PHASE encounter=([\w-]+) paired=False nativePhaseBool=True frame=\d+'))
if($solo.Count -lt 2){throw 'Need repeated native solo-phase entry.'}
foreach($start in $solo){
 $encounter=$start.Groups[2].Value
 $pair=[regex]::Match($hk,"(?m)^(\S+) HK_NAILMASTER_PRESENTATION_PHASE encounter=$encounter paired=True nativePhaseBool=True frame=\d+")
 if(!$pair.Success){throw 'Native paired phase was not reached.'}
 $tail=$hk.Substring($start.Index)
 $next=[regex]::Match($tail,"HK_NAILMASTER_PRESENTATION_PHASE encounter=(?!$encounter)[\w-]+ paired=False")
 if($next.Success){$tail=$tail.Substring(0,$next.Index)}
 $mato=[regex]::Match($tail,'(?m)^(\S+) HK_ROSTER_TARGET entity=(-?\d+) activation=\d+ name=Mato max=\d+ shape=\w+')
 if(!$mato.Success -or [DateTime]::Parse($mato.Groups[1].Value) -lt [DateTime]::Parse($pair.Groups[1].Value)){throw 'Dormant Mato was exported before the authored phase.'}
 if($ns -notmatch "NS_BOSS_LAYOUT encounter=$encounter entity=-?\d+ count=1 index=0 compact=False title=NailmasterOro name=Nailmaster_Oro position=\(0.00, 0.00, 0.00\) scale=\(1.00, 1.00, 1.00\) nativeWidget=True"){throw 'Solo native widget layout/title unproven.'}
 foreach($index in 0..1){
  if($ns -notmatch "NS_BOSS_LAYOUT encounter=$encounter entity=-?\d+ count=2 index=$index compact=True title=BrothersOroMato name=Brothers_Oro_&_Mato "){throw 'Paired compact widget layout/title unproven.'}
 }
}
if($ns -notmatch 'NS_BOSS_LAYOUT encounter=[\w-]+ entity=-?\d+ count=1 index=0 compact=False title=BrothersOroMato name=Brothers_Oro_&_Mato position=\(0.00, 0.00, 0.00\) scale=\(1.00, 1.00, 1.00\) nativeWidget=True'){throw 'Remaining paired-phase widget did not restore the native single layout/title.'}
Write-Output "PASS (presentation only): repeated native Oro solo/paired admission and HUD transitions=$($solo.Count), compact two-bar layouts and restored native single layout retaining the paired title. Pixel appearance, injury/counter correctness and whole-fight completion are separate."
