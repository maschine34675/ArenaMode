# Shared runner for linked production-source host checks. Fault copies live only in a unique temp directory;
# production files are never rewritten.
#
# Test-ArenaSourceFaults runs the baseline (dotnet run, which restores and builds), then every fault: the copy
# of the production file with Find replaced by Replace is compiled into its own intermediate and output
# directories under the project's obj/ and bin/ (dotnet build --no-restore, reusing the baseline's restore) and
# the program runs from there.
# Each fault must make the program exit non-zero AND print "<FailurePrefix><Failure>" (default prefix
# "FAIL: "); a fault that does not compile is never a kill, whatever its build output says. The faults build
# and run side by side, ARENA_MUTANT_PARALLEL at a time (default: 8, or what Run-ArenaVerification.ps1 sets
# for its workers; 1 runs them one after another): a mutant costs about 0.3 s of compile and program and
# about 2.5 s of process start, evaluation and restore check, so a sequential dotnet run per mutant spent
# most of its time waiting (KitPickerChecks: 229 s sequential with dotnet run, 37 s at 4, 24 s at 8).
# Running side by side cannot let a surviving mutant pass: every fault still needs its own non-zero exit and
# its own named assertion, and a collision between two mutant processes shows up as a fault that was not
# killed. The report keeps the order of $Faults.
#
# A fault is a hashtable:
#   Name, Path, Failure    required; Path is the production file the copy is made of.
#   Find, Replace          a string each, or arrays of equal length applied in order (each to the text the
#                          previous one left); every anchor must be present, and with -RequireSingleSite occur
#                          exactly once at the point it is applied. A fault that leaves the text unchanged is
#                          refused.
#   Property               the MSBuild property that points the build at the copy (-p:<Property>=<copy>).
#   Environment            one or more environment variable names set to the copy path for the build and the
#                          program of this fault only (harnesses that read pinned source text at run time).
#                          Per process, through ProcessStartInfo: $env: is process-wide, and the mutants run
#                          in runspaces of one process.
#   Prepare                a scriptblock param($Copy, $Directory, $Fault), run in order before any mutant
#                          builds; it may write more files into $Directory (the fault's own scratch folder)
#                          and returns the extra -p: properties for this fault (and nothing else).
#                          It runs inside this helper, and PowerShell scopes are dynamic: a variable it reads
#                          resolves to the helper's own first ($fault, $copy, $directory, $source, $text,
#                          $build, $i), so script variables it needs must have other names.
#   CopyName               file name of the copy in $Directory (default <Name>.cs).
#   At least one of Property, Environment and Prepare must carry the copy into the build.
# Options: -FailurePrefix ('' compares the raw Failure text, for programs that throw it unprefixed),
# -FailureMarker (one more string the output must contain), -RequireSingleSite, -CollapseWhitespace (output
# and expected text compared with every whitespace run as one space), -Arguments (program arguments:
# dotnet run -- <args> for baseline and final run, dotnet <dll> <args> for a mutant).
# Variables a harness sets with $env: before the call reach every process, the mutants included. The output is
# the raw text of both pipes (CRLF line ends, stdout before stderr): a Failure that spans lines needs
# -CollapseWhitespace.
function Invoke-ArenaCheckProcess([string]$Project, [string[]]$Properties, [string[]]$Arguments) {
    $tail = @()
    if ($Arguments) { $tail = @('--') + @($Arguments) }
    $output = (& dotnet run --project $Project -c Release @Properties @tail 2>&1 | Out-String)
    return @{ Code=$LASTEXITCODE; Output=$output }
}
function Invoke-ArenaChildProcess([string]$File, [string[]]$Arguments, [hashtable]$Environment, [string]$Directory) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($Environment) { foreach ($name in $Environment.Keys) { $info.Environment[$name] = [string]$Environment[$name] } }
    $process = [Diagnostics.Process]::Start($info)
    try {
        # Both pipes are drained at once: a child that fills one while the other is read would block.
        $out = $process.StandardOutput.ReadToEndAsync()
        $err = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return @{ Code=$process.ExitCode; Output=($out.GetAwaiter().GetResult() + $err.GetAwaiter().GetResult()) }
    }
    finally { $process.Dispose() }
}
function Test-ArenaSourceFaults([string]$Project, [string]$Token, [string[]]$Properties, [object[]]$Faults,
        [string]$FailurePrefix = 'FAIL: ', [string]$FailureMarker, [switch]$RequireSingleSite,
        [switch]$CollapseWhitespace, [string[]]$Arguments) {
    $programArguments = @()
    if ($Arguments) { $programArguments = @($Arguments) }
    $baseline=Invoke-ArenaCheckProcess $Project $Properties $programArguments
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
    try {
    $targetFile=((& dotnet msbuild $Project -getProperty:TargetFileName -p:Configuration=Release @Properties 2>&1) | Out-String).Trim()
    if(-not $targetFile -or $targetFile -notmatch '\.dll$') { throw "Cannot evaluate TargetFileName of ${Project}: $targetFile" }
    $dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $jobs=@()
    for($i=0; $i -lt $Faults.Count; $i++) {
        $fault=$Faults[$i]
        foreach($field in 'Name','Path','Failure') { if(-not $fault.$field) { throw "Fault $i has no ${field}: $($fault.Name)" } }
        if(-not ($fault.Property -or $fault.Environment -or $fault.Prepare)) { throw ('Fault reaches no build (no Property, Environment or Prepare): '+$fault.Name) }
        # Anchors are compared with LF line endings on both sides: a checkout with core.autocrlf
        # has CRLF sources while multi-line Find/Replace literals are written with `n, which made
        # every multi-line anchor report 'missing' (PoolPrewarmChecks, 2026-10-23). The copy the
        # mutant compiles from is LF too; the production file itself is never rewritten.
        $source=[IO.File]::ReadAllText($fault.Path).Replace("`r`n","`n")
        $finds=@($fault.Find)
        $replaces=@($fault.Replace)
        if($finds.Count -ne $replaces.Count) { throw ('Fault has '+$finds.Count+' anchors but '+$replaces.Count+' replacements: '+$fault.Name) }
        $text=$source
        for($k=0; $k -lt $finds.Count; $k++) {
            $find=([string]$finds[$k]).Replace("`r`n","`n")
            $replace=([string]$replaces[$k]).Replace("`r`n","`n")
            if(-not $find) { throw ('Fault anchor is empty: '+$fault.Name) }
            if($RequireSingleSite) {
                $sites=[regex]::Matches($text,[regex]::Escape($find)).Count
                if($sites -ne 1) { throw ("Fault anchor matches $sites sites (expected exactly one): "+$fault.Name) }
            }
            elseif(-not $text.Contains($find)) { throw ('Fault anchor missing: '+$fault.Name) }
            $text=$text.Replace($find,$replace)
        }
        if($text -ceq $source) { throw ('Fault changes nothing: '+$fault.Name) }
        $directory=Join-Path $scratch ('{0:D3}-{1}' -f $i,($fault.Name -replace '[^A-Za-z0-9_.-]','_'))
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $copy=Join-Path $directory $(if($fault.CopyName) { $fault.CopyName } else { $fault.Name+'.cs' })
        [IO.File]::WriteAllText($copy,$text)
        $build=@($Properties)
        if($fault.Property) { $build+=('-p:'+$fault.Property+'='+$copy) }
        if($fault.Prepare) {
            foreach($extra in @(& $fault.Prepare $copy $directory $fault)) {
                if($extra -isnot [string] -or -not $extra.StartsWith('-p:')) { throw ("Prepare of fault $($fault.Name) returned '$extra'; it may return only -p: properties") }
                $build+=$extra
            }
        }
        $build+=@(('-p:IntermediateOutputPath={0}\{1:D3}\' -f $mutantObj,$i), ('-p:OutDir={0}\{1:D3}\' -f $mutantBin,$i))
        $environment=@{}
        foreach($name in @($fault.Environment)) { if($name) { $environment[[string]$name]=$copy } }
        $jobs+=[pscustomobject]@{ Index=$i; Name=$fault.Name; Path=$fault.Path; Failure=[string]$fault.Failure
            Before=(Get-FileHash -LiteralPath $fault.Path -Algorithm SHA256).Hash
            Build=$build; Environment=$environment
            Program=('{0}\{1:D3}\{2}' -f $mutantBin,$i,$targetFile) }
    }
    $throttle=if($env:ARENA_MUTANT_PARALLEL -as [int]) { [Math]::Max(1,[int]$env:ARENA_MUTANT_PARALLEL) } else { 8 }
    $here=(Get-Location).Path   # the program runs where dotnet run would have run it
    $childProcess=${function:Invoke-ArenaChildProcess}.ToString()
    $results=@($jobs | ForEach-Object -ThrottleLimit $throttle -Parallel {
        ${function:Invoke-ArenaChildProcess}=[scriptblock]::Create($using:childProcess)
        $built=Invoke-ArenaChildProcess $using:dotnet (@('build',$using:Project,'-c','Release','--no-restore','--nologo','-v:q') + @($_.Build)) $_.Environment $using:here
        if($built.Code -eq 0) { $run=Invoke-ArenaChildProcess $using:dotnet (@($_.Program) + @($using:programArguments)) $_.Environment $using:here; $compiled=$true }
        else { $run=@{ Code=$built.Code; Output=("build failed (exit $($built.Code)):`n"+$built.Output) }; $compiled=$false }
        [pscustomobject]@{ Index=$_.Index; Name=$_.Name; Path=$_.Path; Failure=$_.Failure; Before=$_.Before; Compiled=$compiled; Code=$run.Code; Output=[string]$run.Output }
    } | Sort-Object Index)
    if($results.Count -ne $Faults.Count) { throw "Only $($results.Count) of $($Faults.Count) faults reported back" }
    foreach($result in $results) {
        $expected=$FailurePrefix+$result.Failure
        $output=$result.Output
        if($CollapseWhitespace) { $expected=$expected -replace '\s+',' '; $output=$output -replace '\s+',' ' }
        $reason=if(-not $result.Compiled) { 'it did not compile' }
            elseif($result.Code -eq 0) { 'the program exited 0' }
            elseif($FailureMarker -and -not $result.Output.Contains($FailureMarker)) { "the output lacks $FailureMarker" }
            elseif(-not $output.Contains($expected)) { "the output lacks '$expected'" }
        if($reason) { throw ('Fault was not killed by intended assertion: '+$result.Name+' ('+$reason+")`n"+$result.Output) }
        if((Get-FileHash -LiteralPath $result.Path -Algorithm SHA256).Hash -ne $result.Before) { throw 'Production source changed during check' }
    }
    $final=Invoke-ArenaCheckProcess $Project $Properties $programArguments
    if($final.Code -ne 0 -or -not $final.Output.Contains($Token)) { throw $final.Output }
    }
    finally {
        # Also after a failure: the outputs of a large mutant set are hundreds of MB, and the reason of the
        # failure is in the thrown message.
        foreach($done in $scratch,$mutantBin,$mutantObj) { Remove-Item -LiteralPath $done -Recurse -Force -ErrorAction SilentlyContinue }
    }
    foreach($result in $results) { Write-Output ('Fault rejected: '+$result.Name) }
    Write-Output ('Source faults rejected: '+$Faults.Count)
}
