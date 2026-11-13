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
       Failure='timer grace period cannot reenable Arena extractions' },
    # Fail-closed admission (2026-11-13): Arena must not arm without these hooks.
    @{ Name='admission-ignores-hooks'; Path=$source; Property='ExtractionSource';
       Find='AdmissionFailure => _enabled ? null'; Replace='AdmissionFailure => true ? null';
       Failure='admission refuses before Enable' },
    @{ Name='failed-enable-claims-ready'; Path=$source; Property='ExtractionSource';
       Find='Shutdown();'; Replace='Patches.UnpatchSelf(); _enabled = true;';
       Failure='failed hook 0 refuses Arena admission' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ArenaExtractionChecks.csproj') -Token 'ARENAEXTRACTIONCHECKS_PASSED' -Properties @() -Faults $faults
