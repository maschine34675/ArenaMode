$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'LabsBotSpawnChecks.csproj'
$policy = Join-Path $repoRoot 'ArenaMode.Shared\ArenaBotSpawnPolicy.cs'
$source = [System.IO.File]::ReadAllText($policy)
$pattern = '(public static bool IsExcludedZone\([^)]*\))\s*=>[^;]+;'
$matchesFound = [regex]::Matches($source, $pattern)
if ($matchesFound.Count -ne 1) {
    throw 'Expected exactly one exclusion policy expression; refusing an ambiguous mutation.'
}

# Only the temporary copy is changed. Build output is kept beside it as evidence.
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaLabsSpawnMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
$mutant = Join-Path $scratch 'ArenaBotSpawnPolicy.cs'
[System.IO.File]::WriteAllText($mutant, [regex]::Replace($source, $pattern, '$1 => false;'))
$mutantOutput = Join-Path $scratch 'bin\'
$mutantIntermediate = Join-Path $scratch 'obj\'
$output = & dotnet run --project $project -c Release "-p:ProductionPolicySource=$mutant" "-p:OutputPath=$mutantOutput" "-p:IntermediateOutputPath=$mutantIntermediate" 2>&1
$mutantExit = $LASTEXITCODE
$output | ForEach-Object { Write-Output $_ }
if ($mutantExit -eq 0 -or ($output -join "`n") -notmatch 'LABS_BOT_SPAWN_CHECKS_FAILED.*armed/loading Labs: three safe zones retained in order') {
    throw "Mutation did not fail at the expected gameplay boundary (exit $mutantExit). Evidence: $scratch"
}

# Independently disable the client session guard while retaining the real shared
# policy. This must fail through the production client postfix, not a policy unit
# assertion, proving the hook behavior rather than only the shared zone table.
$filter = Join-Path $repoRoot 'ArenaMode.Client\Bots\ArenaBotSpawnFilter.cs'
$filterSource = [System.IO.File]::ReadAllText($filter)
$filterPattern = '(private static bool IsEnabled)\s*=>[^;]+;'
if ([regex]::Matches($filterSource, $filterPattern).Count -ne 1) {
    throw 'Expected exactly one client session guard; refusing an ambiguous mutation.'
}
$filterMutant = Join-Path $scratch 'ArenaBotSpawnFilter.cs'
[System.IO.File]::WriteAllText($filterMutant, [regex]::Replace($filterSource, $filterPattern, '$1 => false;'))
$filterOutput = Join-Path $scratch 'client-bin\'
$filterIntermediate = Join-Path $scratch 'client-obj\'
$output = & dotnet run --project $project -c Release "-p:ProductionFilterSource=$filterMutant" "-p:OutputPath=$filterOutput" "-p:IntermediateOutputPath=$filterIntermediate" 2>&1
$mutantExit = $LASTEXITCODE
$output | ForEach-Object { Write-Output $_ }
if ($mutantExit -eq 0 -or ($output -join "`n") -notmatch 'LABS_BOT_SPAWN_CHECKS_FAILED.*armed/loading Labs: three safe zones retained in order') {
    throw "Client mutation did not fail at the expected gameplay boundary (exit $mutantExit). Evidence: $scratch"
}

# Recheck the unmodified production files; a failed mutation must never leave
# a mutant binary as the final positive result.
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unmodified production checks failed after the mutation run.'
}
Write-Output "LABS_BOT_SPAWN_REGRESSION_VERIFIED (both mutants rejected; evidence: $scratch)"
