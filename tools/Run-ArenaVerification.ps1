<#
.SYNOPSIS
One entry point for the offline verification: the xunit suite once, without deploying, then any number
of harness scripts under tools/ one after another, with one verdict line each and a summary at the end.

.DESCRIPTION
The suite (ArenaMode.Tests, about 2 200 tests, about 40 s plus the build) runs exactly once with the
no-deploy targets and a sentinel that must not exist, so neither the server DLL nor the client payload
reaches the live install (a bare `dotnet test ArenaMode.Tests` would deploy the server DLL). Its TRX is
judged by tools/Assert-ArenaTestRun.ps1: every result must pass, only the known environment-gated skips
are tolerated. Harnesses run sequentially (parallel dotnet builds in one checkout collide) and a failing
one does not stop the rest; the summary names every verdict and the exit code is the number of failures.

Several older wrappers run the whole suite themselves (AirPitChecks, ApbsCompatibilityChecks, AssistChecks,
CustomsAreaRefreshChecks/Verify-Server, EquatorMapChecks, TdmMatchChecks/Run-Regressions,
TeamDeathmatchChecks/Verify-ExistingModes, WeaponLaserIntegrationChecks, BowlTeamBoxChecks); naming them
here runs the suite twice. Running every Verify.ps1 under tools/ takes hours: pick the harnesses whose
production code the change touched, and run the full set only when a pass ends.

.PARAMETER Harness
Harness folder names (tools/<name>/Verify.ps1) or script paths relative to the repository root.

.PARAMETER SkipUnitTests
Leave the suite out (for example when a named wrapper runs it anyway).

.PARAMETER SptRoot
The installed game, for the environment variables the data-bound tests read.

.EXAMPLE
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness PoolPrewarmChecks, ArenaSessionClientChecks
#>
param(
    [string[]]$Harness = @(),
    [switch]$SkipUnitTests,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
$results = Join-Path $repo 'artifacts\run-arena-verification'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$noDeploy = Join-Path $results 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $noDeploy) { throw 'The no-deploy sentinel must not exist.' }
$verdicts = New-Object System.Collections.Generic.List[object]

function Add-Verdict([string]$Name, [bool]$Passed, [double]$Seconds, [string]$Detail) {
    $verdicts.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Seconds = [math]::Round($Seconds); Detail = $Detail })
    Write-Output ("{0} {1,-48} {2,5}s  {3}" -f ($(if ($Passed) { 'PASS' } else { 'FAIL' })), $Name, [math]::Round($Seconds), $Detail)
}

$previousDatabase = $env:SPT_DATABASE
$previousRuntime = $env:SPT_RUNTIME
Push-Location $repo
try {
    $runtime = Join-Path $SptRoot 'SPT_Runtime'
    $env:SPT_DATABASE = Join-Path $runtime 'SPT_Data\database'
    $env:SPT_RUNTIME = $runtime

    if (-not $SkipUnitTests) {
        $t0 = Get-Date
        $trx = 'run-arena-verification.trx'
        & dotnet test 'ArenaMode.Tests\ArenaMode.Tests.csproj' -c Release --nologo -v:minimal `
            "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" `
            "-p:SPTUserMods=$noDeploy" "-p:SPTPath=$SptRoot" "-p:SPTUserPath=$runtime" `
            --logger "trx;LogFileName=$trx" --results-directory $results 2>&1 | Where-Object { $_ -match 'error|Fehler|Bestanden|Passed!|Failed!' }
        $code = $LASTEXITCODE
        $detail = "dotnet test exit $code"
        $passed = $code -eq 0
        if ($passed) {
            try {
                $run = Assert-ArenaTestRun -TrxPath (Join-Path $results $trx)
                $detail = "total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)" + $(if ($run.Skipped) { " ($($run.SkippedNames -join ', '))" } else { '' })
            }
            catch { $passed = $false; $detail = $_.Exception.Message }
        }
        if (Test-Path -LiteralPath $noDeploy) { $passed = $false; $detail += '; the test run created the no-deploy sentinel (it deployed)' }
        Add-Verdict 'ArenaMode.Tests (xunit, once)' $passed ((Get-Date) - $t0).TotalSeconds $detail
    }

    foreach ($item in $Harness) {
        $script = if ($item -like '*.ps1') { Join-Path $repo $item } else { Join-Path $repo "tools\$item\Verify.ps1" }
        if (-not (Test-Path -LiteralPath $script)) { Add-Verdict $item $false 0 "no such script: $script"; continue }
        $t0 = Get-Date
        $log = Join-Path $results (($item -replace '[^A-Za-z0-9]', '_') + '.log')
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { & pwsh -NoLogo -NoProfile -File $script 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log }
        finally { $ErrorActionPreference = $previousPreference }
        $code = $LASTEXITCODE
        $tail = (Get-Content -LiteralPath $log -Tail 3) -join ' | '
        Add-Verdict $item ($code -eq 0) ((Get-Date) - $t0).TotalSeconds ("exit $code; " + $tail.Substring(0, [Math]::Min(160, $tail.Length)) + "; log $log")
    }
}
finally {
    Pop-Location
    $env:SPT_DATABASE = $previousDatabase
    $env:SPT_RUNTIME = $previousRuntime
}

$failed = @($verdicts | Where-Object { -not $_.Passed })
Write-Output ("ARENA_VERIFICATION_SUMMARY passed={0} failed={1} seconds={2}" -f (@($verdicts | Where-Object Passed).Count), $failed.Count, ($verdicts | Measure-Object Seconds -Sum).Sum)
exit $failed.Count
