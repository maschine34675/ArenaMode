$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$binder = Join-Path $root 'ArenaMode.Client/Compat/ArenaMapPortScoreMonitors.cs'
$mapper = Join-Path $root 'ArenaMode.Client/Modes/TeamDeathmatchMonitors.cs'
$runtime = Join-Path $root 'ArenaMode.Client/Modes/TeamDeathmatchMode.Monitors.cs'
$faults = @(
    @{ Name='convertible-inexact-contract'; Path=$binder; Property='MonitorBinderSource';
       Find='if (parameters[index].ParameterType != args[index]) return null;'; Replace='if (false) return null;';
       Failure='contravariant object claim is not the exact string contract' },
    @{ Name='retained-claim-on-refusal'; Path=$binder; Property='MonitorBinderSource';
       Find='if (!accepted) { Release(); return false; }'; Replace='if (!accepted) { return false; }';
       Failure='clock failure releases retained handle' },
    @{ Name='unchanged-frame-misses-lost-claim'; Path=$binder; Property='MonitorBinderSource';
       Find='bool accepted = _clock(_handle, frame.ClockMode, frame.ClockSeconds);';
       Replace='bool accepted = _sent != null || _clock(_handle, frame.ClockMode, frame.ClockSeconds);';
       Failure='unchanged frame only validates handle through idempotent clock' },
    @{ Name='reversed-faction-colors'; Path=$mapper; Property='MonitorMapperSource';
       Find='Frame(ArenaTeamPalette.BearRgb, ArenaTeamPalette.UsecRgb, teamSize,';
       Replace='Frame(ArenaTeamPalette.UsecRgb, ArenaTeamPalette.BearRgb, teamSize,';
       Failure='faction colors fixed' },
    @{ Name='relative-team-score-on-faction-board'; Path=$mapper; Property='MonitorMapperSource';
       Find='localUsec ? opponentKills : friendlyKills, localUsec ? friendlyKills : opponentKills,';
       Replace='friendlyKills, opponentKills,'; Failure='faction scores True' },
    @{ Name='wrong-live-deadline'; Path=$mapper; Property='MonitorMapperSource';
       Find='startedAt + timeLimitSeconds : startedAt'; Replace='timeLimitSeconds : startedAt';
       Failure='clock Live limit 600' },
    @{ Name='repeated-scoreboard-scan'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='else if (_monitorScoreVersion != _match.Scoreboard.Version)'; Replace='else if (true)';
       Failure='unchanged scoreboard avoids repeated scans' },
    @{ Name='stale-physical-body-counted'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='&& ReferenceEquals(_world.GetAlivePlayerByProfileID(bot.ProfileId), player)'; Replace='&& true';
       Failure='alive contains only current active bodies and local slot' },
    @{ Name='shared-owner-across-matches'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='"AM TDM " + Guid.NewGuid().ToString("N")'; Replace='"ArenaMode TDM"';
       Failure='replacement cannot share previous match handle' },
    @{ Name='native-stop-presentation-leak'; Path=$runtime; Property='MonitorRuntimeSource';
       Find="internal void OnNativePresentationStop()`n    {`n        ReleaseMonitors();`n        ReleaseDomination();`n    }";
       Replace='internal void OnNativePresentationStop() { ReleaseDomination(); }';
       Failure='native service early return releases once without a mode tick' },
    @{ Name='domination-requires-kill-policy'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='&& MatchRulesReady && _population != null'; Replace='&& _rules != null && _population != null';
       Failure='DOM claims using objective rules without a TDM policy' },
    @{ Name='objective-income-cached-by-kills'; Path=$runtime; Property='MonitorRuntimeSource';
       Find="else if (IsDomination)`n            {";
       Replace="else if (IsDomination && _monitorScoreVersion != _match.Scoreboard.Version)`n            {`n                _monitorScoreVersion = _match.Scoreboard.Version;";
       Failure='DOM objective income updates without a kill scoreboard version change' },
    @{ Name='objective-clock-starts-at-zero'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='IsDomination ? _dominationRules.StartedAt : _rules.StartedAt';
       Replace='IsDomination ? 0f : _rules.StartedAt';
       Failure='DOM live timer starts at actual delayed gate release' },
    @{ Name='objective-score-uses-friendly-as-bear'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='bool localUsec = _population.LocalUsec;'; Replace='bool localUsec = !IsDomination && _population.LocalUsec;';
       Failure='DOM faction colors and living roster stay faction-correct' },
    @{ Name='final-scores-continue-to-use-scoreboard'; Path=$runtime; Property='MonitorRuntimeSource';
       Find='_monitorFriendlyScore = FinalFriendlyScore;'; Replace='_monitorFriendlyScore = FriendlyTeamScore;';
       Failure='result survives full 15 second hold with frozen scores/time' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'TdmMonitorChecks.csproj') -Token 'TDMMONITORCHECKS_PASSED' -Properties @() -Faults $faults
