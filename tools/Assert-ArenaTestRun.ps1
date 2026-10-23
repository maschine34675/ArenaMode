# Shared judgement of an xunit TRX for the harnesses under tools/. Every executed test must pass;
# a skipped test is tolerated only when its skip reason names a known environment gate (the APBS
# installation root that only ApbsCompatibilityChecks provides, the symbolic-link privilege the
# helper tests need). A skip for a missing SPT database is NOT tolerated: the harnesses set
# SPT_DATABASE themselves, and a run without the real data is not a pass. Dot-source this file,
# then call Assert-ArenaTestRun with the TRX the test run wrote.
$ArenaKnownSkipReasons = @(
    'Set ARENA_APBS_TEST_ROOT*',
    'creating a symbolic link needs a privilege*'
)

function Assert-ArenaTestRun([string]$TrxPath, [string[]]$AllowedSkipReasons = $ArenaKnownSkipReasons, [string[]]$RequireFamilies = @()) {
    [xml]$trx = Get-Content -Raw -LiteralPath $TrxPath
    $counters = $trx.TestRun.ResultSummary.Counters
    $results = @($trx.TestRun.Results.UnitTestResult)
    if ([int]$counters.total -le 0 -or $results.Count -eq 0) { throw "No tests were executed: $($counters.OuterXml)" }
    $failed = @($results | Where-Object { $_.outcome -eq 'Failed' })
    if ($failed.Count -ne 0) { throw ('Failed tests: ' + (($failed | ForEach-Object { $_.testName }) -join ', ')) }
    # xUnit's dynamic skips appear as NotExecuted results while VSTest's aggregate notExecuted
    # counter can stay zero, so the results are judged one by one, never the counters alone.
    $skipped = @($results | Where-Object { $_.outcome -ne 'Passed' })
    foreach ($skip in $skipped) {
        $reason = [string]$skip.Output.ErrorInfo.Message
        $allowed = $false
        foreach ($pattern in $AllowedSkipReasons) { if ($reason -like $pattern) { $allowed = $true; break } }
        if (-not $allowed) { throw "Unexpected skipped test $($skip.testName) ($($skip.outcome)): $reason" }
    }
    foreach ($family in $RequireFamilies) {
        if (@($results | Where-Object { $_.testName -like "*$family*" -and $_.outcome -eq 'Passed' }).Count -eq 0) {
            throw "Required test family was not executed: $family"
        }
    }
    return [pscustomobject]@{
        Total = [int]$counters.total
        Passed = [int]$counters.passed
        Skipped = $skipped.Count
        SkippedNames = @($skipped | ForEach-Object { $_.testName })
    }
}
