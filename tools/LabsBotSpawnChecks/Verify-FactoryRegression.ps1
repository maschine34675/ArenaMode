$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'LabsBotSpawnChecks.csproj'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaFactorySpawnMutation-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$sources = @{
    ProductionPolicySource = Join-Path $repo 'ArenaMode.Shared/ArenaBotSpawnPolicy.cs'
    ProductionFilterSource = Join-Path $repo 'ArenaMode.Client/Bots/ArenaBotSpawnFilter.cs'
    ProductionPlannerSource = Join-Path $repo 'ArenaMode.Client/Bots/BurstSpawnPointSelector.cs'
    ProductionTeamPointsSource = Join-Path $repo 'ArenaMode.Client/Bots/ArenaTeamSpawnPointsPatch.cs'
}
$texts = @{}; $snapshots = @{}; $hashes = @{}
foreach ($key in $sources.Keys) {
    $texts[$key] = [IO.File]::ReadAllText($sources[$key])
    $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
    $snapshots[$key] = Join-Path $scratch ($key + '.cs')
    [IO.File]::WriteAllText($snapshots[$key], $texts[$key])
}

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production Factory baseline failed before mutation checks.' }

$policyPattern = '(public static bool IsExcludedPoint\([^)]*\))\s*=>[^;]+;'
if ([regex]::Matches($texts.ProductionPolicySource, $policyPattern).Count -ne 1) { throw 'Ambiguous exact point policy mutation anchor.' }
$pointDisabled = [regex]::Replace($texts.ProductionPolicySource, $policyPattern, '$1 => false;')

$plannerAnchor = ' || ArenaBotSpawnFilter.IsExcludedPoint(point)'
if ([regex]::Matches($texts.ProductionPlannerSource, [regex]::Escape($plannerAnchor)).Count -ne 2) { throw 'Expected independent selection and capacity point filters.' }
$plannerDisabled = $texts.ProductionPlannerSource.Replace($plannerAnchor, '')
$capacityStart = $texts.ProductionPlannerSource.IndexOf('internal static int CountUsable(')
if ($capacityStart -lt 0) { throw 'Capacity method mutation anchor missing.' }
$capacityDisabled = $texts.ProductionPlannerSource.Substring(0, $capacityStart) + $texts.ProductionPlannerSource.Substring($capacityStart).Replace($plannerAnchor, '')

$teamAnchor = '!ArenaBotSpawnFilter.IsPointRestrictedRaid'
if (!$texts.ProductionTeamPointsSource.Contains($teamAnchor)) { throw 'Team scene session guard anchor missing.' }
$teamGuardDisabled = $texts.ProductionTeamPointsSource.Replace($teamAnchor, 'false')
$modeAnchor = 'ArenaSession.ArmedForNextRaid ? ArenaSession.ArmedMode : ArenaSession.ActiveMode'
if (!$texts.ProductionTeamPointsSource.Contains($modeAnchor)) { throw 'Armed mode precedence anchor missing.' }
$modeReversed = $texts.ProductionTeamPointsSource.Replace($modeAnchor, 'ArenaSession.IsActive ? ArenaSession.ActiveMode : ArenaSession.ArmedMode')

$worldPattern = '(internal static bool IsPointRestrictedRaid)\s*=>[^;]+;'
if ([regex]::Matches($texts.ProductionFilterSource, $worldPattern).Count -ne 1) { throw 'World ownership guard mutation anchor missing.' }
$worldGuardDisabled = [regex]::Replace($texts.ProductionFilterSource, $worldPattern,
    '$1 => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) && ArenaBotSpawnPolicy.HasPointExclusions(ArenaSession.ArmedMap);')

$mutants = @(
    @{ Name='DisabledPointPolicy'; Key='ProductionPolicySource'; Text=$pointDisabled; Failure='factory4_day armed: exact unsafe Factory point rejects complete AI batch' },
    @{ Name='DisabledPlannerFilter'; Key='ProductionPlannerSource'; Text=$plannerDisabled; Failure='factory4_day: planner skips unsafe Factory ID before physical-position dedupe and fills five safe points' },
    @{ Name='UnsafeCapacity'; Key='ProductionPlannerSource'; Text=$capacityDisabled; Failure='factory4_day: unsafe distinct position never inflates squad capacity' },
    @{ Name='LeakedTeamView'; Key='ProductionTeamPointsSource'; Text=$teamGuardDisabled; Failure='team scene view remains untouched: stale' },
    @{ Name='StaleWorld'; Key='ProductionFilterSource'; Text=$worldGuardDisabled; Failure='Factory stale or foreign world does not inherit point restrictions: wrongmap' },
    @{ Name='StaleActiveMode'; Key='ProductionTeamPointsSource'; Text=$modeReversed; Failure='team scene view remains untouched: bothflagswrongarmedmode' }
)
foreach ($mutant in $mutants) {
    $path = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($path, $mutant.Text)
    $runArgs = @('run', '--project', $project, '-c', ('FactoryMutant' + $mutant.Name))
    foreach ($key in $sources.Keys) {
        $source = if ($key -eq $mutant.Key) { $path } else { $snapshots[$key] }
        $runArgs += "-p:${key}=$source"
    }
    $output = & dotnet @runArgs 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('LABS_BOT_SPAWN_CHECKS_FAILED') -or !$output.Contains($mutant.Failure)) {
        throw "Factory mutant did not fail its intended gameplay assertion: $($mutant.Name)`n$output"
    }
    Write-Output "Rejected Factory mutant: $($mutant.Name)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) { throw "Production source changed during Factory checks: $key" }
}
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Unmodified production Factory checks failed after mutation checks.' }
Write-Output "FACTORY_BOT_SPAWN_REGRESSION_VERIFIED ($($mutants.Count) mutants rejected; evidence: $scratch)"
