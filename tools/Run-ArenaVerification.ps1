#Requires -Version 7.0
<#
.SYNOPSIS
One entry point for the offline verification: the xunit suite once, without deploying, then any number
of harness scripts under tools/ one after another, with one verdict line each and a summary at the end.

.DESCRIPTION
The suite (ArenaMode.Tests, about 3 900 tests, about 100 s plus the build) runs exactly once with the
no-deploy targets and a sentinel that must not exist, so neither the server DLL nor the client payload
reaches the live install (a bare `dotnet test ArenaMode.Tests` would deploy the server DLL). Its TRX is
judged by tools/Assert-ArenaTestRun.ps1: every result must pass, only the known environment-gated skips
are tolerated. The live install (client, companion, server DLL, gate patcher, session helper) is hashed
before the run and after every step; a step that changed any of them fails, whatever it printed.
Harnesses run sequentially (parallel dotnet builds in one checkout collide) and a failing one does not
stop the rest; the summary names every verdict and the exit code is the number of failures (1 when
nothing ran at all).

Result cache: a step whose inputs are unchanged since its last PASS is not run again; its verdict line
reads "PASS <step> 0s cached: inputs unchanged since <time> (ran <n>s) ..." and its log is the log of that
PASS. The inputs are every file of the checkout the step can reach (shared files, its harness folder and
the folders it names, the docs it names), the installed game/BepInEx/SPT state, the sibling checkouts, the
tool versions, the relevant environment variables and, for steps that call git, HEAD; see
tools/ArenaVerificationInputs.ps1 for the exact rules. Only a PASS is kept, and only when the step's inputs
are still the same after it ran. Entries live under %LOCALAPPDATA%\ArenaMode\verification-cache (or
$env:ARENA_VERIFICATION_CACHE), shared by every checkout on the machine. -NoCache runs every step and
refreshes the entries; a release gate runs with -NoCache.

Several older wrappers run the whole suite themselves (`git grep -n 'dotnet test' tools`; the ones
without --filter); naming them here runs the suite twice. Running every Verify.ps1 under tools/ takes
hours: pick the harnesses whose production code the change touched, and run the full set only when a
pass ends.

.PARAMETER Harness
Harness folder names (tools/<name>/Verify.ps1; a folder without one but with tools/<name>/<name>.csproj is
run with `dotnet run`, no program arguments and the no-deploy properties), optionally with a stage for the scripts that need one
(<name>:<Stage>, forwarded as -Stage), or script paths relative to the repository root. Several names
go comma-separated without spaces (-Harness A,B:Stage) or as repeated arguments.

.PARAMETER SkipUnitTests
Leave the suite out (for example when a named wrapper runs it anyway).

.PARAMETER SptRoot
The installed game, for the environment variables the data-bound tests read and for the live-install
hashes. When it is named here, it is also forwarded as -SptRoot to every harness script whose param
block declares one, so the harnesses read the same installation the runner hashes; left out, each
script keeps its own default.

.PARAMETER NoCache
Run every step even when an earlier PASS with the same inputs exists (the PASS entries are refreshed).

.EXAMPLE
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness PoolPrewarmChecks,ArenaAreaChecks:Server
#>
param(
    [string[]]$Harness = @(),
    [switch]$SkipUnitTests,
    [string]$SptRoot = 'D:\SPT41',
    [switch]$NoCache
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
. (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot 'ArenaVerificationInputs.ps1')
$results = Join-Path $repo 'artifacts\run-arena-verification'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$noDeploy = Join-Path $results 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $noDeploy) { throw 'The no-deploy sentinel must not exist.' }
# "A,B:Stage" and repeated arguments both work; a stage after ':' is forwarded to the script.
$Harness = @($Harness | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$verdicts = New-Object System.Collections.Generic.List[object]
# Only a value named on this command line is forwarded: the scripts' own defaults are not all
# D:\SPT41 (ApbsCompatibilityChecks derives its root from the checkout), and they stay as they are.
$forwardSptRoot = $PSBoundParameters.ContainsKey('SptRoot')
# The installations whose state is part of every key: the one named here and the default every harness
# script without -SptRoot keeps (ARENA_VERIFICATION_INSTALL_ROOTS replaces both, for the runner's own checks).
$installRoots = if ($env:ARENA_VERIFICATION_INSTALL_ROOTS) { @($env:ARENA_VERIFICATION_INSTALL_ROOTS -split ';') } else { @($SptRoot, 'D:\SPT41') }
function Test-ScriptDeclaresSptRoot([string]$Path) {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$null, [ref]$null)
    return $null -ne $ast.ParamBlock -and
        @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'SptRoot' }).Count -gt 0
}

function Add-Verdict([string]$Name, [bool]$Passed, [double]$Seconds, [string]$Detail, [bool]$Cached = $false) {
    $verdicts.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Seconds = [math]::Round($Seconds); Detail = $Detail; Cached = $Cached })
    Write-Output ("{0} {1,-48} {2,5}s  {3}" -f ($(if ($Passed) { 'PASS' } else { 'FAIL' })), $Name, [math]::Round($Seconds), $Detail)
}

# The installed snapshot covers the client and companion DLLs; the server DLL, the gate patcher and the
# session helper are hashed on top, so a deploy from any step is seen as a changed file, not inferred.
$liveFiles = @(
    (Join-Path $SptRoot 'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll'),
    (Join-Path $SptRoot 'BepInEx\patchers\maschine-ArenaMode\maschine-ArenaMode.Gate.dll'),
    (Join-Path $SptRoot 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Session.exe'),
    (Join-Path $SptRoot 'BepInEx\plugins\maschine-ArenaMode\arena-whitelist.json'))
function Get-LiveHashes {
    $hashes = @{}
    foreach ($path in $liveFiles) {
        $hashes[$path] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { '<absent>' }
    }
    return $hashes
}
function Test-LiveUnchanged([hashtable]$Snapshot, [hashtable]$Hashes) {
    try { Assert-ArenaInstalledSnapshotUnchanged $Snapshot $SptRoot } catch { return $_.Exception.Message }
    $now = Get-LiveHashes
    foreach ($path in $liveFiles) { if ($now[$path] -ne $Hashes[$path]) { return "live install changed: $path" } }
    return $null
}

# The cache. Any error in the input model switches the cache off for this run, never the verification.
$cacheModel = $null
$cacheNote = $null
$runnerArguments = "sptRoot=$SptRoot|forward=$forwardSptRoot"
function Get-StepKeyInfo([string]$Item, [string]$Name) {
    if (-not $cacheModel) { return $null }
    $homeFolder = $null; $extra = @()
    if ($Item -eq 'xunit') { }
    elseif ($Name -like '*.ps1') {
        $rel = ($Name -replace '\\', '/') -replace '^\./', ''
        if ($rel -match '^tools/([^/]+)/') { $homeFolder = $Matches[1] } else { $extra = @($rel) }
    }
    else { $homeFolder = $Name }
    $scope = Get-ArenaStepScope $cacheModel $homeFolder $extra
    $info = Get-ArenaStepKey $cacheModel $scope $Item $runnerArguments $script:environmentHash $script:externalFingerprint $installRoots
    $info | Add-Member -NotePropertyName Scope -NotePropertyValue $scope
    return $info
}
# After a step ran: the same files, environment and installation, or the PASS is not kept.
function Test-ArenaStepStillValid($KeyInfo, $Scope, [string]$Item) {
    $changed = Test-ArenaSnapshotStillValid $cacheModel.Snapshot (Get-ArenaScopeFiles $cacheModel $Scope)
    if ($changed) { return $changed }
    Reset-ArenaStatManifestMemo
    $script:externalFingerprint = Get-ArenaExternalFingerprint $repo $installRoots
    $script:environmentHash = Get-ArenaEnvironmentFingerprint
    $again = Get-ArenaStepKey $cacheModel $Scope $Item $runnerArguments $script:environmentHash $script:externalFingerprint $installRoots
    if ($again.Key -ne $KeyInfo.Key) {
        $moved = @(foreach ($p in $again.Parts.GetEnumerator()) { if ([string]$KeyInfo.Parts[$p.Key] -ne [string]$p.Value) { $p.Key } })
        $what = Compare-ArenaExternalDigest $KeyInfo.ExternalDigest $again.ExternalDigest
        return 'inputs moved during the run: ' + ($moved -join ', ') + $(if ($what) { " ($what)" } else { '' })
    }
    return $null
}
function Write-CachedLog([string]$Log, $Entry) {
    $header = "cached: inputs unchanged since $(([datetime]$Entry.Finished).ToString('yyyy-MM-dd HH:mm:ss')) (key $($Entry.Key.Substring(0, 12)), checkout $($Entry.Checkout), HEAD $($Entry.Head)); original log follows"
    $original = if ($Entry.Log -and (Test-Path -LiteralPath $Entry.Log)) { Get-Content -LiteralPath $Entry.Log } else { @('<original log no longer in the cache>') }
    Set-Content -LiteralPath $Log -Value (@($header) + @($original))
}
# One step under the cache: on a hit, write the step's log from the cached PASS and return the verdict
# detail; $null means the caller runs the step.
function Get-CachedVerdict($KeyInfo, [string]$Log) {
    if ($keyInfo -and $keyInfo.Cacheable -and -not $NoCache) {
        $entry = Get-ArenaCachedPass $KeyInfo.Key
        if (-not $entry) { return $null }
        Write-CachedLog $Log $entry
        $detail = "cached: inputs unchanged since $(([datetime]$entry.Finished).ToString('yyyy-MM-dd HH:mm')) (ran $($entry.Seconds)s); $($entry.Detail)"
        return $detail.Substring(0, [Math]::Min(300, $detail.Length)) + "; log $Log"
    }
    return $null
}
function Complete-Step([string]$Item, $KeyInfo, [bool]$Passed, [double]$Seconds, [string]$Detail, [string]$Log) {
    if ($passed -and $keyInfo -and $keyInfo.Cacheable) {
        $afterRun = Test-ArenaStepStillValid $keyInfo $keyInfo.Scope $item
        if ($afterRun) { return "; not cached: $afterRun" }
        Save-ArenaCachedPass $KeyInfo $Item $Seconds $Log $repo $cacheModel.Snapshot.Head $Detail
        return ''
    }
    if ($KeyInfo -and -not $KeyInfo.Cacheable) { return "; not cacheable: $($KeyInfo.Why)" }
    return ''
}

$previousDatabase = $env:SPT_DATABASE
$previousRuntime = $env:SPT_RUNTIME
Push-Location $repo
try {
    $runtime = Join-Path $SptRoot 'SPT_Runtime'
    $env:SPT_DATABASE = Join-Path $runtime 'SPT_Data\database'
    $env:SPT_RUNTIME = $runtime
    $liveSnapshot = Read-ArenaInstalledSnapshot $SptRoot
    $liveHashes = Get-LiveHashes
    try {
        $cacheModel = Get-ArenaInputModel $repo
        $script:externalFingerprint = Get-ArenaExternalFingerprint $repo $installRoots
        $script:environmentHash = Get-ArenaEnvironmentFingerprint
    }
    catch { $cacheModel = $null; $cacheNote = "result cache off: $($_.Exception.Message)"; Write-Output $cacheNote }

    if (-not $SkipUnitTests) {
        $t0 = Get-Date
        $trx = 'run-arena-verification.trx'
        $trxPath = Join-Path $results $trx
        $log = Join-Path $results 'xunit.log'
        $keyInfo = $null
        try { $keyInfo = Get-StepKeyInfo 'xunit' 'xunit' } catch { $keyInfo = $null }
        $cachedDetail = Get-CachedVerdict $keyInfo $log
        if ($null -ne $cachedDetail) { Add-Verdict 'ArenaMode.Tests (xunit, once)' $true 0 $cachedDetail $true }
        else {
            $scope = if ($keyInfo) { $keyInfo.Scope } else { $null }
            if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }   # never judge a stale run
            & dotnet test 'ArenaMode.Tests\ArenaMode.Tests.csproj' -c Release --nologo -v:minimal `
                "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" `
                "-p:SPTUserMods=$noDeploy" "-p:SPTPath=$SptRoot" "-p:SPTUserPath=$runtime" `
                --logger "trx;LogFileName=$trx" --results-directory $results 2>&1 | ForEach-Object { $_.ToString() } |
                Tee-Object -FilePath $log | Where-Object { $_ -match 'error|Fehler|Bestanden|Passed!|Failed!' }
            $code = $LASTEXITCODE
            $detail = "dotnet test exit $code"
            $passed = $code -eq 0 -and (Test-Path -LiteralPath $trxPath)
            if ($passed) {
                try {
                    $run = Assert-ArenaTestRun -TrxPath $trxPath
                    $detail = "total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)" + $(if ($run.Skipped) { " ($($run.SkippedNames -join ', '))" } else { '' })
                }
                catch { $passed = $false; $detail = $_.Exception.Message }
            }
            elseif ($code -eq 0) { $detail = 'dotnet test wrote no TRX' }
            $changed = Test-LiveUnchanged $liveSnapshot $liveHashes
            if ($changed) { $passed = $false; $detail += "; $changed" }
            $seconds = ((Get-Date) - $t0).TotalSeconds
            $detail += Complete-Step 'xunit' $keyInfo $passed $seconds $detail $log
            Add-Verdict 'ArenaMode.Tests (xunit, once)' $passed $seconds $detail
        }
    }

    foreach ($item in $Harness) {
        $name, $stage = $item -split ':', 2
        $script = if ($name -like '*.ps1') { Join-Path $repo $name } else { Join-Path $repo "tools\$name\Verify.ps1" }
        $project = Join-Path $repo "tools\$name\$name.csproj"
        $t0 = Get-Date
        $log = Join-Path $results (($item -replace '[^A-Za-z0-9]', '_') + '.log')
        $keyInfo = $null
        $keyNote = ''
        try { $keyInfo = Get-StepKeyInfo $item $name } catch { $keyNote = "; not cached: key failed: $($_.Exception.Message)" }
        $cachedDetail = Get-CachedVerdict $keyInfo $log
        if ($null -ne $cachedDetail) { Add-Verdict $item $true 0 $cachedDetail $true; continue }
        if ($keyInfo -and -not $NoCache) { $keyNote += "; ran: $(Get-ArenaMissReason $keyInfo $item)" }
        $scope = if ($keyInfo) { $keyInfo.Scope } else { $null }
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            if (Test-Path -LiteralPath $script) {
                $arguments = @('-NonInteractive', '-NoLogo', '-NoProfile', '-File', $script)
                if ($stage) { $arguments += @('-Stage', $stage) }
                if ($forwardSptRoot -and (Test-ScriptDeclaresSptRoot $script)) { $arguments += @('-SptRoot', $SptRoot) }
                & pwsh @arguments 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log
            }
            elseif ($name -notlike '*.ps1' -and (Test-Path -LiteralPath $project)) {
                # A harness without a Verify.ps1: its program is the whole check (no mutants).
                # There is no shared positional-argument contract: MolotovPresentationChecks
                # treats a path as an audio export destination, NativeStopChecks as a repo root.
                # Use their no-argument defaults; SPT paths are supplied via properties/env above.
                & dotnet run --project $project -c Release "-p:SPTPath=$SptRoot" `
                    "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" "-p:SPTUserMods=$noDeploy" `
                    2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log
            }
            else { Add-Verdict $item $false 0 "no such script or project: $script"; continue }
        }
        finally { $ErrorActionPreference = $previousPreference }
        $code = $LASTEXITCODE
        $tail = (Get-Content -LiteralPath $log -Tail 3) -join ' | '
        $detail = "exit $code; " + $tail.Substring(0, [Math]::Min(160, $tail.Length))
        $passed = $code -eq 0
        $changed = Test-LiveUnchanged $liveSnapshot $liveHashes
        if ($changed) { $passed = $false; $detail += "; $changed" }
        $seconds = ((Get-Date) - $t0).TotalSeconds
        $detail += $keyNote + (Complete-Step $item $keyInfo $passed $seconds $detail $log)
        Add-Verdict $item $passed $seconds "$detail; log $log"
    }
}
finally {
    Pop-Location
    $env:SPT_DATABASE = $previousDatabase
    $env:SPT_RUNTIME = $previousRuntime
}

if ($verdicts.Count -eq 0) { Write-Output 'ARENA_VERIFICATION_SUMMARY nothing ran (no harness named and the suite skipped)'; exit 1 }
$failed = @($verdicts | Where-Object { -not $_.Passed })
Write-Output ("ARENA_VERIFICATION_SUMMARY passed={0} failed={1} seconds={2} cached={3}" -f (@($verdicts | Where-Object Passed).Count), $failed.Count, ($verdicts | Measure-Object Seconds -Sum).Sum, @($verdicts | Where-Object Cached).Count)
exit $failed.Count
