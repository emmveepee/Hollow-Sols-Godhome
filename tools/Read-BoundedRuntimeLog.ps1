# Shared evidence reader for the logger's known, bounded files. No directory scan.
function Get-BoundedRuntimeLog {
    param([Parameter(Mandatory)][string]$Run, [switch]$Lines)
    if(-not (Test-Path -LiteralPath "$Run/plugin.log")){throw "Missing runtime log: $Run"}
    $records=foreach($suffix in @('.startup','.2','.1','')){
        $path="$Run/plugin.log$suffix"
        if(Test-Path -LiteralPath $path){Get-Content -LiteralPath $path}
    }
    # Startup excerpts overlap rotation. Exact duplicate records count only once.
    $ordered=@($records | Sort-Object -Unique -CaseSensitive)
    if($Lines){return $ordered}
    return ($ordered -join "`n")
}
