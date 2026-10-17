param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$lines=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session" -Lines
$clips=@($lines | Where-Object { $_ -match 'HK_ENTRY_ART scene=GG_Gruz_Mother state=GG_Extra_Pause clip=Fly frame=\d+' })
if($clips.Count -lt 2){throw 'Two identified native flying introductions are required.'}
if($lines -match 'HK_ENCOUNTER_HELD state=GG_Boss_Wake|SESSION_TERMINAL.*phase=(Preparing|Active|Restoring|Defeated|Victory)'){throw 'Native wake was held or gameplay failed.'}
foreach($clip in $clips){
    $index=[Array]::IndexOf($lines,$clip)
    $start=$index-1
    while($start -ge 0 -and $lines[$start] -notmatch 'HK_HUB_COMBAT_BEGIN'){ $start-- }
    if($start -lt 0 -or -not ($lines[$start..$index] -match 'HK_BODY_STATE Big Fly Control:Wake;')){throw 'Flying clip has no preceding native wake in its encounter.'}
    $end=$index+1
    while($end -lt $lines.Count -and $lines[$end] -notmatch 'HK_HUB_COMBAT_ENDED'){ $end++ }
    if($end -ge $lines.Count -or -not ($lines[$index..$end] -match 'HK_BODY_STATE Big Fly Control:Buzz;')){throw 'Introduction did not proceed into native combat.'}
}
Write-Output "PASS: $($clips.Count) native Wake -> Fly introductions; readiness held only the later native pause and combat continued. Full fade artwork remains human acceptance."
