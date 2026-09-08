param([Parameter(Mandatory)][string]$OutputPath, [string]$DriverPath = (Join-Path $PSScriptRoot '../../ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'))
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText($DriverPath)
function Slice([string]$start, [string]$end) {
    $i = $source.IndexOf($start, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Missing production fragment: $start" }
    $j = $source.IndexOf($end, $i, [StringComparison]::Ordinal)
    if ($j -lt $i) { throw "Missing production fragment end: $end" }
    return $source.Substring($i, $j - $i)
}
$preload = Slice '// A final kill or time limit can land while kit bundles are loading.' 'var requestedSpawn = new ArenaSpawnSelector('
$failure = Slice 'if (failure != null)' 'Plugin.ModLogger.LogError($"Arena respawn failed:'
$methods = foreach ($name in @('ShouldBlockDeathScreen(LocalGame game)', 'ShouldBlockKilledStop(LocalGame game)', 'ShouldHoldBlackScreen()', 'ShouldSuppressKilledSessionResult()')) {
    $start = $source.IndexOf('public bool ' + $name, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production driver method: $name" }
    $end = $source.IndexOf(';', $start)
    $source.Substring($start, $end - $start + 1)
}
$template = @'
using System.Collections;
using EFT;
using Redeploy.Core.Engine;
using ArenaMode.Session;
namespace ArenaMode.Respawn;
// Extracted from actual production entry/failure guards on every verifier run.
// Only native operations on either side of the guards are represented by spies.
internal sealed class RespawnGuardProbe
{
    internal bool IsTerminalTdmHold, IsRespawnInProgress, StandDown;
    internal bool SameRaid = true, EnteredEngine, FallbackCalled;
    internal int LeaseEnds;
    private bool IsSameActiveRaid(LocalGame game, int epoch) => SameRaid;
    private void EndPipelineUse(int epoch) => LeaseEnds++;
    internal IEnumerator AfterPreload(LocalGame game, Player deadPlayer, int raidEpoch)
    {
        PRELOAD
        EnteredEngine = true;
        yield break;
    }
    internal IEnumerator AfterEngineFailure(Exception failure, int raidEpoch)
    {
        FAILURE
        FallbackCalled = true;
        StandDown = true;
        }
        yield break;
    }
    METHODS
}
'@
[IO.File]::WriteAllText($OutputPath, $template.Replace('PRELOAD', $preload).Replace('FAILURE', $failure).Replace('METHODS', ($methods -join "`n")))
