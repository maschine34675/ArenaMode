#Requires -Version 7.0
<#
.SYNOPSIS
One entry point for the offline verification: the xunit suite once, without deploying, then any number
of harness scripts under tools/ one after another, with one verdict line each and a summary at the end.

.DESCRIPTION
The suite (ArenaMode.Tests, about 2 300 tests, about 40 s plus the build) runs exactly once with the
no-deploy targets and a sentinel that must not exist, so neither the server DLL nor the client payload
reaches the live install (a bare `dotnet test ArenaMode.Tests` would deploy the server DLL). Its TRX is
judged by tools/Assert-ArenaTestRun.ps1: every result must pass, only the known environment-gated skips
are tolerated. The live install (client, companion, server DLL, gate patcher, session helper) is hashed
before the run and after every step; a step that changed any of them fails, whatever it printed.
Harnesses run sequentially (parallel dotnet builds in one checkout collide) and a failing one does not
stop the rest; the summary names every verdict and the exit code is the number of failures (1 when
nothing ran at all).

Several older wrappers run the whole suite themselves (`git grep -n 'dotnet test' tools` lists them;
the ones without --filter); naming them here runs the suite twice. Running every Verify.ps1 under
tools/ takes hours: pick the harnesses whose production code the change touched, and run the full set
only when a pass ends.

.PARAMETER Harness
Harness folder names (tools/<name>/Verify.ps1; a folder without one but with tools/<name>/<name>.csproj is
run with `dotnet run` and the no-deploy properties), optionally with a stage for the scripts that need one
(<name>:<Stage>, forwarded as -Stage), or script paths relative to the repository root. Several names
go comma-separated without spaces (-Harness A,B:Stage) or as repeated arguments.

.PARAMETER SkipUnitTests
Leave the suite out (for example when a named wrapper runs it anyway).

.PARAMETER SptRoot
The installed game, for the environment variables the data-bound tests read and for the live-install
hashes.

.EXAMPLE
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness PoolPrewarmChecks,ArenaAreaChecks:Server
#>
param(
    [string[]]$Harness = @(),
    [switch]$SkipUnitTests,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
. (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
$results = Join-Path $repo 'artifacts\run-arena-verification'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$noDeploy = Join-Path $results 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $noDeploy) { throw 'The no-deploy sentinel must not exist.' }
# "A,B:Stage" and repeated arguments both work; a stage after ':' is forwarded to the script.
$Harness = @($Harness | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$verdicts = New-Object System.Collections.Generic.List[object]

function Add-Verdict([string]$Name, [bool]$Passed, [double]$Seconds, [string]$Detail) {
    $verdicts.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Seconds = [math]::Round($Seconds); Detail = $Detail })
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

$previousDatabase = $env:SPT_DATABASE
$previousRuntime = $env:SPT_RUNTIME
Push-Location $repo
try {
    $runtime = Join-Path $SptRoot 'SPT_Runtime'
    $env:SPT_DATABASE = Join-Path $runtime 'SPT_Data\database'
    $env:SPT_RUNTIME = $runtime
    $liveSnapshot = Read-ArenaInstalledSnapshot $SptRoot
    $liveHashes = Get-LiveHashes

    if (-not $SkipUnitTests) {
        $t0 = Get-Date
        $trx = 'run-arena-verification.trx'
        $trxPath = Join-Path $results $trx
        if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }   # never judge a stale run
        & dotnet test 'ArenaMode.Tests\ArenaMode.Tests.csproj' -c Release --nologo -v:minimal `
            "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" `
            "-p:SPTUserMods=$noDeploy" "-p:SPTPath=$SptRoot" "-p:SPTUserPath=$runtime" `
            --logger "trx;LogFileName=$trx" --results-directory $results 2>&1 | Where-Object { $_ -match 'error|Fehler|Bestanden|Passed!|Failed!' }
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
        Add-Verdict 'ArenaMode.Tests (xunit, once)' $passed ((Get-Date) - $t0).TotalSeconds $detail
    }

    foreach ($item in $Harness) {
        $name, $stage = $item -split ':', 2
        $script = if ($name -like '*.ps1') { Join-Path $repo $name } else { Join-Path $repo "tools\$name\Verify.ps1" }
        $project = Join-Path $repo "tools\$name\$name.csproj"
        $t0 = Get-Date
        $log = Join-Path $results (($item -replace '[^A-Za-z0-9]', '_') + '.log')
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            if (Test-Path -LiteralPath $script) {
                $arguments = @('-NonInteractive', '-NoLogo', '-NoProfile', '-File', $script)
                if ($stage) { $arguments += @('-Stage', $stage) }
                & pwsh @arguments 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log
            }
            elseif ($name -notlike '*.ps1' -and (Test-Path -LiteralPath $project)) {
                # A harness without a Verify.ps1: its program is the whole check (no mutants).
                & dotnet run --project $project -c Release "-p:SPTPath=$SptRoot" `
                    "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" "-p:SPTUserMods=$noDeploy" `
                    -- $SptRoot 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log
            }
            else { Add-Verdict $item $false 0 "no such script or project: $script"; continue }
        }
        finally { $ErrorActionPreference = $previousPreference }
        $code = $LASTEXITCODE
        $tail = (Get-Content -LiteralPath $log -Tail 3) -join ' | '
        $detail = "exit $code; " + $tail.Substring(0, [Math]::Min(160, $tail.Length)) + "; log $log"
        $passed = $code -eq 0
        $changed = Test-LiveUnchanged $liveSnapshot $liveHashes
        if ($changed) { $passed = $false; $detail += "; $changed" }
        Add-Verdict $item $passed ((Get-Date) - $t0).TotalSeconds $detail
    }
}
finally {
    Pop-Location
    $env:SPT_DATABASE = $previousDatabase
    $env:SPT_RUNTIME = $previousRuntime
}

if ($verdicts.Count -eq 0) { Write-Output 'ARENA_VERIFICATION_SUMMARY nothing ran (no harness named and the suite skipped)'; exit 1 }
$failed = @($verdicts | Where-Object { -not $_.Passed })
Write-Output ("ARENA_VERIFICATION_SUMMARY passed={0} failed={1} seconds={2}" -f (@($verdicts | Where-Object Passed).Count), $failed.Count, ($verdicts | Measure-Object Seconds -Sum).Sum)
exit $failed.Count
