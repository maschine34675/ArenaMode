$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Session/ArenaExtractionIsolation.cs')).Path
$faults = @(
    @{ Name='secret-override-unprotected'; Path=$source; Property='ExtractionSource';
       Find='new[] { typeof(ExfiltrationPoint), typeof(SecretExfiltrationPoint) }'; Replace='new[] { typeof(ExfiltrationPoint) }';
       Failure='all extraction-specific hooks installed' },
    @{ Name='stale-active-world'; Path=$source; Property='ExtractionSource';
       Find='!ArenaSession.IsExactActiveRaid(game, world)'; Replace='false';
       Failure='scope excludes exact-game' },
    @{ Name='foreign-body-blocked'; Path=$source; Property='ExtractionSource';
       Find='!ReferenceEquals(player, local)'; Replace='false';
       Failure='scope excludes foreign-body' },
    @{ Name='stale-point-by-name'; Path=$source; Property='ExtractionSource';
       Find='ReferenceEquals(candidate, point)'; Replace='candidate.Settings.Name == point.Settings.Name';
       Failure='same-name stale trigger remains native' },
    @{ Name='completion-extraction-allowed'; Path=$source; Property='ExtractionSource';
       Find='return !TryCurrentRaid(out var game, out var world)';
       Replace='return true || !TryCurrentRaid(out var game, out var world)';
       Failure='shared/individual extraction-only completion blocked on factory4_day regular' },
    @{ Name='stopping-latch-used'; Path=$source; Property='ExtractionSource';
       Find='(game.Status != GameStatus.Started && game.Status != GameStatus.SoftStopping)';
       Replace='false'; Failure='scope excludes stopping' },
    @{ Name='stale-loading-token'; Path=$source; Property='ExtractionSource';
       Find='|| (!Protocol.IsNativeStartIdentity(settings.serverId, token)';
       Replace='|| (false && !Protocol.IsNativeStartIdentity(settings.serverId, token)';
       Failure='ordinary or unconfirmed arming passes through True' },
    @{ Name='replacement-registry-ignored'; Path=$source; Property='ExtractionSource';
       Find='&& ReferenceEquals(world.GetAlivePlayerByProfileID(local.ProfileId), local)'; Replace='&& true';
       Failure='scope excludes registry' },
    @{ Name='foreign-game-completion-blocked'; Path=$source; Property='ExtractionSource';
       Find='!ReferenceEquals(__instance, game)'; Replace='false';
       Failure='old game completion cannot claim current raid' },
    @{ Name='nonextraction-status-blocked'; Path=$source; Property='ExtractionSource';
       Find='|| (__1 != ExitStatus.Survived && __1 != ExitStatus.Runner)'; Replace='';
       Failure='non-extraction exit status preserved Left' },
    @{ Name='queued-extraction-during-death'; Path=$source; Property='ExtractionSource';
       Find='|| __0 != game.Profile.Id'; Replace='|| __0 != game.Profile.Id || world.MainPlayer?.HealthController?.IsAlive != true';
       Failure='completion does not require a living published body: dead' },
    @{ Name='timer-grace-reenables-extraction'; Path=$source; Property='ExtractionSource';
       Find='&& game.Status != GameStatus.SoftStopping'; Replace='';
       Failure='timer grace period cannot reenable Arena extractions' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ArenaExtractionChecks.csproj') -Token 'ARENAEXTRACTIONCHECKS_PASSED' -Properties @() -Faults $faults
