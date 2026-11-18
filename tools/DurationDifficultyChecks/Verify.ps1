$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clock=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyClock.cs'
$demand=Join-Path $repo 'ArenaMode.Client/Bots/ArenaBotDifficultyClockPatch.cs'
$difficulty=Join-Path $repo 'ArenaMode.Client/Bots/ArenaDifficulty.cs'
$policy=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyPolicy.cs'
$faults=@(
    @{Name='time-leaks-into-fixed-mode';Path=$difficulty;Property='DifficultySource';Find=@('public static bool IsGunGame => ArenaDifficultyPolicy.ScalesWithTime(ArenaSession.ActiveMode);','ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp)');Replace=@('public static bool IsGunGame => true;','ArenaDifficultyPolicy.GunGameProgress(match.Elapsed, ramp)');Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='streak-leaks-into-fixed-mode';Path=$difficulty;Property='DifficultySource';Find='                return 1f;';Replace='                return (ArenaMatchService.Current?.Streak ?? 0) >= 8 ? 0f : 1f;';Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='streak-leaks-into-gun-game';Path=$difficulty;Property='DifficultySource';Find='return (float)ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp);';Replace='return Math.Min(1f, (float)ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp) + match.Streak / 8f);';Failure='kill streak never feeds the Gun Game curve'},
    @{Name='reduced-maps-to-maximum-row';Path=$difficulty;Property='DifficultySource';Find='=> ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Replace='=> ArenaDifficultyStep.Maximum;';Failure='fixed mode ignores elapsed time and streak: PMC level'},
    @{Name='live-setting-instead-of-frozen';Path=$difficulty;Property='DifficultySource';Find='=> ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Replace='=> Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;';Failure='frozen step wins over a live setting change'},
    @{Name='pmc-floor-zero-gets-bonus';Path=$difficulty;Property='DifficultySource';Find='configured <= 0 ? 0 :';Replace='false ? 0 :';Failure='PmcFloor zero stays off'},
    @{Name='bonus-ignores-step';Path=$difficulty;Property='DifficultySource';Find='ArenaDifficultyPolicy.PmcBonusAt(Step, Progress)';Replace='ArenaDifficultyPolicy.PmcBonusAt(ArenaDifficultyStep.Maximum, Progress)';Failure='fixed mode ignores elapsed time and streak: PMC floor'},
    @{Name='cooldown-ignores-step';Path=$difficulty;Property='DifficultySource';Find='ArenaDifficultyPolicy.LegacyCooldownFactor(Step)';Replace='ArenaDifficultyPolicy.LegacyCooldownFactor(ArenaDifficultyStep.Maximum)';Failure='refill cooldown factor follows the step'},
    @{Name='map-ramp-ignored';Path=$difficulty;Property='DifficultySource';Find='ArenaClientConfig.GetBotTierRampMinutes(map, FallbackRampMinutes)';Replace='FallbackRampMinutes';Failure='Gun Game uses the map''s ramp length'},
    @{Name='gun-game-pmc-step-early';Path=$policy;Property='PolicySource';Find='GunGamePmcStepProgress = 0.8;';Replace='GunGamePmcStepProgress = 0.5;';Failure='Gun Game PMC level follows the curve'},
    @{Name='gun-game-scav-step-late';Path=$policy;Property='PolicySource';Find='GunGameScavStepProgress = 0.5;';Replace='GunGameScavStepProgress = 0.8;';Failure='Gun Game scav level follows the curve'},
    @{Name='full-duration';Path=$clock;Property='ClockSource';Find='timeLimitSeconds * 0.8d';Replace='timeLimitSeconds * 1d';Failure='finite wire clock peaks at eighty percent'},
    @{Name='early-cache';Path=$demand;Property='DemandSource';Find='profile.Info.Settings.Role == role && profile.Info.Settings.BotDifficulty == difficulty';Replace='false';Failure='only unused matching cache references removed'},
    @{Name='prefetch';Path=$demand;Property='DemandSource';Find=': base(1, original.Role, original.Difficulty)';Replace=': base(original.Limit, original.Role, original.Difficulty)';Failure='owned demand requests one fresh profile without mutating native DTO'},
    @{Name='backup';Path=$demand;Property='DemandSource';Find='!TryCurrent(provider, out _, data)';Replace='true';Failure='finite PMC backup suppressed'},
    @{Name='provider-scope';Path=$demand;Property='DemandSource';Find='_request = null; // A nested foreign/stale demand must shadow its caller.';Replace='// omitted';Failure='stale nested provider cannot borrow current scope'},
    @{Name='arm-check';Path=$demand;Property='DemandSource';Find='currentClock.ArmToken != request.Clock.ArmToken';Replace='false';Failure='old arm cannot be adopted by new match'},
    @{Name='scope-lifetime';Path=$demand;Property='DemandSource';Find='_request = _previous;';Replace='// omitted';Failure='outer scope restored after nested call'},
    @{Name='partial-hooks';Path=$demand;Property='DemandSource';Find='_ready = false;';Replace='_ready = true;';Failure='partial hook installation cannot mutate native generation'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'DurationDifficultyChecks.csproj') 'ARENA_DURATION_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_DURATION_DIFFICULTY_CHECKS_PASSED'
