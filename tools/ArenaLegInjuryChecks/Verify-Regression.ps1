$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaLegInjuryChecks.csproj'
$tuningPath = Join-Path $repoRoot 'ArenaMode.Client\Movement\ArenaLegInjuryTuning.cs'
$patchPath = Join-Path $repoRoot 'ArenaMode.Client\Movement\ArenaLegInjuryPatches.cs'
$tuning = [System.IO.File]::ReadAllText($tuningPath)
$patch = [System.IO.File]::ReadAllText($patchPath)
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaLegInjuryMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase(
    [string]$name,
    [string]$tuningSource,
    [string]$patchSource,
    [string]$expectedFailure) {
    $tuningCopy = Join-Path $scratch ($name + '-tuning.cs')
    $patchCopy = Join-Path $scratch ($name + '-patch.cs')
    [System.IO.File]::WriteAllText($tuningCopy, $tuningSource)
    [System.IO.File]::WriteAllText($patchCopy, $patchSource)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release `
        "-p:ProductionTuningSource=$tuningCopy" `
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
Invoke-NegativeCase 'ordinary-raid-leak' $tuning ($patch.Replace($arenaGuard, 'if (false')) `
    'ordinary raid is unchanged'

$penaltyFormula = 'vanillaSpeedLimit + (1f - vanillaSpeedLimit) * reduction'
if ([regex]::Matches($tuning, [regex]::Escape($penaltyFormula)).Count -ne 1) {
    throw 'Expected exactly one penalty-reduction formula; refusing an ambiguous mutation.'
}
$brokenFormula = 'vanillaSpeedLimit * reduction'
Invoke-NegativeCase 'wrong-penalty-formula' ($tuning.Replace($penaltyFormula, $brokenFormula)) $patch `
    '50% reduction halves the vanilla one-leg penalty'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production leg-injury checks failed after mutation testing.'
}
Write-Output "ARENA_LEG_INJURY_REGRESSION_VERIFIED (Arena gate and formula mutants rejected; evidence: $scratch)"
