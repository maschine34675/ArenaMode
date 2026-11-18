$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$burst=Join-Path $repo 'ArenaMode.Client/Bots/BurstSpawnService.cs'
$difficulty=Join-Path $repo 'ArenaMode.Client/Bots/ArenaDifficulty.cs'
$policy=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyPolicy.cs'
$session=Join-Path $repo 'ArenaMode.Client/Session/ArenaSession.cs'
$clientConfig=Join-Path $repo 'ArenaMode.Client/Session/ArenaClientConfig.cs'
$hostility=Join-Path $repo 'ArenaMode.Client/Bots/ArenaHostility.cs'
$movement=Join-Path $repo 'ArenaMode.Client/Movement/ArenaBotMovement.cs'
$tdm=Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$solo=Join-Path $repo 'ArenaMode.Client/Bots/ArenaSoloPopulation.cs'
$faults=@(
    @{Name='time-leaks-into-fixed-mode';Path=$difficulty;Property='DifficultySource';Find=@('public static bool IsGunGame => ArenaDifficultyPolicy.ScalesWithTime(ArenaSession.ActiveMode);','ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp)');Replace=@('public static bool IsGunGame => true;','ArenaDifficultyPolicy.GunGameProgress(match.Elapsed, ramp)');Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='streak-leaks-into-fixed-mode';Path=$difficulty;Property='DifficultySource';Find='                return 1f;';Replace='                return (ArenaMatchService.Current?.Streak ?? 0) >= 8 ? 0f : 1f;';Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='streak-leaks-into-gun-game';Path=$difficulty;Property='DifficultySource';Find='return (float)ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp);';Replace='return Math.Min(1f, (float)ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp) + match.Streak / 8f);';Failure='kill streak never feeds the Gun Game curve'},
    @{Name='reduced-maps-to-maximum-row';Path=$difficulty;Property='DifficultySource';Find='=> ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Replace='=> ArenaDifficultyStep.Maximum;';Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='live-setting-instead-of-frozen';Path=$difficulty;Property='DifficultySource';Find='=> ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Replace='=> Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Failure='frozen step wins over a live setting change'},
    @{Name='session-difficulty-not-frozen';Path=$session;Property='SessionText';Find='Difficulty = difficulty;';Replace='// omitted';Failure='MarkArmed freezes the acknowledged difficulty step after clearing the old raid'},
    @{Name='session-difficulty-not-cleared';Path=$session;Property='SessionText';Find='Difficulty = null;';Replace='// omitted';Failure='ClearParticipants resets the frozen difficulty step'},
    @{Name='ramp-key-misspelled';Path=$clientConfig;Property='ClientConfigText';Find='"botTierRampMinutes"';Replace='"botTierRamp"';Failure='client and server read the same botTierRampMinutes key'},
    @{Name='ramp-nonpositive-unclamped';Path=$clientConfig;Property='ClientConfigText';Find='value.HasValue ? Math.Max(1, value.Value) : fallback';Replace='value.HasValue && value.Value > 0 ? value.Value : fallback';Failure='a present non-positive ramp length clamps to one minute like the server'},
    @{Name='sweep-ignores-step';Path=$hostility;Property='HostilityText';Find='ArenaDifficultyPolicy.ReportStep(ArenaSession.ActiveMode, ArenaDifficulty.Step));';Replace='ArenaDifficultyStep.Maximum);';Failure='the HuntPlayer sweep interval comes from the policy with the frozen step'},
    @{Name='sweep-factor-dropped';Path=$policy;Property='PolicySource';Find='return interval * ReportIntervalFactor(step);';Replace='return interval;';Failure='sweep interval takes the step factor last'},
    @{Name='sweep-factor-before-halving';Path=$policy;Property='PolicySource';Find=@('double interval = tactical ? Math.Max(2d, mapSeconds / 2d) : mapSeconds;','return interval * ReportIntervalFactor(step);');Replace=@('double interval = tactical ? Math.Max(2d, mapSeconds * ReportIntervalFactor(step) / 2d) : mapSeconds * ReportIntervalFactor(step);','return interval;');Failure='sweep interval takes the step factor last'},
    @{Name='speed-ignores-step';Path=$movement;Property='MovementText';Find='BotSpeedFactor(requested, ArenaDifficulty.Step)';Replace='BotSpeedFactor(requested, ArenaDifficultyStep.Maximum)';Failure='bot speed comes from the policy with the frozen step'},
    @{Name='speed-cap-removed';Path=$policy;Property='PolicySource';Find='Math.Min(requested, BotSpeedUserCap)';Replace='requested';Failure='bot speed caps the setting before scaling the surplus'},
    @{Name='pmc-floor-zero-gets-bonus';Path=$difficulty;Property='DifficultySource';Find='configured <= 0 ? 0 :';Replace='false ? 0 :';Failure='PmcFloor zero stays off'},
    @{Name='bonus-ignores-step';Path=$difficulty;Property='DifficultySource';Find='ArenaDifficultyPolicy.PmcBonusAt(Step, Progress)';Replace='ArenaDifficultyPolicy.PmcBonusAt(ArenaDifficultyStep.Maximum, Progress)';Failure='fixed mode ignores elapsed time and streak: PMC floor'},
    @{Name='cooldown-ignores-step';Path=$difficulty;Property='DifficultySource';Find='ArenaDifficultyPolicy.LegacyCooldownFactor(Step)';Replace='ArenaDifficultyPolicy.LegacyCooldownFactor(ArenaDifficultyStep.Maximum)';Failure='refill cooldown factor follows the step'},
    @{Name='map-ramp-ignored';Path=$difficulty;Property='DifficultySource';Find='ArenaClientConfig.GetBotTierRampMinutes(map, FallbackRampMinutes)';Replace='FallbackRampMinutes';Failure='Gun Game uses the map''s ramp length'},
    @{Name='prewarm-fixed-mode-warms-both';Path=$policy;Property='PolicySource';Find='        return new[] { end };';Replace='        return new[] { GunGamePmcOpening, end };';Failure='prewarm deathmatch Maximum warms only impossible'},
    @{Name='prewarm-gun-game-warms-end-level';Path=$policy;Property='PolicySource';Find='return new[] { GunGamePmcOpening };';Replace='return PmcRequestLevels(mode, step);';Failure='prewarm Gun Game warms only the opening level'},
    @{Name='prewarm-ignores-session-gate';Path=$policy;Property='PolicySource';Find='        if (!active)';Replace='        if (false)';Failure='prewarm does nothing outside an active session'},
    @{Name='prewarm-tactical-scav-squad-warmed';Path=$policy;Property='PolicySource';Find='&& !tacticalSquadIsPmc)';Replace='&& false)';Failure='prewarm warms no PMC for a Tactical squad of scavs'},
    @{Name='prewarm-squad-setting-gates-all-modes';Path=$policy;Property='PolicySource';Find='string.Equals(mode, TacticalModeId, StringComparison.OrdinalIgnoreCase) && !tacticalSquadIsPmc';Replace='!tacticalSquadIsPmc';Failure='the squad setting only matters in Tactical'},
    @{Name='prewarm-call-site-drops-session-gate';Path=$burst;Property='BurstText';Find='ArenaSession.IsActive, ArenaSession.ActiveMode, Settings.TacticalSquadIsPmc.Value';Replace='true, ArenaSession.ActiveMode, Settings.TacticalSquadIsPmc.Value';Failure='prewarm asks the policy with the session gate'},
    @{Name='tdm-request-literal-level';Path=$tdm;Property='TdmText';Find='SpawnFactionBot(role, ArenaDifficulty.DifficultyFor(true),';Replace='SpawnFactionBot(role, BotDifficulty.hard,';Failure='Team Deathmatch requests take their level from ArenaDifficulty'},
    @{Name='solo-request-literal-level';Path=$solo;Property='SoloText';Find='ArenaDifficulty.DifficultyFor(pmc)';Replace='BotDifficulty.hard';Failure='solo population takes its level from ArenaDifficulty'},
    @{Name='prewarm-gun-game-misses-opening';Path=$policy;Property='PolicySource';Find='        if (ScalesWithTime(mode) && GunGamePmcOpening != end)';Replace='        if (false)';Failure='Gun Game Maximum can request hard then impossible'},
    @{Name='prewarm-zombie-warmed';Path=$policy;Property='PolicySource';Find='        if (string.Equals(mode, ZombieModeId, StringComparison.OrdinalIgnoreCase))';Replace='        if (false)';Failure='prewarm Zombie warms no PMC level'},
    @{Name='prewarm-literal-pair-list';Path=$burst;Property='BurstText';Find='foreach (var level in levels)';Replace='foreach (var level in new[] { Shared.ArenaBotLevel.Hard, Shared.ArenaBotLevel.Impossible })';Failure='prewarm walks the policy''s level list'},
    @{Name='prewarm-clock-gate-back';Path=$burst;Property='BurstText';Find='if (levels.Length == 0)';Replace='if (levels.Length == 0 || DifficultyClockReady.IsReady)';Failure='prewarm has no clock readiness gate'},
    @{Name='prewarm-drops-bear';Path=$burst;Property='BurstText';Find='spawner.AddToTargetBackup(difficulty, WildSpawnType.pmcBEAR, 6);';Replace='';Failure='prewarm covers both PMC sides'},
    @{Name='gun-game-pmc-step-early';Path=$policy;Property='PolicySource';Find='GunGamePmcStepProgress = 0.8;';Replace='GunGamePmcStepProgress = 0.5;';Failure='Gun Game PMC level follows the curve'},
    @{Name='gun-game-scav-step-late';Path=$policy;Property='PolicySource';Find='GunGameScavStepProgress = 0.5;';Replace='GunGameScavStepProgress = 0.8;';Failure='Gun Game scav level follows the curve'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'DurationDifficultyChecks.csproj') 'ARENA_DURATION_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_DURATION_DIFFICULTY_CHECKS_PASSED'
