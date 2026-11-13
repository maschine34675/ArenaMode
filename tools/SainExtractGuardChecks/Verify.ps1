param([string]$SptRoot = 'D:\SPT41', [switch]$SkipContracts)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Bots/ArenaSainExtractGuard.cs')).Path
$faults = @(
    @{ Name='guard-ignores-arena'; Path=$source; Property='ProductionSource';
       Find='if (__result && _ready && InArenaRaid()) __result = false;'; Replace='if (false) __result = false;';
       Failure='active Arena raid refuses SAIN extraction' },
    @{ Name='stale-active-raid'; Path=$source; Property='ProductionSource';
       Find='&& ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world));'; Replace=');';
       Failure='stale active session keeps SAIN''s answer (game)' },
    @{ Name='map-not-checked'; Path=$source; Property='ProductionSource';
       Find='|| !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase))'; Replace=')';
       Failure='armed for another map keeps SAIN''s answer' },
    @{ Name='foreign-postfix-can-reopen'; Path=$source; Property='ProductionSource';
       Find='{ priority = Priority.Last }'; Replace='';
       Failure='guard installs one last-priority postfix' },
    @{ Name='version-unchecked'; Path=$source; Property='ProductionSource';
       Find='ArenaSainVersion.Require(assembly);'; Replace='';
       Failure='SAIN older than 4.5.1 is refused' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'SainExtractGuardChecks.csproj') -Token 'ARENA_SAIN_EXTRACT_GUARD_CHECKS_PASSED' -Properties @() -Faults $faults

# The stand-ins model SAIN 4.5.1; this reads the installed assembly to prove they still match.
if (-not $SkipContracts) {
    # A contract failure throws; ErrorActionPreference Stop carries it out of this script.
    & (Join-Path $PSScriptRoot 'Verify-Contracts.ps1') -SptRoot $SptRoot
}
