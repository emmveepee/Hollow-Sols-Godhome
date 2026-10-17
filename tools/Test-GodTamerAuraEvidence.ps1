param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'GodTamer'){throw 'Wrong encounter.'}
$owners=@([regex]::Matches($hk,'HK_LOCAL_AURA_OWNER entity=(-?\d+) name=(\S+) traits=(\S+) teal=(True|False) present=(True|False) frame=(\d+)'))
$lancer=@($owners | Where-Object {$_.Groups[2].Value -eq 'Lancer' -and $_.Groups[3].Value -eq 'Crimson'})
$beast=@($owners | Where-Object {$_.Groups[2].Value -eq 'Lobster' -and $_.Groups[3].Value -eq 'Kickable'})
if($lancer.Count -lt 2 -or $beast.Count -lt 2){throw 'Need repeated actual native attacks from each body.'}
if(@($lancer | Where-Object {$_.Groups[4].Value -ne 'False' -or $_.Groups[5].Value -ne 'True'}).Count){throw 'Tamer inherited wrong palette.'}
if(@($owners | Where-Object {$_.Groups[2].Value -eq 'Lancer' -and $_.Groups[4].Value -eq 'True'}).Count){throw 'Tamer became teal.'}
if(@($beast | Where-Object {$_.Groups[4].Value -ne 'True' -or $_.Groups[5].Value -ne 'True'}).Count){throw 'Beast lost its Tai-Chi palette.'}
Write-Output "PASS (scoped): native God Tamer body palettes, crimson jumps=$($lancer.Count), teal rolls=$($beast.Count); pixel inspection separate."
