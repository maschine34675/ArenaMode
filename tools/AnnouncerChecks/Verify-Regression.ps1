$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AnnouncerChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\AnnouncerService.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$sourceHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaAnnouncerMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase([string]$name, [string]$candidate, [string]$expectedFailure) {
    $copyPath = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copyPath, $candidate)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release "-p:AnnouncerServiceSource=$copyPath" "-p:OutputPath=$outputPath" "-p:IntermediateOutputPath=$intermediatePath" 2>&1
    $resultCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($resultCode -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($expectedFailure)) {
        throw "$name did not fail at the expected recovery boundary (exit $resultCode). Evidence: $scratch"
    }
}

$loadingReset = '_loading = false;'
if ([regex]::Matches($source, [regex]::Escape($loadingReset)).Count -ne 1) {
    throw 'Expected exactly one loading-state reset; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'load-recovery' ($source.Replace($loadingReset, '_loading = true;')) `
    'A failed first load must retry only its failed clip; healthy cached clips must survive.'

$raceGuard = 'if (IsReady(previous))'
if ([regex]::Matches($source, [regex]::Escape($raceGuard)).Count -ne 1) {
    throw 'Expected exactly one late-readiness guard; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'late-readiness-race' ($source.Replace($raceGuard, 'if (false)')) `
    'Late-ready cached clip survives; its unnecessary downloaded replacement is destroyed.'

$probeSchedule = '_outputCheckAt = now + delay;'
if ([regex]::Matches($source, [regex]::Escape($probeSchedule)).Count -ne 1) {
    throw 'Expected exactly one buffer-aware probe schedule; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'premature-output-probe' ($source.Replace(
        $probeSchedule,
        '_outputCheckAt = Time.realtimeSinceStartup + 0.05f;')) `
    'The output probe waits beyond a 1024x4 DSP ring instead of judging playback at 50 ms.'

$probeExpiry = 'if (Time.realtimeSinceStartup >= expiresAt)'
if ([regex]::Matches($source, [regex]::Escape($probeExpiry)).Count -ne 1) {
    throw 'Expected exactly one output-probe expiry guard; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'expired-output-probe' ($source.Replace($probeExpiry, 'if (false)')) `
    'A late main-thread tick cannot replay or stop a cue that already completed.'

$uiObservation = 'ReportUnverified(key, route, "shared_one_shot_state", state);'
if ([regex]::Matches($source, [regex]::Escape($uiObservation)).Count -ne 1) {
    throw 'Expected exactly one shared UI observation; refusing an ambiguous source-copy mutation.'
}
$destructiveUiObservation = 'if (alive) output.Stop();' + [Environment]::NewLine `
    + '            ' + $uiObservation
Invoke-NegativeCase 'destructive-ui-probe' ($source.Replace($uiObservation, $destructiveUiObservation)) `
    'A UI source snapshot records evidence but can never stop or reject the requested one-shot.'

$finalHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
if ($sourceHash -ne $finalHash) { throw 'Production source changed during negative verification.' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'The unchanged production service failed its final positive check.' }
Write-Output "ARENA_ANNOUNCER_REGRESSION_VERIFIED (load, race and watchdog mutants rejected; production source unchanged; evidence: $scratch)"
