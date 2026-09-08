param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamDeathmatchRuntimeChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$mode = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$population = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchPopulation.cs'
$relations = Join-Path $repo 'ArenaMode.Client/Bots/TeamDeathmatchRelations.cs'
$awareness = Join-Path $repo 'ArenaMode.Client/Bots/TdmSearchAwareness.cs'
$sources = @($mode, $population, $relations, $awareness)
$before = @{}
foreach ($source in $sources) { $before[$source] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-tdm-runtime-mutations-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-Model([string[]] $properties) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet run --project $project --configuration Release @properties 2>&1 |
            ForEach-Object { $_.ToString() } | Out-String -Width 10000
        $code = $LASTEXITCODE
        return @{ Output = $output; Code = $code }
    }
    finally { $ErrorActionPreference = $previous }
}

$baseline = Invoke-Model @()
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'ARENA_TDM_RUNTIME_CHECKS_PASSED') {
    throw ('Runtime model baseline failed: ' + $baseline.Output)
}
$cases = @(
    @{
        Name = 'human-slot'; Path = $population; Property = 'PopulationSource';
        Find = '=> TeamSize - (usec == LocalUsec ? 1 : 0);'; Replace = '=> TeamSize;';
        Failure = 'human capacity: local faction has exactly N minus one bots'
    },
    @{
        Name = 'friendly-native-admission'; Path = $relations; Property = 'RelationsSource';
        Find = 'if (!TryGroupRelationship(__instance, person, out bool enemies) || enemies) return true;';
        Replace = 'if (true) return true;';
        Failure = 'native friendly admission rejects initial, revenge and direct causes'
    },
    @{
        Name = 'partial-respawn'; Path = $mode; Property = 'ModeSource';
        Find = 'if (!Respawn.ArenaRespawnDriver.IsRespawnInProgress'; Replace = 'if (true';
        Failure = 'intermediate local body published during respawn must not be adopted before readiness'
    },
    @{
        Name = 'native-stop-window'; Path = $mode; Property = 'ModeSource';
        Find = @('if (_nativeStopObserved) return true;', ' && _game.Status == GameStatus.Started');
        Replace = @('if (true) return false;', '');
        Failure = 'native teardown wrappers must not count as activation failures or be killed by TDM'
    },
    @{
        Name = 'detached-request-status'; Path = $mode; Property = 'ModeSource';
        Find = ' && _game.Status == GameStatus.Started'; Replace = '';
        Failure = 'native stop immediately invalidates detached request predicates'
    },
    @{
        Name = 'reentrant-stop-failure'; Path = $mode; Property = 'ModeSource';
        Find = 'if (_failure != null || ObserveNativeStop()) return;'; Replace = 'if (_failure != null) return;';
        Failure = 'reentrant stop during retirement cannot become a false mode failure or warning'
    },
    @{
        Name = 'shutdown-ready-cue'; Path = $mode; Property = 'ModeSource';
        Find = 'if (ObserveNativeStop() || !IsCurrentRaid()) return;'; Replace = '';
        Failure = 'stop in last arrival prevents a round-start announcement during native shutdown'
    },
    @{
        Name = 'diagnostic-spam'; Path = $mode; Property = 'ModeSource';
        Find = 'if (!_reportedUnreadableRoster)'; Replace = 'if (true)';
        Failure = 'unreadable roster is reported once while refills remain paused'
    },
    @{
        Name = 'diagnostic-request-phase'; Path = $population; Property = 'PopulationSource';
        Find = 'item.Preparing ? "preparing" : "native"'; Replace = '"preparing"';
        Failure = 'timeout diagnostics distinguish faction capacity, profile loading and uncertain native work'
    },
    @{
        Name = 'cold-load-expiry'; Path = $population; Property = 'PopulationSource';
        Find = 'now - ticket.CreatedAt < PreparationTimeout'; Replace = 'now - ticket.CreatedAt < RequestTimeout';
        Failure = '154-second cold preparation remains owned without a replacement load'
    },
    @{
        Name = 'preparation-consumes-arrival'; Path = $population; Property = 'PopulationSource';
        Find = 'now - ticket.SubmittedAt < RequestTimeout'; Replace = 'now - ticket.CreatedAt < RequestTimeout';
        Failure = 'cold preparation does not consume the separate native arrival budget'
    },
    @{
        Name = 'admit-preparing-body'; Path = $mode; Property = 'ModeSource';
        Find = '!_population.CanAdmit(ticket, Time.realtimeSinceStartup)'; Replace = '!_population.IsCurrent(ticket, Time.realtimeSinceStartup)';
        Failure = 'an observer cannot admit a body while its ticket is still preparing'
    },
    @{
        Name = 'watchdog-simulation-throttle'; Path = $mode; Property = 'ModeSource';
        Find = 'if (ObserveNativeStop() || CheckPreparationWatchdog()) return;'; Replace = 'if (ObserveNativeStop()) return;';
        Failure = 'preparation watchdog terminally closes ownership even between simulation ticks'
    },
    @{
        Name = 'watchdog-completion-race'; Path = $mode; Property = 'ModeSource';
        Find = 'if (CheckPreparationWatchdog(ticket)) return;'; Replace = '';
        Failure = 'preparation watchdog terminally closes ownership even between simulation ticks'
    },
    @{
        Name = 'stale-initial-readiness'; Path = $mode; Property = 'ModeSource';
        Find = '_announcedReady && IsCurrentMatch()'; Replace = '_announcedReady';
        Failure = 'old match readiness cannot release another raid''s optional warmup'
    },
    @{
        Name = 'diagnostic-after-cancellation'; Path = $mode; Property = 'ModeSource';
        Find = 'string state = DescribeState(); // capture pending work before Stop clears it';
        Replace = '_population.Stop(); string state = DescribeState();';
        Failure = 'shutdown diagnostics capture pending work before cancellation clears it'
    },
    @{
        Name = 'diagnostic-exception-isolation'; Path = $mode; Property = 'ModeSource';
        Find = 'catch (Exception ex) { return "roster snapshot unavailable (" + ex.GetType().Name + ")"; }';
        Replace = 'catch (Exception) { throw; }';
        Failure = 'a broken diagnostic snapshot cannot interrupt native shutdown or fail the mode'
    }
)
foreach ($case in $cases) {
    $text = [IO.File]::ReadAllText($case.Path)
    $finds = @($case.Find)
    $replacements = @($case.Replace)
    if ($finds.Count -ne $replacements.Count) { throw ('Mutation replacement count differs: ' + $case.Name) }
    for ($i = 0; $i -lt $finds.Count; $i++) {
        if (($text.Split(@($finds[$i]), [StringSplitOptions]::None).Length - 1) -ne 1) {
            throw ('Mutation anchor must occur exactly once: ' + $case.Name)
        }
        $text = $text.Replace($finds[$i], $replacements[$i])
    }
    $copy = Join-Path $scratch ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $text)
    $result = Invoke-Model @('-p:' + $case.Property + '=' + $copy)
    if ($result.Code -eq 0 -or $result.Output -notmatch [regex]::Escape($case.Failure)) {
        throw ('Mutation did not fail for its intended behavioral assertion: ' + $case.Name + [Environment]::NewLine + $result.Output)
    }
    Write-Output ('PASS mutation: ' + $case.Name)
}
foreach ($source in $sources) {
    if ($before[$source] -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
        throw ('Production source changed during regression runner: ' + $source)
    }
}
$restored = Invoke-Model @()
if ($restored.Code -ne 0 -or $restored.Output -notmatch 'ARENA_TDM_RUNTIME_CHECKS_PASSED') {
    throw ('Unchanged production source failed after mutation controls: ' + $restored.Output)
}
Write-Output $restored.Output.Trim()
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'BurstIntegration/Run.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Extracted production burst preparation regression failed' }
Write-Output 'ARENA_TDM_RUNTIME_REGRESSION_PASSED'
