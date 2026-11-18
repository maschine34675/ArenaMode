$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
$runtime = Join-Path $repo 'ArenaMode.Client/Bots/ZombieRecoveryRuntime.cs'
$wave = Join-Path $repo 'ArenaMode.Client/Modes/ZombieWavePolicy.cs'
$service = Join-Path $repo 'ArenaMode.Client/Bots/ZombieSpawnService.cs'
$faults = @(
    @{ Name='missing-arrival-ownership'; Path=$service; Property='ServiceSource'; Find='ZombieRecoveryRuntime.Track(bot);'; Replace=''; Failure='confirmed owned arrival is retained' },
    @{ Name='no-stationary-observation'; Path=$runtime; Property='RecoverySource'; Find='internal const float StationarySeconds = 20f;'; Replace='internal const float StationarySeconds = 0f;'; Failure='immobility alone cannot retire before repeated fresh route proof' },
    @{ Name='one-route-is-not-proof'; Path=$runtime; Property='RecoverySource'; Find='probe.UnreachablePasses >= 2'; Replace='probe.UnreachablePasses >= 1'; Failure='immobility alone cannot retire before repeated fresh route proof' },
    @{ Name='no-renewed-intent-time'; Path=$runtime; Property='RecoverySource'; Find='now - probe.IntentSince >= RenewedIntentSeconds'; Replace='true'; Failure='complete route requires renewed sustained movement intent' },
    @{ Name='no-movement-input'; Path=$runtime; Property='RecoverySource'; Find='|| probe.Entry.Body.MovementContext.MovementDirection.sqrMagnitude < .01f'; Replace=''; Failure='stale moving flag door sequence or unrelated path is not sustained pursuit' },
    @{ Name='asleep-moving-flags'; Path=$runtime; Property='RecoverySource'; Find='standby.StandByType != BotStandByType.active'; Replace='false'; Failure='stale moving flag door sequence or unrelated path is not sustained pursuit' },
    @{ Name='visible-disappearance'; Path=$runtime; Property='RecoverySource'; Find='if (!Physics.Linecast(entry.Body.Position + Vector3.up * 1.5f'; Replace='if (false && !Physics.Linecast(entry.Body.Position + Vector3.up * 1.5f'; Failure='clear head sight alone prevents recovery' },
    @{ Name='near-disappearance'; Path=$runtime; Property='RecoverySource'; Find='|| (player.Position - entry.Body.Position).sqrMagnitude <= SafeDistanceSquared'; Replace=''; Failure='near survivor prevents recovery' },
    @{ Name='missing-current-body'; Path=$runtime; Property='RecoverySource'; Find='|| !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)'; Replace=''; Failure='foreign stale inactive follower terminal paused or rebinding state rejects recovery' },
    @{ Name='missing-terminal-admission'; Path=$runtime; Property='RecoverySource'; Find='&& mode.IsRecoveryAdmissionOpen && Time.timeScale > 0f'; Replace='&& Time.timeScale > 0f'; Failure='foreign stale inactive follower terminal paused or rebinding state rejects recovery' },
    @{ Name='failed-kill-returns-slot'; Path=$runtime; Property='RecoverySource'; Find='bool dead = health.IsAlive == false;'; Replace='bool dead = true;'; Failure='failed native death never returns quota' },
    @{ Name='teardown-forgets-confirmed-death'; Path=$runtime; Property='RecoverySource'; Find='bool dead = health.IsAlive == false;'; Replace='bool dead = candidate.Owner.GetPlayer != null && health.IsAlive == false;'; Failure='synchronous owner teardown still credits confirmed captured death' },
    @{ Name='probe-starves-next-body'; Path=$runtime; Property='RecoverySource'; Find='now - probe.StartedAt >= ProbeLifetimeSeconds'; Replace='false'; Failure='bounded no intent probe cannot starve another stuck body' },
    @{ Name='unbounded-wave-recycling'; Path=$runtime; Property='RecoverySource'; Find='bool budgetAvailable = _recovered < Math.Min(8, Math.Max(1, waveSize / 5));'; Replace='bool budgetAvailable = true;'; Failure='per wave replacement cap bounds recycling' },
    @{ Name='cap-retains-dead-bodies'; Path=$runtime; Property='RecoverySource'; Find='if (!budgetAvailable) _probe = null;'; Replace='if (!budgetAvailable) return 0;'; Failure='exhausted allowance still prunes dead owned bodies' },
    @{ Name='admin-death-is-false-progress'; Path=$wave; Property='WaveSource'; Find=' - state.RecoveredSinceLastCall'; Replace=''; Failure='administrative death is excluded from combat progress' },
    @{ Name='recovered-slot-lost'; Path=$wave; Property='WaveSource'; Find="state.SpawnedThisWave--;`n        state.RecoveredSinceLastCall++;"; Replace='state.RecoveredSinceLastCall++;'; Failure='confirmed recovery owes replacement without clearing pending tail' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ZombieRecoveryChecks.csproj') `
    -Token 'ZOMBIE_RECOVERY_CHECKS_PASSED' -Faults $faults -RequireSingleSite
Test-ArenaSourceFaults -Project (Join-Path $repo 'tools/ZombieWavePendingChecks/ZombieWavePendingChecks.csproj') `
    -Token 'ZOMBIE_WAVE_PENDING_CHECKS_PASSED' -FailurePrefix '' -RequireSingleSite -Faults @(
        @{ Name='mode-loses-recovered-slot'; Path=(Join-Path $repo 'ArenaMode.Client/Modes/ZombieMode.cs'); Property='ModeSource';
           Find='ZombieWavePolicy.ReportRecoveredSpawn(ref _waveState);'; Replace=';';
           Failure='confirmed individual retirement returns its slot before wave clearance' }
    )
Write-Output 'ZOMBIE_RECOVERY_VERIFIED'
