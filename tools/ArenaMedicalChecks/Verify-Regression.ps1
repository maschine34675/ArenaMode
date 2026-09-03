$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaMedicalChecks.csproj'
$timingPath = Join-Path $repoRoot 'ArenaMode.Client\Combat\ArenaMedicalTiming.cs'
$patchPath = Join-Path $repoRoot 'ArenaMode.Client\Combat\ArenaMedicalPatches.cs'
$timing = [System.IO.File]::ReadAllText($timingPath)
$patch = [System.IO.File]::ReadAllText($patchPath)
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaMedicalMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase(
    [string]$name,
    [string]$timingSource,
    [string]$patchSource,
    [string]$expectedFailure) {
    $timingCopy = Join-Path $scratch ($name + '-timing.cs')
    $patchCopy = Join-Path $scratch ($name + '-patch.cs')
    [System.IO.File]::WriteAllText($timingCopy, $timingSource)
    [System.IO.File]::WriteAllText($patchCopy, $patchSource)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release `
        "-p:ProductionTimingSource=$timingCopy" `
        "-p:ProductionPatchSource=$patchCopy" `
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
Invoke-NegativeCase 'ordinary-raid-leak' $timing ($patch.Replace($arenaGuard, 'if (false')) `
    'ordinary raid keeps delay'

$progress = 'progress * scaledPhaseDuration,'
if ([regex]::Matches($timing, [regex]::Escape($progress)).Count -ne 1) {
    throw 'Expected exactly one scaled-progress expression; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'unscaled-progress' ($timing.Replace($progress, 'stateElapsed,')) $patch `
    'waiting-state progress percentage is preserved'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production medical checks failed after mutation testing.'
}
Write-Output "ARENA_MEDICAL_REGRESSION_VERIFIED (Arena gate and progress mutants rejected; evidence: $scratch)"
