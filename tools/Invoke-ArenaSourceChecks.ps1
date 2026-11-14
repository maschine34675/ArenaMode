# Shared runner for linked production-source host checks. Fault copies live only in a unique temp directory;
# production files are never rewritten.
#
# Test-ArenaSourceFaults runs the baseline (dotnet run, which restores and builds), then every fault: the copy
# of the production file with Find replaced by Replace is compiled into its own intermediate and output
# directories under the project's obj/ and bin/ (dotnet build --no-restore, reusing the baseline's restore) and
# the program runs from there.
# Each fault must make the program exit non-zero AND print "FAIL: <Failure>"; a fault that does not compile
# fails the same way (its output carries no such line). The faults build and run side by side,
# ARENA_MUTANT_PARALLEL at a time (default: 8, or what Run-ArenaVerification.ps1 sets for its workers; 1 runs
# them one after another): a mutant costs about 0.3 s of compile and program and about 2.5 s of process start,
# evaluation and restore check, so a sequential dotnet run per mutant spent most of its time waiting
# (KitPickerChecks: 229 s sequential with dotnet run, 37 s at 4, 24 s at 8). Running side by side cannot let
# a surviving mutant pass: every fault still needs its own non-zero exit and its own named assertion, and a
# collision between two mutant processes shows up as a fault that was not killed. The report keeps the order
# of $Faults.
function Invoke-ArenaCheckProcess([string]$Project, [string[]]$Properties) {
    $output = (& dotnet run --project $Project -c Release @Properties 2>&1 | Out-String)
    return @{ Code=$LASTEXITCODE; Output=$output }
}
function Test-ArenaSourceFaults([string]$Project, [string]$Token, [string[]]$Properties, [object[]]$Faults) {
    $baseline=Invoke-ArenaCheckProcess $Project $Properties
    if($baseline.Code -ne 0 -or -not $baseline.Output.Contains($Token)) { throw $baseline.Output }
    Write-Output $baseline.Output.Trim()
    $runId=[Guid]::NewGuid().ToString('N').Substring(0,12)
    $scratch=Join-Path ([IO.Path]::GetTempPath()) ('arena-balance-checks-'+$runId)
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    # The program of a mutant runs from inside its project, at the depth of bin/Release/<tfm>/, as under
    # dotnet run: programs find the repository by walking up from their own directory (CustomKitChecks,
    # FailedStartAbortChecks), and a DLL under %TEMP% finds none. obj/ and bin/ are ignored by git and by the
    # project's default globs.
    $projectDir=Split-Path -Parent ([IO.Path]::GetFullPath($Project))
    $mutantBin=Join-Path $projectDir "bin\m-$runId"
    $mutantObj=Join-Path $projectDir "obj\m-$runId"
    $targetFile=((& dotnet msbuild $Project -getProperty:TargetFileName -p:Configuration=Release @Properties 2>&1) | Out-String).Trim()
    if(-not $targetFile -or $targetFile -notmatch '\.dll$') { throw "Cannot evaluate TargetFileName of ${Project}: $targetFile" }
    $jobs=@()
    for($i=0; $i -lt $Faults.Count; $i++) {
        $fault=$Faults[$i]
        # Anchors are compared with LF line endings on both sides: a checkout with core.autocrlf
        # has CRLF sources while multi-line Find/Replace literals are written with `n, which made
        # every multi-line anchor report 'missing' (PoolPrewarmChecks, 2026-10-23). The copy the
        # mutant compiles from is LF too; the production file itself is never rewritten.
        $source=[IO.File]::ReadAllText($fault.Path).Replace("`r`n","`n")
        $find=([string]$fault.Find).Replace("`r`n","`n")
        $replace=([string]$fault.Replace).Replace("`r`n","`n")
        if(-not $source.Contains($find)) { throw ('Fault anchor missing: '+$fault.Name) }
        $directory=Join-Path $scratch ('{0:D3}-{1}' -f $i,($fault.Name -replace '[^A-Za-z0-9_.-]','_'))
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $copy=Join-Path $directory ($fault.Name+'.cs')
        [IO.File]::WriteAllText($copy,$source.Replace($find,$replace))
        $jobs+=[pscustomobject]@{ Index=$i; Name=$fault.Name; Path=$fault.Path; Failure=$fault.Failure
            Before=(Get-FileHash -LiteralPath $fault.Path -Algorithm SHA256).Hash
            Build=@($Properties + @(('-p:'+$fault.Property+'='+$copy), ('-p:IntermediateOutputPath={0}\{1:D3}\' -f $mutantObj,$i), ('-p:OutDir={0}\{1:D3}\' -f $mutantBin,$i)))
            Program=('{0}\{1:D3}\{2}' -f $mutantBin,$i,$targetFile) }
    }
    try {
    $throttle=if($env:ARENA_MUTANT_PARALLEL -as [int]) { [Math]::Max(1,[int]$env:ARENA_MUTANT_PARALLEL) } else { 8 }
    $here=(Get-Location).Path
    $results=@($jobs | ForEach-Object -ThrottleLimit $throttle -Parallel {
        Set-Location -LiteralPath $using:here   # the program runs where dotnet run would have run it
        $output=(& dotnet build $using:Project -c Release --no-restore --nologo -v:q @($_.Build) 2>&1 | Out-String)
        $code=$LASTEXITCODE
        if($code -eq 0) { $output=(& dotnet $_.Program 2>&1 | Out-String); $code=$LASTEXITCODE }
        else { $output="build failed (exit $code):`n$output" }
        [pscustomobject]@{ Index=$_.Index; Name=$_.Name; Path=$_.Path; Failure=$_.Failure; Before=$_.Before; Code=$code; Output=$output }
    } | Sort-Object Index)
    foreach($result in $results) {
        if($result.Code -eq 0 -or -not $result.Output.Contains('FAIL: '+$result.Failure)) { throw ('Fault was not killed by intended assertion: '+$result.Name+"`n"+$result.Output) }
        if((Get-FileHash -LiteralPath $result.Path -Algorithm SHA256).Hash -ne $result.Before) { throw 'Production source changed during check' }
    }
    if($results.Count -ne $Faults.Count) { throw "Only $($results.Count) of $($Faults.Count) faults reported back" }
    $final=Invoke-ArenaCheckProcess $Project $Properties
    if($final.Code -ne 0 -or -not $final.Output.Contains($Token)) { throw $final.Output }
    }
    finally {
        # Also after a failure: the outputs of a large mutant set are hundreds of MB, and the reason of the
        # failure is in the thrown message.
        foreach($done in $scratch,$mutantBin,$mutantObj) { Remove-Item -LiteralPath $done -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Write-Output ('Source faults rejected: '+$Faults.Count)
}
