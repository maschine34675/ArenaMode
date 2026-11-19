$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'ZombieCompositionChecks.csproj'
$faults=@(
    @{Name='opening-slow-share-lost'; File='Modes/ZombieCompositionPolicy.cs'; Property='CompositionSource'; Find='return 75;'; Replace='return 50;'; Failure='pre-wave sentinel stays in opening composition'},
    @{Name='cycle-flattened'; File='Modes/ZombieCompositionPolicy.cs'; Property='CompositionSource'; Find='1 => 20'; Replace='1 => 40'; Failure='slow curve advances through opening and repeat cycle'},
    @{Name='role-weights-retuned'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find=@('(InfectedAssault, 50)','(InfectedPmc, 25)'); Replace=@('(InfectedAssault, 25)','(InfectedPmc, 50)'); Failure='four generic role weights remain 50 25 15 10'},
    @{Name='shooter-roll-off-by-one'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='rng.Next(100) < ArenaDifficultyPolicy.ZombieShooterPercent(step, shooterPercent, waveNumber)'; Replace='rng.Next(100) <= ArenaDifficultyPolicy.ZombieShooterPercent(step, shooterPercent, waveNumber)'; Failure='shooter chance remains independent and exact at zero and hundred'},
    @{Name='draw-ignores-wave'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='ZombieCompositionPolicy.SlowPercent(waveNumber, step)'; Replace='ZombieCompositionPolicy.SlowPercent(1, step)'; Failure='melee bucket split follows each wave independently of shooter chance'},
    @{Name='opening-warms-wrong-wave'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='ZombieCompositionPolicy.SlowPercent(1)'; Replace='ZombieCompositionPolicy.SlowPercent(3)'; Failure='opening preload reflects actual first wave slow share'},
    @{Name='backup-omits-slow'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='entries.Add(new Entry(role, ZombieRoleMix.Easy, count));'; Replace=';'; Failure='backup contains only every possible future pair including shooter extremes'},
    @{Name='shooters-only-warms-melee'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='if (shooterPercent < 100)'; Replace='if (true)'; Failure='backup contains only every possible future pair including shooter extremes'},
    @{Name='backup-floor-lost'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='Math.Clamp(countPerPair, MinimumBackupCount, MaxBackupCountPerPair)'; Replace='Math.Clamp(countPerPair, 0, MaxBackupCountPerPair)'; Failure='backup preserves native floor and per pair ceiling'},
    @{Name='backup-ceiling-lost'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='Math.Clamp(countPerPair, MinimumBackupCount, MaxBackupCountPerPair)'; Replace='Math.Max(countPerPair, MinimumBackupCount)'; Failure='backup preserves native floor and per pair ceiling'},
    @{Name='slow-mapped-as-fast'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Find='value = BotDifficulty.easy;'; Replace='value = BotDifficulty.normal;'; Failure='easy maps to native slow difficulty'}
)
$policyPath=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyPolicy.cs'
$faults+=@(
    @{Name='fade-lost'; Path=$policyPath; Property='PolicySource'; Find='return (int)Math.Max(0L, slow - fade);'; Replace='return slow;'; Failure='Maximum fades the slow share from wave four'},
    @{Name='fade-starts-late'; Path=$policyPath; Property='PolicySource'; Find='ZombieSlowFadePointsPerWave * (waveNumber - (ZombieSlowFadeFromWave - 1))'; Replace='ZombieSlowFadePointsPerWave * (waveNumber - ZombieSlowFadeFromWave)'; Failure='Maximum fades the slow share from wave four'},
    @{Name='fade-applies-at-reduced'; Path=$policyPath; Property='PolicySource'; Find='if (step == ArenaDifficultyStep.Reduced || waveNumber < ZombieSlowFadeFromWave)'; Replace='if (waveNumber < ZombieSlowFadeFromWave)'; Failure='melee bucket split follows each wave'},
    @{Name='shooter-growth-flat'; Path=$policyPath; Property='PolicySource'; Find='if (step == ArenaDifficultyStep.Reduced || waveNumber <= 1 || share == 0 || share == 100)'; Replace='if (true)'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='shooter-growth-at-reduced'; Path=$policyPath; Property='PolicySource'; Find='if (step == ArenaDifficultyStep.Reduced || waveNumber <= 1 || share == 0 || share == 100)'; Replace='if (waveNumber <= 1 || share == 0 || share == 100)'; Failure='shooter chance remains independent and exact at zero and hundred'},
    @{Name='shooter-growth-rate-doubled'; Path=$policyPath; Property='PolicySource'; Find='ZombieShooterGrowthPerWave = 0.1;'; Replace='ZombieShooterGrowthPerWave = 0.2;'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='shooter-growth-uncapped'; Path=$policyPath; Property='PolicySource'; Find='Math.Min(ZombieShooterGrowthCap - 1d, ZombieShooterGrowthPerWave * ((long)waveNumber - 1))'; Replace='ZombieShooterGrowthPerWave * ((long)waveNumber - 1)'; Failure='Maximum shooter growth stops at twice the setting and at 100 percent'},
    @{Name='shooter-100-clamp-lost'; Path=$policyPath; Property='PolicySource'; Find='Math.Clamp(grown, share, 100)'; Replace='Math.Max(grown, share)'; Failure='Maximum shooter growth stops at twice the setting and at 100 percent'},
    @{Name='reduced-cap-lost'; Path=$policyPath; Property='PolicySource'; Find='Math.Min(configured, ZombieMaxAliveReduced)'; Replace='configured'; Failure='Reduced caps the alive limit at 24 and Maximum never lowers it'},
    @{Name='maximum-cap-applied'; Path=$policyPath; Property='PolicySource'; Find='step == ArenaDifficultyStep.Reduced ? Math.Min(configured, ZombieMaxAliveReduced)'; Replace='true ? Math.Min(configured, ZombieMaxAliveReduced)'; Failure='Reduced caps the alive limit at 24 and Maximum never lowers it'},
    @{Name='step-ignored-by-draw'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='ZombieCompositionPolicy.SlowPercent(waveNumber, step)'; Replace='ZombieCompositionPolicy.SlowPercent(waveNumber)'; Failure='Maximum draws the faded slow share of the wave'},
    @{Name='growth-ignored-by-draw'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='ArenaDifficultyPolicy.ZombieShooterPercent(step, shooterPercent, waveNumber)'; Replace='Math.Clamp(shooterPercent, 0, 100)'; Failure='Maximum draws the grown shooter share of the wave'}
)
# Special waves (docs/reviews/ZOMBIE-ESCALATION-20261119.md): the calendar, the two share functions and the decider.
$faults+=@(
    @{Name='special-wave-interval-4'; Path=$policyPath; Property='PolicySource'; Find='waveNumber % ZombieSpecialWaveInterval != 0'; Replace='waveNumber % 4 != 0'; Failure='special waves fall on every fifth wave from wave five, Runners first'},
    @{Name='special-wave-at-zero'; Path=$policyPath; Property='PolicySource'; Find='waveNumber < ZombieSpecialWaveInterval'; Replace='waveNumber < 0'; Failure='special waves fall on every fifth wave from wave five, Runners first'},
    @{Name='special-waves-at-reduced'; Path=$policyPath; Property='PolicySource'; Find='step != ArenaDifficultyStep.Maximum || waveNumber < ZombieSpecialWaveInterval'; Replace='waveNumber < ZombieSpecialWaveInterval'; Failure='Reduced has no special waves'},
    @{Name='theme-order-swapped'; Path=$policyPath; Property='PolicySource'; Find='(waveNumber / ZombieSpecialWaveInterval) % 2 == 1'; Replace='(waveNumber / ZombieSpecialWaveInterval) % 2 == 0'; Failure='special waves fall on every fifth wave from wave five, Runners first'},
    @{Name='runners-keep-slow'; Path=$policyPath; Property='PolicySource'; Find='ZombieScheduledTheme(step, waveNumber) == ZombieWaveTheme.Runners'; Replace='false'; Failure='Maximum fades the slow share from wave four'},
    @{Name='runners-keep-shooters'; Path=$policyPath; Property='PolicySource'; Find='case ZombieWaveTheme.Runners: return 0;'; Replace='case ZombieWaveTheme.Runners: return share;'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='shooters-wave-not-all-hard'; Path=$policyPath; Property='PolicySource'; Find='case ZombieWaveTheme.Shooters: return 100;'; Replace='case ZombieWaveTheme.Shooters: return share;'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='shooters-wave-at-zero'; Path=$policyPath; Property='PolicySource'; Find='waveNumber <= 1 || share == 0 || share == 100)'; Replace='waveNumber <= 1 || share == 100)'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='runners-at-hundred'; Path=$policyPath; Property='PolicySource'; Find='waveNumber <= 1 || share == 0 || share == 100)'; Replace='waveNumber <= 1 || share == 0)'; Failure='Maximum grows the shooter share by a tenth of the setting per wave'},
    @{Name='theme-keeps-runners-at-hundred'; Path=$policyPath; Property='PolicySource'; Find='theme == ZombieWaveTheme.Runners && share == 100'; Replace='false'; Failure='a shooter share of 100 never gets a runners wave'},
    @{Name='theme-keeps-shooters-at-zero'; Path=$policyPath; Property='PolicySource'; Find='theme == ZombieWaveTheme.Shooters && share == 0'; Replace='false'; Failure='a shooter share of 0 never gets a shooters wave'},
    # Faults that are right at waves 5 and 10 and wrong later: only the waves 1 to 60 cross-check sees them. (The Runners return in
    # ZombieSlowPercent has no such fault: the faded slow share is already 0 from wave 11, so it cannot differ at a later wave.)
    @{Name='runners-shooter-override-ends-early'; Path=$policyPath; Property='PolicySource'; Find='case ZombieWaveTheme.Runners: return 0;'; Replace='case ZombieWaveTheme.Runners: if (waveNumber < 12) return 0; break;'; Failure='a runners wave draws neither shooters nor slow infected'},
    @{Name='shooters-override-ends-early'; Path=$policyPath; Property='PolicySource'; Find='case ZombieWaveTheme.Shooters: return 100;'; Replace='case ZombieWaveTheme.Shooters: if (waveNumber < 12) return 100; break;'; Failure='a shooters wave draws only shooters'}
)
foreach($fault in $faults) { if(-not $fault.Path){ $fault.Path=Join-Path $repo ('ArenaMode.Client/'+$fault.File) } }
Test-ArenaSourceFaults $project 'ZOMBIE_COMPOSITION_CHECKS_PASSED' @() $faults -RequireSingleSite

$modePath=Join-Path $repo 'ArenaMode.Client/Modes/ZombieMode.cs'
$modeFaults=@(
    @{Name='actual-wave-not-forwarded'; Path=$modePath; Property='ModeSource';
      Find='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber, _step)'; Replace='ZombieRoleMix.Next(_rng, _shooterPercent, 1, _step)';
      Failure='draw follows actual second wave instead of opening composition'},
    @{Name='shooter-config-read-live'; Path=$modePath; Property='ModeSource';
      Find='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber, _step)'; Replace='ZombieRoleMix.Next(_rng, Settings.ZombieShooterPercent.Value, _waveState.WaveNumber, _step)';
      Failure='later draw retains frozen shooter percentage after live config edit'},
    @{Name='step-read-live-per-draw'; Path=$modePath; Property='ModeSource';
      Find='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber, _step)'; Replace='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber, ArenaDifficulty.Step)';
      Failure='draws use the step frozen at match start, not the live value'},
    @{Name='step-not-frozen-at-start'; Path=$modePath; Property='ModeSource';
      Find='_step = ArenaDifficulty.Step;'; Replace='_step = ArenaMode.Shared.ArenaDifficultyStep.Maximum;';
      Failure='the alive limit follows the frozen step'},
    @{Name='alive-limit-ignores-step'; Path=$modePath; Property='ModeSource';
      Find='ArenaDifficultyPolicy.ZombieMaxAlive(_step, Settings.ZombieMaxAlive.Value)'; Replace='Settings.ZombieMaxAlive.Value';
      Failure='the alive limit follows the frozen step'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot '../ZombieWavePendingChecks/ZombieWavePendingChecks.csproj') 'ZOMBIE_WAVE_PENDING_CHECKS_PASSED' @() $modeFaults -FailurePrefix '' -RequireSingleSite
& (Join-Path $PSScriptRoot '../TeamDeathmatchRuntimeChecks/BurstIntegration/Run.ps1') -CompositionOnly
Write-Output 'ZOMBIE_COMPOSITION_VERIFIED'
