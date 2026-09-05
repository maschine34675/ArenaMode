$ErrorActionPreference = 'Stop'
$tdmRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tdmProject = Join-Path $PSScriptRoot 'TeamDeathmatchChecks.csproj'
$tdmTemp = Join-Path ([IO.Path]::GetTempPath()) ('arena-tdm-policy-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tdmTemp)

$tdmSources = @{
    PopulationSource = 'ArenaMode.Client\Modes\TeamDeathmatchPopulation.cs'
    ScoreboardSource = 'ArenaMode.Client\Modes\ArenaScoreboard.cs'
    SpawnDispatchSource = 'ArenaMode.Client\Bots\TeamDeathmatchSpawnDispatch.cs'
}
$tdmOriginals = @{}
$tdmSnapshots = @{}
foreach ($tdmProperty in $tdmSources.Keys) {
    # Freeze all mutable sources once. Other leaves can continue working on
    # production files while each controlled mutant uses the same baseline.
    $tdmOriginal = [IO.File]::ReadAllText((Join-Path $tdmRepo $tdmSources[$tdmProperty]))
    $tdmOriginals[$tdmProperty] = $tdmOriginal
    $tdmSnapshot = Join-Path $tdmTemp ($tdmProperty + '.cs')
    [IO.File]::WriteAllText($tdmSnapshot, $tdmOriginal)
    $tdmSnapshots[$tdmProperty] = $tdmSnapshot
}

$tdmMutants = @(
    @{ Name = 'HumanSlot'; Property = 'PopulationSource';
       From = 'TeamSize - (usec == LocalUsec ? 1 : 0)'; To = 'TeamSize';
       Failure = 'human slot is reserved on its actual PMC side' },
    @{ Name = 'ParallelLoading'; Property = 'PopulationSource';
       From = '!item.Dispatched || IsCurrent(item, now)'; To = 'IsCurrent(item, now)';
       Failure = 'expiry does not start a concurrent profile assembly' },
    @{ Name = 'UnlimitedDebt'; Property = 'PopulationSource';
       From = 'MaxOutstandingPerFaction = 3'; To = 'MaxOutstandingPerFaction = 4';
       Failure = 'three uncertain dispatched requests pause further growth' },
    @{ Name = 'StaleAdmission'; Property = 'PopulationSource';
       From = 'now - ticket.CreatedAt < RequestTimeout'; To = 'true';
       Failure = 'ticket admission expires exactly at its bounded deadline' },
    @{ Name = 'UnstoppedPopulation'; Property = 'PopulationSource';
       From = '_stopped = true;'; To = '_stopped = false;';
       Failure = 'Stop invalidates callbacks and prohibits every later reservation' },
    @{ Name = 'FriendlyPoints'; Property = 'ScoreboardSource';
       From = 'if (kill.ScoreEligible)'; To = 'if (true)';
       Failure = 'friendly kills earn no points, kills, headshots or streak bonuses' },
    @{ Name = 'AllOpponentRows'; Property = 'ScoreboardSource';
       From = 'friendly == true ? ArenaScoreboardSide.Friendly : ArenaScoreboardSide.Opponent';
       To = 'ArenaScoreboardSide.Opponent';
       Failure = 'capture-time faction metadata classifies newly observed bot bodies' },
    @{ Name = 'LostArchivedTotals'; Property = 'ScoreboardSource';
       From = 'Score += entry.Score;'; To = 'Score += 0;';
       Failure = 'both faction score totals survive ordinary bot-history compaction' },
    @{ Name = 'UnboundedRows'; Property = 'ScoreboardSource';
       From = 'RetainedOrdinaryHistoryLimit = 32'; To = 'RetainedOrdinaryHistoryLimit = 1000000';
       Failure = 'thousands of bots retain bounded active and historical identity dictionaries' },
    @{ Name = 'ReusedDispatch'; Property = 'SpawnDispatchSource';
       From = 'lease.Consumed = true;'; To = 'lease.Consumed = false;';
       Failure = 'one lease authorizes exactly one native dispatch' },
    @{ Name = 'ForeignData'; Property = 'SpawnDispatchSource';
       From = 'ReferenceEquals(_data, data)'; To = 'true';
       Failure = 'dispatch ownership uses exact spawner and creation-data references' },
    @{ Name = 'SharedThreadScope'; Property = 'SpawnDispatchSource';
       From = '[ThreadStatic]'; To = '// mutation: thread ownership removed';
       Failure = 'synchronous ownership does not flow to another thread' },
    @{ Name = 'DisposedAncestor'; Property = 'SpawnDispatchSource';
       From = 'while (previous != null && previous._disposed)'; To = 'while (false)';
       Failure = 'out-of-order disposal skips closed ancestors and restores the live grandparent' }
)

foreach ($tdmMutant in $tdmMutants) {
    $tdmOriginal = $tdmOriginals[$tdmMutant.Property]
    if (-not $tdmOriginal.Contains($tdmMutant.From)) { throw "Missing mutation anchor: $($tdmMutant.Name)" }
    $tdmPath = Join-Path $tdmTemp ($tdmMutant.Name + '.cs')
    [IO.File]::WriteAllText($tdmPath, $tdmOriginal.Replace($tdmMutant.From, $tdmMutant.To))
    $tdmArguments = @('run', '--project', $tdmProject, '-c', ('TeamDeathmatchMutant' + $tdmMutant.Name))
    foreach ($tdmProperty in $tdmSources.Keys) {
        $tdmSourcePath = if ($tdmProperty -eq $tdmMutant.Property) { $tdmPath } else { $tdmSnapshots[$tdmProperty] }
        $tdmArguments += "-p:${tdmProperty}=$tdmSourcePath"
    }
    $tdmPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $tdmOutput = & dotnet @tdmArguments 2>&1 | Out-String
        $tdmExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $tdmPreference }
    if ($tdmExit -eq 0 -or -not $tdmOutput.Contains($tdmMutant.Failure)) {
        throw "Mutant did not fail its intended policy assertion: $($tdmMutant.Name)`n$tdmOutput"
    }
    Write-Output "Rejected mutant: $($tdmMutant.Name)"
}

# Production sources are only read, never edited. Running their current state
# again also detects integration drift since the frozen mutation baseline.
& dotnet run --project $tdmProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production Team Deathmatch checks failed' }
Write-Output 'ARENA_TEAM_DEATHMATCH_POLICY_REGRESSION_PASSED'
