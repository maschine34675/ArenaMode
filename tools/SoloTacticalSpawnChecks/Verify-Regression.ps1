$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'SoloTacticalSpawnChecks.csproj'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaSoloTacticalMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
$productionPaths = @(
    (Join-Path $repoRoot 'ArenaMode.Client\Modes\SoloTacticalSpawnPolicy.cs'),
    (Join-Path $repoRoot 'ArenaMode.Client\Modes\SoloTacticalSquadDispatch.cs'),
    (Join-Path $repoRoot 'ArenaMode.Client\Modes\TacticalMode.cs'),
    (Join-Path $repoRoot 'ArenaMode.Client\Bots\BurstSpawnService.cs')
)
$productionHashes = @{}
foreach ($path in $productionPaths) {
    $productionHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Invoke-ExpectedFailure {
    param(
        [string]$Label,
        [string[]]$ProjectArguments,
        [string[]]$ApplicationArguments,
        [string]$Expected
    )

    # Windows PowerShell 5.1 promotes an expected native stderr line to a
    # terminating NativeCommandError while the script-wide preference is Stop.
    # Keep the mutation result as ordinary captured evidence in both 5.1 and 7+.
    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet run --project $project -c Release @ProjectArguments -- @ApplicationArguments 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    $output | ForEach-Object { Write-Output ([string]$_) }
    if ($exit -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($Expected)) {
        throw "$Label mutant did not fail at '$Expected' (exit $exit). Evidence: $scratch"
    }
}

$policyPath = Join-Path $repoRoot 'ArenaMode.Client\Modes\SoloTacticalSpawnPolicy.cs'
$policy = [System.IO.File]::ReadAllText($policyPath)
if ([regex]::Matches($policy, 'AssemblySeconds\s*=\s*45f').Count -ne 1) {
    throw 'Expected exactly one 45-second production assembly constant.'
}
$shortPolicy = Join-Path $scratch 'SoloTacticalSpawnPolicy-12s.cs'
[System.IO.File]::WriteAllText($shortPolicy, [regex]::Replace(
    $policy, 'AssemblySeconds\s*=\s*45f', 'AssemblySeconds = 12f'))
Invoke-ExpectedFailure `
    -Label 'timeout' `
    -ProjectArguments @(
        "-p:ProductionPolicySource=$shortPolicy",
        "-p:OutputPath=$(Join-Path $scratch 'timeout-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'timeout-obj\')"
    ) `
    -ApplicationArguments @() `
    -Expected 'the measured 12.728-second first request is not discarded by the old 12-second boundary'

$tacticalPath = Join-Path $repoRoot 'ArenaMode.Client\Modes\TacticalMode.cs'
$tactical = [System.IO.File]::ReadAllText($tacticalPath)
if ([regex]::Matches($tactical, '_squadRequested = expected;').Count -ne 1) {
    throw 'Expected exactly one pre-published Solo Tactical observer limit.'
}
$latePublish = Join-Path $scratch 'TacticalMode-late-publish.cs'
[System.IO.File]::WriteAllText($latePublish, $tactical.Replace(
    '_squadRequested = expected;', '_squadRequested = planned;'))
Invoke-ExpectedFailure `
    -Label 'callback-order' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'order-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'order-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $latePublish) `
    -Expected 'the exact observer limit is published before a warm synchronous SpawnSquad callback'

$requestAt = $tactical.IndexOf('private void RequestSoloSquad(bool retry)', [StringComparison]::Ordinal)
$clearAt = if ($requestAt -ge 0) {
    $tactical.IndexOf('RetireSquad();', $requestAt, [StringComparison]::Ordinal)
} else {
    -1
}
if ($requestAt -lt 0 -or $clearAt -lt 0) {
    throw 'Expected the exact same-round retry boundary.'
}
$insertAt = $clearAt + 'RetireSquad();'.Length
$newline = if ($tactical.Contains("`r`n")) { "`r`n" } else { "`n" }
$replay = Join-Path $scratch 'TacticalMode-replay.cs'
[System.IO.File]::WriteAllText($replay, $tactical.Insert($insertAt, $newline + '            EnterBuy();'))
Invoke-ExpectedFailure `
    -Label 'round-replay' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'replay-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'replay-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $replay) `
    -Expected 'a failed opponent request retries inside the same round without another buy, charge, body swap or cue'

$holdGate = 'if (Time.time >= _phaseEndsAt && !HoldRoundStartForKitSwap())'
if ([regex]::Matches($tactical, [regex]::Escape($holdGate)).Count -ne 1) {
    throw 'Expected exactly one raid-start swap gate on the buy phase end.'
}
$unguarded = Join-Path $scratch 'TacticalMode-unguarded-buy-end.cs'
[System.IO.File]::WriteAllText($unguarded, $tactical.Replace($holdGate, 'if (Time.time >= _phaseEndsAt)'))
Invoke-ExpectedFailure `
    -Label 'unguarded-buy-end' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'unguarded-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'unguarded-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $unguarded) `
    -Expected 'the buy phase ends only when the raid-start kit swap is no longer pending, or the wait has given up'

$holdCase = 'case SoloTacticalRoundStartDecision\.Hold:(\s*)return true;'
if ([regex]::Matches($tactical, $holdCase).Count -ne 1) {
    throw 'Expected exactly one Hold case that keeps the buy phase open.'
}
$ignoredHold = Join-Path $scratch 'TacticalMode-ignored-hold.cs'
[System.IO.File]::WriteAllText($ignoredHold, [regex]::Replace(
    $tactical, $holdCase, 'case SoloTacticalRoundStartDecision.Hold:$1return false;'))
Invoke-ExpectedFailure `
    -Label 'ignored-hold' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'ignored-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'ignored-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $ignoredHold) `
    -Expected 'a wait in progress keeps the buy phase open'

# Dispatch order (docs/reviews/SOLO-TACTICAL-SPAWN-REVIEW.md): the round's squad request waits
# for the body move. The order before the fix, read from the commit the fix was made on, and the
# smallest reintroduction of it must both fail the distance assertion on the installed rows.
$postMove = 'every squad point is at least 25 m (BurstSpawnPointSelector.MinSpawnDistance) from the post-move position'
$preFix = Join-Path $scratch 'TacticalMode-pre-fix.cs'
$preFixText = (& git -C $repoRoot show 'a1e33b87:ArenaMode.Client/Modes/TacticalMode.cs') -join "`n"
if ($LASTEXITCODE -ne 0 -or -not $preFixText.Contains('RequestSoloSquad(retry: false);')) {
    throw 'The pre-fix Tactical source (a1e33b87) is unavailable.'
}
[System.IO.File]::WriteAllText($preFix, $preFixText)
Invoke-ExpectedFailure `
    -Label 'pre-fix-order' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'prefix-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'prefix-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $preFix) `
    -Expected $postMove

function New-TacticalMutant([string]$Name, [string]$Find, [string]$Replace) {
    $text = $tactical.Replace("`r`n", "`n")
    if ([regex]::Matches($text, [regex]::Escape($Find)).Count -ne 1) {
        throw "Mutant anchor missing or ambiguous ($Name): $Find"
    }
    $path = Join-Path $scratch "TacticalMode-$Name.cs"
    [System.IO.File]::WriteAllText($path, $text.Replace($Find, $Replace))
    return $path
}

$survivorComment = "        // A player who SURVIVED the last round is standing there in last round's"
$requestFirst = New-TacticalMutant 'request-first' $survivorComment ("        RequestSoloSquad(retry: false);`n`n" + $survivorComment)
Invoke-ExpectedFailure `
    -Label 'request-first' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'first-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'first-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $requestFirst) `
    -Expected $postMove

$noLanding = New-TacticalMutant 'no-landing-report' "        _squadDispatch.Report(RoundNumber, landed: true);`n" ''
Invoke-ExpectedFailure `
    -Label 'no-landing-report' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'landing-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'landing-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $noLanding) `
    -Expected "the held respawn's landing releases the round's request"

$silentSwap = New-TacticalMutant 'silent-swap' "RoundNumber == roundNumber,`n            landed);" "RoundNumber == roundNumber,`n            null);"
Invoke-ExpectedFailure `
    -Label 'silent-swap' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'silent-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'silent-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $silentSwap) `
    -Expected "the survivor's round-break swap reports its landing to the held request"

$noCap = New-TacticalMutant 'no-cap-tick' "            if (_squadDispatch.Waiting(RoundNumber))`n                return;`n" ''
Invoke-ExpectedFailure `
    -Label 'no-cap-tick' `
    -ProjectArguments @(
        "-p:OutputPath=$(Join-Path $scratch 'cap-tick-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'cap-tick-obj\')"
    ) `
    -ApplicationArguments @('--tactical-source', $noCap) `
    -Expected 'the assembly check waits while the request is held for the body move'

$dispatchPath = Join-Path $repoRoot 'ArenaMode.Client\Modes\SoloTacticalSquadDispatch.cs'
$dispatch = [System.IO.File]::ReadAllText($dispatchPath).Replace("`r`n", "`n")
function New-DispatchMutant([string]$Name, [string]$Find, [string]$Replace) {
    if ([regex]::Matches($dispatch, [regex]::Escape($Find)).Count -ne 1) {
        throw "Mutant anchor missing or ambiguous ($Name): $Find"
    }
    $path = Join-Path $scratch "SoloTacticalSquadDispatch-$Name.cs"
    [System.IO.File]::WriteAllText($path, $dispatch.Replace($Find, $Replace))
    return $path
}

$releaseAtOpen = New-DispatchMutant 'release-at-open' "        bool moving = false;`n" "        Release(round, SoloTacticalSquadRelease.NoBodyMove);`n        bool moving = false;`n"
Invoke-ExpectedFailure `
    -Label 'release-at-open' `
    -ProjectArguments @(
        "-p:ProductionDispatchSource=$releaseAtOpen",
        "-p:OutputPath=$(Join-Path $scratch 'open-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'open-obj\')"
    ) `
    -ApplicationArguments @() `
    -Expected "a round's request is held while its body move is in flight"

$longCap = New-DispatchMutant 'long-cap' 'BodyMoveCapSeconds = SoloTacticalSpawnPolicy.AssemblySeconds;' 'BodyMoveCapSeconds = SoloTacticalSpawnPolicy.AssemblySeconds + 15f;'
Invoke-ExpectedFailure `
    -Label 'long-cap' `
    -ProjectArguments @(
        "-p:ProductionDispatchSource=$longCap",
        "-p:OutputPath=$(Join-Path $scratch 'long-cap-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'long-cap-obj\')"
    ) `
    -ApplicationArguments @() `
    -Expected "a move that never reports holds the request no longer than the squad's own 45-second assembly window"

$doubleRelease = New-DispatchMutant 'double-release' "        if (!_pending || _round != round)`n            return;`n`n        // Cleared" "        if (_round != round)`n            return;`n`n        // Cleared"
Invoke-ExpectedFailure `
    -Label 'double-release' `
    -ProjectArguments @(
        "-p:ProductionDispatchSource=$doubleRelease",
        "-p:OutputPath=$(Join-Path $scratch 'double-bin\')",
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'double-obj\')"
    ) `
    -ApplicationArguments @() `
    -Expected 'a second report releases nothing and a released round no longer waits'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unmodified production checks failed after mutation verification.'
}

foreach ($path in $productionPaths) {
    $after = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($after -ne $productionHashes[$path]) {
        throw "Mutation verification changed production source: $path"
    }
}

Write-Output "SOLO_TACTICAL_SPAWN_REGRESSION_VERIFIED (twelve mutants rejected; evidence: $scratch)"
