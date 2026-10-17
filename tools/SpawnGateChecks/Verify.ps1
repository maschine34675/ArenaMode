param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaArtifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $arenaArtifacts | Out-Null
$arenaRuntime = Get-Content -Raw (Join-Path $arenaRepo 'ArenaMode.Client\Bots\ArenaEquatorTeamSpawns.cs')
$arenaGates = Get-Content -Raw (Join-Path $arenaRepo 'ArenaMode.Client\Compat\ArenaMapPortSpawnGates.cs')
$arenaFlow = Get-Content -Raw (Join-Path $arenaRepo 'ArenaMode.Client\Modes\TeamDeathmatchMatchFlow.cs')
$arenaMethod = [regex]::Match($arenaFlow, '(?s)    private void TickMatchStart\(bool ready\).*?(?=    // Called before)').Value
if (!$arenaMethod) { throw 'Production countdown method boundary changed.' }
$arenaFlowSource = @"
using UnityEngine;
namespace ArenaMode.Modes;
internal sealed class GateCountdownFixture
{
    private TeamDeathmatchMatchPolicy _rules = new(25, 600);
    private bool _announcedReady = false;
    private float _gateCountdownEndsAt;
    internal TdmMatchPhase Phase => _rules.Phase;
    internal float StartedAt => _rules.StartedAt;
    internal void Tick(bool ready) => TickMatchStart(ready);
$arenaMethod
}
"@
$arenaFlowPath = Join-Path $arenaArtifacts 'Flow.cs'
[IO.File]::WriteAllText($arenaFlowPath, $arenaFlowSource)
function Invoke-GateCheck([string]$Name, [string]$Property = '', [string]$Source = '') {
    $arenaArgs = @('run','--project',(Join-Path $PSScriptRoot 'SpawnGateChecks.csproj'),'-c','Release')
    if ($Property) { $arenaArgs += "-p:${Property}=$Source" }
    $arenaOutput = (& dotnet @arenaArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $arenaCode = $LASTEXITCODE
    $arenaOutput | Set-Content (Join-Path $arenaArtifacts ($Name + '.log'))
    return [pscustomobject]@{ Code=$arenaCode; Output=$arenaOutput }
}
$arenaResult = Invoke-GateCheck 'baseline'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('EQUATOR_SPAWN_GATE_CHECKS_PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
$arenaFaults = @(
    @('foreign-hold','GateSource',$arenaGates,'if (isHeld() || closedParts() <= 0)','if (closedParts() <= 0)','FOREIGN:'),
    @('unconfirmed-token','GateSource',$arenaGates,'return settings != null && Protocol.IsLifecycleRequestId(token)','return true || settings != null && Protocol.IsLifecycleRequestId(token)','TOKEN:'),
    @('stale-epoch','GateSource',$arenaGates,'epoch != ArenaSession.ArmEpoch || ','','STALE-PATH:'),
    @('no-reset-release','RuntimeSource',$arenaRuntime,'ReleaseGates("ArenaMode Equator reset");','','RESET:'),
    @('post-release-partial','RuntimeSource',$arenaRuntime,'|| _gates?.IsHeld != true','|| _gates == null || (!_gates.IsHeld && !_gates.Released)','RESERVATION:'),
    @('local-path-bypass','RuntimeSource',$arenaRuntime,'&& local.status == NavMeshPathStatus.PathComplete','&& true','LOCAL-PATH:'),
    @('outside-corners','RuntimeSource',$arenaRuntime,'&& local.corners.All(p => InsideBooth(p, usec))','&& true','CORNERS:'),
    @('unsurveyed-position','RuntimeSource',$arenaRuntime,'&& (p.Position - position).sqrMagnitude <= .5625f','&& true','CANONICAL:'),
    @('rejected-start-leak','RuntimeSource',$arenaRuntime,'ReleaseGates("ArenaMode Equator start not admitted");',';','REJECTED-START:'),
    @('countdown-before-open','FlowSource',$arenaFlowSource,'&& !Bots.ArenaEquatorTeamSpawns.TryOpenForCombat()) return;','&& false) return;','RELEASE:'),
    @('exfil-unblocked','RuntimeSource',$arenaRuntime,'DeferredExtractions[__instance] = player;','return true; //','EXFIL:'),
    @('exfil-no-replay','RuntimeSource',$arenaRuntime,'ResumeExtractions();',';','EXFIL-RESUME:'),
    @('exfil-stale-trigger','RuntimeSource',$arenaRuntime,'&& CurrentBoothExit(pair.Key) && pair.Key.Entered.Contains(pair.Value)','&& pair.Key.Entered.Contains(pair.Value)','EXFIL-STALE:'),
    @('stop-wrong-profile','RuntimeSource',$arenaRuntime,'&& profileId == _game?.Profile?.Id','&& true','STOP-PROFILE:'),
    @('stop-suppressed','RuntimeSource',$arenaRuntime,'&& (_game.Status == GameStatus.Stopping || _game.Status == GameStatus.Stopped)','&& true','STOP-SUPPRESSED:'),
    @('pause-timeout','RuntimeSource',$arenaRuntime,'Time.time - _openingStartedAt >= 10f','Time.realtimeSinceStartup - _openingStartedAt >= 10f','PAUSE:'),
    @('premature-load-release','RuntimeSource',$arenaRuntime,'try { await original; }','try { await original; if (!IsRequired) ReleaseGates("fixture premature release"); }','RUN-BEFORE-EVENT:'),
    @('started-pair-unguarded','RuntimeSource',$arenaRuntime,'|| !ArenaSession.IsExactStartedRaid(_game, world)','|| false','STALE-START:'),
    @('armed-event-released','RuntimeSource',$arenaRuntime,'if (ArenaSession.ArmedForNextRaid || _gates?.OwnsCurrentRaid != true','if (_gates?.OwnsCurrentRaid != true','UNCONSUMED:')
)
try {
    foreach ($arenaFault in $arenaFaults) {
        if (($arenaFault[2].Split([string[]]@($arenaFault[3]), [StringSplitOptions]::None)).Count -ne 2) { throw "Mutation site changed: $($arenaFault[0])" }
        $arenaMutant = Join-Path $arenaArtifacts ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaFault[2].Replace($arenaFault[3],$arenaFault[4]))
        $arenaResult = Invoke-GateCheck $arenaFault[0] $arenaFault[1] $arenaMutant
        if ($arenaResult.Code -eq 0 -or !$arenaResult.Output.Contains($arenaFault[5]) -or $arenaResult.Output.Contains('error CS')) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
} finally {
    $arenaResult = Invoke-GateCheck 'restored'
    if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('EQUATOR_SPAWN_GATE_CHECKS_PASSED')) { throw $arenaResult.Output }
}
Write-Output 'EQUATOR_SPAWN_GATE_CHECKS_PASSED'
