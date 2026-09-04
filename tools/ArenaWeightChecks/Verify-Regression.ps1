$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaWeightChecks.csproj'
$tuningPath = Join-Path $repoRoot 'ArenaMode.Client\Movement\ArenaWeightTuning.cs'
$patchPath = Join-Path $repoRoot 'ArenaMode.Client\Movement\ArenaWeightPatches.cs'
$runtimePath = Join-Path $repoRoot 'ArenaMode.Client\Movement\MovementRuntime.cs'
$tuning = [System.IO.File]::ReadAllText($tuningPath)
$patch = [System.IO.File]::ReadAllText($patchPath)
$runtime = [System.IO.File]::ReadAllText($runtimePath)
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaWeightMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase(
    [string]$name,
    [string]$tuningSource,
    [string]$patchSource,
    [string]$runtimeSource,
    [string]$expectedFailure) {
    $tuningCopy = Join-Path $scratch ($name + '-tuning.cs')
    $patchCopy = Join-Path $scratch ($name + '-patch.cs')
    $runtimeCopy = Join-Path $scratch ($name + '-runtime.cs')
    [System.IO.File]::WriteAllText($tuningCopy, $tuningSource)
    [System.IO.File]::WriteAllText($patchCopy, $patchSource)
    [System.IO.File]::WriteAllText($runtimeCopy, $runtimeSource)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release `
        "-p:ProductionTuningSource=$tuningCopy" `
        "-p:ProductionPatchSource=$patchCopy" `
        "-p:ProductionRuntimeSource=$runtimeCopy" `
        "-p:OutputPath=$outputPath" `
        "-p:IntermediateOutputPath=$intermediatePath" 2>&1
    $resultCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($resultCode -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($expectedFailure)) {
        throw "$name did not fail at the expected boundary (exit $resultCode). Evidence: $scratch"
    }
}

$arenaGuard = 'if (!ArenaSession.IsActive'
if ([regex]::Matches($patch, [regex]::Escape($arenaGuard)).Count -ne 1) {
    throw 'Expected exactly one active-Arena gate; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'ordinary-raid-leak' $tuning ($patch.Replace($arenaGuard, 'if (false')) $runtime `
    'ordinary raid is unchanged'

$speedLine = '__instance.WalkSpeedOverweightLimits = ArenaWeightTuning.ScaleLimits('
if ([regex]::Matches($patch, [regex]::Escape($speedLine)).Count -ne 1) {
    throw 'Expected exactly one walk-speed scaling assignment; refusing an ambiguous mutation.'
}
$withoutSpeed = $patch.Replace($speedLine, '_ = ArenaWeightTuning.ScaleLimits(')
Invoke-NegativeCase 'missing-speed-range' $tuning $withoutSpeed $runtime `
    'local Arena walk-speed range scales'

$respawnGate = '|| !ReferenceEquals(player.Physical, _lastWeightPhysical)'
if ([regex]::Matches($runtime, [regex]::Escape($respawnGate)).Count -ne 1) {
    throw 'Expected exactly one Physical replacement gate; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'respawn-not-reapplied' $tuning $patch ($runtime.Replace($respawnGate, '')) `
    'respawned Physical receives the Arena limits'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production Arena weight checks failed after mutation testing.'
}
Write-Output "ARENA_WEIGHT_REGRESSION_VERIFIED (Arena gate, complete limit set and respawn mutants rejected; evidence: $scratch)"
