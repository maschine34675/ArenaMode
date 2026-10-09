param([switch]$RequireInstalledDatabase)

$ErrorActionPreference = 'Stop'
$appArgs = if ($RequireInstalledDatabase) { @('--', '--require-factory-database') } else { @() }
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

& dotnet run --project $project -c Release @appArgs
if ($LASTEXITCODE -ne 0) { throw 'Production Factory baseline failed before mutation checks.' }

$policyPattern = '(public static bool IsExcludedPoint\([^)]*\))\s*=>[^;]+;'
if ([regex]::Matches($texts.ProductionPolicySource, $policyPattern).Count -ne 1) { throw 'Ambiguous exact point policy mutation anchor.' }
$pointDisabled = [regex]::Replace($texts.ProductionPolicySource, $policyPattern, '$1 => false;')
$nightMapAnchor = '|| string.Equals(mapId, "factory4_night", StringComparison.OrdinalIgnoreCase)'
if ([regex]::Matches($texts.ProductionPolicySource, [regex]::Escape($nightMapAnchor)).Count -ne 1) { throw 'Night map classification mutation anchor missing.' }
$nightMapDisabled = $texts.ProductionPolicySource.Replace($nightMapAnchor, '')
$groupId = 'c2d01ce0-e0e4-41af-8311-44dc5cf38a93'
$pointRuleAnchor = '&& FactoryExcludedPointIds.Contains(pointId);'
if (!$texts.ProductionPolicySource.Contains($pointRuleAnchor)) { throw 'Night-only group exclusion mutation anchor missing.' }
$nightGroupAllowed = $texts.ProductionPolicySource.Replace($pointRuleAnchor,
    "&& !(string.Equals(mapId, `"factory4_night`", StringComparison.OrdinalIgnoreCase) && string.Equals(pointId, `"$groupId`", StringComparison.OrdinalIgnoreCase)) $pointRuleAnchor")
$database = [Environment]::GetEnvironmentVariable('SPT_DATABASE')
if ($null -eq $database) { $database = 'D:\SPT41\SPT_Runtime\SPT_Data\database' }
$nightGroupFailure = if ([IO.Directory]::Exists($database)) {
    'factory4_night: installed scene view removes exactly the reported points and retains every other physical object'
} else { "factory4_night: every reported unsafe marker is excluded in either casing: $groupId" }

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
$zombieModeAnchor = '&& !string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase)'
if (!$texts.ProductionTeamPointsSource.Contains($zombieModeAnchor)) { throw 'Zombie team mode mutation anchor missing.' }
$zombieModeDisabled = $texts.ProductionTeamPointsSource.Replace($zombieModeAnchor, '')
$pftAnchor = '|| !PitFireTeamCompat.TeamArenaActive'
if (!$texts.ProductionTeamPointsSource.Contains($pftAnchor)) { throw 'Protected team mutation anchor missing.' }
$pftDisabled = $texts.ProductionTeamPointsSource.Replace($pftAnchor, '')

$worldPattern = '(internal static bool IsPointRestrictedRaid)\s*=>[^;]+;'
if ([regex]::Matches($texts.ProductionFilterSource, $worldPattern).Count -ne 1) { throw 'World ownership guard mutation anchor missing.' }
$worldGuardDisabled = [regex]::Replace($texts.ProductionFilterSource, $worldPattern,
    '$1 => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) && ArenaBotSpawnPolicy.HasPointExclusions(ArenaSession.ArmedMap);')

$mutants = @(
    @{ Name='DisabledPointPolicy'; Key='ProductionPolicySource'; Text=$pointDisabled; Failure='factory4_day armed: exact unsafe Factory point rejects complete AI batch' },
    @{ Name='MissingNightMap'; Key='ProductionPolicySource'; Text=$nightMapDisabled; Failure='factory4_night armed: exact unsafe Factory point rejects complete AI batch' },
    @{ Name='NightUndergroundGap'; Key='ProductionPolicySource'; Text=$nightGroupAllowed; Failure=$nightGroupFailure },
    @{ Name='DisabledPlannerFilter'; Key='ProductionPlannerSource'; Text=$plannerDisabled; Failure='factory4_day: planner skips unsafe Factory ID before physical-position dedupe and fills five safe points' },
    @{ Name='UnsafeCapacity'; Key='ProductionPlannerSource'; Text=$capacityDisabled; Failure='factory4_day: unsafe distinct position never inflates squad capacity' },
    @{ Name='LeakedTeamView'; Key='ProductionTeamPointsSource'; Text=$teamGuardDisabled; Failure='team scene view remains untouched: stale' },
    @{ Name='StaleWorld'; Key='ProductionFilterSource'; Text=$worldGuardDisabled; Failure='Factory stale or foreign world does not inherit point restrictions: wrongmap' },
    @{ Name='StaleActiveMode'; Key='ProductionTeamPointsSource'; Text=$modeReversed; Failure='team scene view remains untouched: bothflagswrongarmedmode' }
    @{ Name='ZombieLeaderGap'; Key='ProductionTeamPointsSource'; Text=$zombieModeDisabled; Failure='factory4_day armed zombie team: protected team scene view excludes exact Player marker without removing its neighbor' },
    @{ Name='UnprotectedLeaderLeak'; Key='ProductionTeamPointsSource'; Text=$pftDisabled; Failure='team scene view remains untouched: solo' }
)
foreach ($courtyardId in @('4755c538-4ee5-4125-8cd3-885787da7ad5', '6d97a439-de10-42c5-99ef-6cf92e243aca',
    'e60d8dce-ee35-4b30-9c3a-afddf4c5445e', 'bcae7a9f-8b2f-4a4c-9cb0-589d4a227c70')) {
    $quotedId = '"' + $courtyardId + '"'
    if ([regex]::Matches($texts.ProductionPolicySource, [regex]::Escape($quotedId)).Count -ne 1) { throw "Courtyard marker mutation anchor missing: $courtyardId" }
    $missingId = $texts.ProductionPolicySource.Replace($quotedId, '"removed-courtyard-marker"')
    $failure = if ([IO.Directory]::Exists($database)) {
        'factory4_day: installed scene view removes exactly the reported points and retains every other physical object'
    } else { "factory4_day: every reported unsafe marker is excluded in either casing: $courtyardId" }
    $mutants += @{ Name=('MissingCourtyard' + $courtyardId.Substring(0, 8)); Key='ProductionPolicySource'; Text=$missingId; Failure=$failure }
}
foreach ($mutant in $mutants) {
    $path = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($path, $mutant.Text)
    $runArgs = @('run', '--project', $project, '-c', ('FactoryMutant' + $mutant.Name))
    foreach ($key in $sources.Keys) {
        $source = if ($key -eq $mutant.Key) { $path } else { $snapshots[$key] }
        $runArgs += "-p:${key}=$source"
    }
    $output = & dotnet @runArgs @appArgs 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('LABS_BOT_SPAWN_CHECKS_FAILED') -or !$output.Contains($mutant.Failure)) {
        throw "Factory mutant did not fail its intended gameplay assertion: $($mutant.Name)`n$output"
    }
    Write-Output "Rejected Factory mutant: $($mutant.Name)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) { throw "Production source changed during Factory checks: $key" }
}
& dotnet run --project $project -c Release @appArgs
if ($LASTEXITCODE -ne 0) { throw 'Unmodified production Factory checks failed after mutation checks.' }
Write-Output "FACTORY_BOT_SPAWN_REGRESSION_VERIFIED ($($mutants.Count) mutants rejected; evidence: $scratch)"
