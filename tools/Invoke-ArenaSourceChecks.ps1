# Shared runner for linked production-source host checks. Fault copies live only
# in a unique temp directory; production files are never rewritten.
function Invoke-ArenaCheckProcess([string]$Project, [string[]]$Properties) {
    $output = (& dotnet run --project $Project -c Release @Properties 2>&1 | Out-String)
    return @{ Code=$LASTEXITCODE; Output=$output }
}
function Test-ArenaSourceFaults([string]$Project, [string]$Token, [string[]]$Properties, [object[]]$Faults) {
    $baseline=Invoke-ArenaCheckProcess $Project $Properties
    if($baseline.Code -ne 0 -or -not $baseline.Output.Contains($Token)) { throw $baseline.Output }
    Write-Output $baseline.Output.Trim()
    $scratch=Join-Path ([IO.Path]::GetTempPath()) ('arena-balance-checks-'+[Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    foreach($fault in $Faults) {
        # Anchors are compared with LF line endings on both sides: a checkout with core.autocrlf
        # has CRLF sources while multi-line Find/Replace literals are written with `n, which made
        # every multi-line anchor report 'missing' (PoolPrewarmChecks, 2026-10-23). The copy the
        # mutant compiles from is LF too; the production file itself is never rewritten.
        $source=[IO.File]::ReadAllText($fault.Path).Replace("`r`n","`n")
        $find=([string]$fault.Find).Replace("`r`n","`n")
        $replace=([string]$fault.Replace).Replace("`r`n","`n")
        if(-not $source.Contains($find)) { throw ('Fault anchor missing: '+$fault.Name) }
        $before=(Get-FileHash -LiteralPath $fault.Path -Algorithm SHA256).Hash
        $copy=Join-Path $scratch ($fault.Name+'.cs')
        [IO.File]::WriteAllText($copy,$source.Replace($find,$replace))
        $result=Invoke-ArenaCheckProcess $Project ($Properties+@('-p:'+$fault.Property+'='+$copy))
        if($result.Code -eq 0 -or -not $result.Output.Contains('FAIL: '+$fault.Failure)) { throw ('Fault was not killed by intended assertion: '+$fault.Name+"`n"+$result.Output) }
        if((Get-FileHash -LiteralPath $fault.Path -Algorithm SHA256).Hash -ne $before) { throw 'Production source changed during check' }
    }
    $final=Invoke-ArenaCheckProcess $Project $Properties
    if($final.Code -ne 0 -or -not $final.Output.Contains($Token)) { throw $final.Output }
    Write-Output ('Source faults rejected: '+$Faults.Count)
}
