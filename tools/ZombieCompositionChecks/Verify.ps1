$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'ZombieCompositionChecks.csproj'
$faults=@(
    @{Name='opening-slow-share-lost'; File='Modes/ZombieCompositionPolicy.cs'; Property='CompositionSource'; Find='return 75;'; Replace='return 50;'; Failure='pre-wave sentinel stays in opening composition'},
    @{Name='cycle-flattened'; File='Modes/ZombieCompositionPolicy.cs'; Property='CompositionSource'; Find='1 => 20'; Replace='1 => 40'; Failure='slow curve advances through opening and repeat cycle'},
    @{Name='role-weights-retuned'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find=@('(InfectedAssault, 50)','(InfectedPmc, 25)'); Replace=@('(InfectedAssault, 25)','(InfectedPmc, 50)'); Failure='four generic role weights remain 50 25 15 10'},
    @{Name='shooter-roll-off-by-one'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='rng.Next(100) < Math.Clamp(shooterPercent, 0, 100)'; Replace='rng.Next(100) <= Math.Clamp(shooterPercent, 0, 100)'; Failure='shooter chance remains independent and exact at zero and hundred'},
    @{Name='draw-ignores-wave'; File='Modes/ZombieRoleMix.cs'; Property='RoleSource'; Find='ZombieCompositionPolicy.SlowPercent(waveNumber)'; Replace='ZombieCompositionPolicy.SlowPercent(1)'; Failure='melee bucket split follows each wave independently of shooter chance'},
    @{Name='opening-warms-wrong-wave'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='ZombieCompositionPolicy.SlowPercent(1)'; Replace='ZombieCompositionPolicy.SlowPercent(3)'; Failure='opening preload reflects actual first wave slow share'},
    @{Name='backup-omits-slow'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='entries.Add(new Entry(role, ZombieRoleMix.Easy, count));'; Replace=';'; Failure='backup contains only every possible future pair including shooter extremes'},
    @{Name='shooters-only-warms-melee'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='if (shooterPercent < 100)'; Replace='if (true)'; Failure='backup contains only every possible future pair including shooter extremes'},
    @{Name='backup-floor-lost'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='Math.Clamp(countPerPair, MinimumBackupCount, MaxBackupCountPerPair)'; Replace='Math.Clamp(countPerPair, 0, MaxBackupCountPerPair)'; Failure='backup preserves native floor and per pair ceiling'},
    @{Name='backup-ceiling-lost'; File='Modes/ZombieProfilePlan.cs'; Property='ProfileSource'; Find='Math.Clamp(countPerPair, MinimumBackupCount, MaxBackupCountPerPair)'; Replace='Math.Max(countPerPair, MinimumBackupCount)'; Failure='backup preserves native floor and per pair ceiling'},
    @{Name='slow-mapped-as-fast'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Find='value = BotDifficulty.easy;'; Replace='value = BotDifficulty.normal;'; Failure='easy maps to native slow difficulty'}
)
foreach($fault in $faults) { $fault.Path=Join-Path $repo ('ArenaMode.Client/'+$fault.File) }
Test-ArenaSourceFaults $project 'ZOMBIE_COMPOSITION_CHECKS_PASSED' @() $faults -RequireSingleSite

$modePath=Join-Path $repo 'ArenaMode.Client/Modes/ZombieMode.cs'
$modeFaults=@(
    @{Name='actual-wave-not-forwarded'; Path=$modePath; Property='ModeSource';
      Find='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber)'; Replace='ZombieRoleMix.Next(_rng, _shooterPercent, 1)';
      Failure='draw follows actual second wave instead of opening composition'},
    @{Name='shooter-config-read-live'; Path=$modePath; Property='ModeSource';
      Find='ZombieRoleMix.Next(_rng, _shooterPercent, _waveState.WaveNumber)'; Replace='ZombieRoleMix.Next(_rng, Settings.ZombieShooterPercent.Value, _waveState.WaveNumber)';
      Failure='later draw retains frozen shooter percentage after live config edit'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot '../ZombieWavePendingChecks/ZombieWavePendingChecks.csproj') 'ZOMBIE_WAVE_PENDING_CHECKS_PASSED' @() $modeFaults -FailurePrefix '' -RequireSingleSite
& (Join-Path $PSScriptRoot '../TeamDeathmatchRuntimeChecks/BurstIntegration/Run.ps1') -CompositionOnly
Write-Output 'ZOMBIE_COMPOSITION_VERIFIED'
