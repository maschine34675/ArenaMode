$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clock=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyClock.cs'
$demand=Join-Path $repo 'ArenaMode.Client/Bots/ArenaBotDifficultyClockPatch.cs'
$pressure=Join-Path $repo 'ArenaMode.Client/Bots/ArenaPressure.cs'
$faults=@(
    @{Name='full-duration';Path=$clock;Property='ClockSource';Find='timeLimitSeconds * 0.8d';Replace='timeLimitSeconds * 1d';Failure='difficulty follows frozen match proportions'},
    @{Name='wall-time';Path=$pressure;Property='PressureSource';Find='ArenaDifficultyClock.Progress(elapsed, duration, rampSeconds)';Replace='ArenaDifficultyClock.Progress(match.Elapsed, duration, rampSeconds)';Failure='difficulty follows frozen match proportions'},
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
