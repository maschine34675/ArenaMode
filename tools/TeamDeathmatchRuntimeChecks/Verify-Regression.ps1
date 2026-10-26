param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamDeathmatchRuntimeChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$mode = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$population = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchPopulation.cs'
$relations = Join-Path $repo 'ArenaMode.Client/Bots/TeamDeathmatchRelations.cs'
$awareness = Join-Path $repo 'ArenaMode.Client/Bots/TdmSearchAwareness.cs'
$flow = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMatchFlow.cs'
$policy = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMatchPolicy.cs'
$shield = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.SpawnProtection.cs'
$shieldLease = Join-Path $repo 'ArenaMode.Client/Modes/TeamSpawnProtectionLease.cs'
$sources = @($mode, $population, $relations, $awareness, $flow, $policy, $shield, $shieldLease)
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
        Name = 'spawn-shield-no-expiry'; Path = $shieldLease; Property = 'ShieldLeaseSource';
        Find = 'now - _started.Value >= _seconds'; Replace = 'false';
        Failure = 'pending activation does not consume duration, configuration capped at 30 seconds'
    },
    @{
        Name = 'spawn-shield-no-attack-release'; Path = $shield; Property = 'ShieldSource';
        Find = 'shield.Lease.End();'; Replace = '';
        Failure = 'first attack ends damage shield and hides its visual synchronously'
    },
    @{
        Name = 'spawn-shield-no-world-registration'; Path = $shield; Property = 'ShieldSource';
        Find = '&& ReferenceEquals(_world.GetAlivePlayerByProfileID(shield.ProfileId), shield.Body)'; Replace = '';
        Failure = 'stale body/game/identity cannot retain immunity'
    },
    @{
        Name = 'spawn-gates-countdown'; Path = $flow; Property = 'FlowSource';
        Find = '&& !Bots.ArenaTeamBoxRuntime.TryOpenForCombat()) return;';
        Replace = '&& false) return;';
        Failure = 'GATES: incomplete opening keeps timer and round cue held'
    },
    @{
        Name = 'search-phase-admission'; Path = $flow; Property = 'FlowSource';
        Find = 'internal bool IsSearchActive => IsCurrentMatch() && MatchPhase == TdmMatchPhase.Live;';
        Replace = 'internal bool IsSearchActive => IsCurrentMatch();';
        Failure = 'search admission is disabled while assembling'
    },
    @{
        Name = 'search-read-publishes-result'; Path = $flow; Property = 'FlowSource';
        Find = 'internal bool IsSearchActive => IsCurrentMatch() && MatchPhase == TdmMatchPhase.Live;';
        Replace = 'internal bool IsSearchActive => TryBeginScoredEvent();';
        Failure = 'search phase read does not publish an overdue result or advance lifecycle'
    },
    @{
        Name = 'human-slot'; Path = $population; Property = 'PopulationSource';
        Find = '=> TeamSize - (usec == LocalUsec ? 1 : 0);'; Replace = '=> TeamSize;';
        # The negotiated-total case now runs first and catches the same lost
        # reserved human slot before the legacy-team-size capacity case.
        Failure = 'frozen full-map total overrides legacy team setting, includes human, stays equal after F12 edit'
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
    },
    @{
        Name = 'initial-kit-readiness'; Path = $mode; Property = 'ModeSource';
        Find = '&& !Respawn.LiveKitRespawn.IsInitialSwapPending;'; Replace = ';';
        Failure = 'the initial player kit swap must finish before countdown admission'
    },
    @{
        Name = 'unreadable-countdown'; Path = $mode; Property = 'ModeSource';
        Find = "if (owners == null)`r`n            {`r`n                TickMatchStart(false);";
        Replace = "if (owners == null)`r`n            {";
        Failure = 'unreadable native roster cancels an already running countdown'
    },
    @{
        Name = 'subtick-readiness-loss'; Path = $mode; Property = 'ModeSource';
        Find = 'if (IsCurrentMatch() && MatchPhase == TdmMatchPhase.Countdown';
        Replace = 'if (false && MatchPhase == TdmMatchPhase.Countdown';
        Failure = 'short local readiness loss cancels countdown before the roster tick'
    },
    @{
        Name = 'terminal-board-sync'; Path = $mode; Property = 'ModeSource';
        Find = '|| !IsCurrentRaid() || HasResult) return true;';
        Replace = '|| !IsCurrentRaid()) return true;';
        Failure = 'terminal roster synchronization cannot mutate the final scoreboard'
    },
    @{
        Name = 'terminal-kill-classification'; Path = $mode; Property = 'ModeSource';
        Find = 'kill.ScoreEligible &= !_ended && !IsComplete && IsCurrentRaid() && MatchPhase == TdmMatchPhase.Live';
        Replace = 'kill.ScoreEligible &= !_ended && IsCurrentRaid()';
        Failure = 'terminal delayed kills have neither score nor award eligibility'
    },
    @{
        Name = 'staging-score-admission'; Path = $flow; Property = 'FlowSource';
        Find = 'return !IsComplete && MatchPhase == TdmMatchPhase.Live;'; Replace = 'return !IsComplete;';
        Failure = 'preactive full roster cannot release scoring, time or staging damage'
    },
    @{
        Name = 'result-before-engine-drain'; Path = $flow; Property = 'FlowSource';
        Find = 'if (TdmMatchExit.PipelineRunning) return;'; Replace = '';
        Failure = 'result display waits for actual replacement work without spending its display interval'
    },
    @{
        Name = 'result-waits-held-death'; Path = $flow; Property = 'FlowSource';
        Find = 'if (TdmMatchExit.PipelineRunning) return;';
        Replace = 'if (TdmMatchExit.PipelineRunning || Respawn.ArenaRespawnDriver.IsRespawnInProgress) return;';
        Failure = 'terminal match requests exit after its display without refills or final-score mutation'
    },
    @{
        Name = 'late-time-limit'; Path = $policy; Property = 'PolicySource';
        Find = 'now - StartedAt >= TimeLimitSeconds'; Replace = 'now - StartedAt > TimeLimitSeconds';
        Failure = 'frozen time-limit settings finish an equal score as a draw exactly at deadline'
    }
)
foreach ($case in $cases) {
    $text = [IO.File]::ReadAllText($case.Path).Replace("`r`n", "`n")
    $finds = @($case.Find | ForEach-Object { $_.Replace("`r`n", "`n") })
    $replacements = @($case.Replace | ForEach-Object { $_.Replace("`r`n", "`n") })
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
