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
    $tactical.IndexOf('_squad.Clear();', $requestAt, [StringComparison]::Ordinal)
} else {
    -1
}
if ($requestAt -lt 0 -or $clearAt -lt 0) {
    throw 'Expected the exact same-round retry boundary.'
}
$insertAt = $clearAt + '_squad.Clear();'.Length
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

Write-Output "SOLO_TACTICAL_SPAWN_REGRESSION_VERIFIED (three mutants rejected; evidence: $scratch)"
