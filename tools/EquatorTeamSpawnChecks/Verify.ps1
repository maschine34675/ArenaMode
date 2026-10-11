param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'EquatorTeamSpawnChecks.csproj'
$arenaEvidence = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $arenaEvidence -Force | Out-Null
$arenaSource = (Get-Content -Raw -LiteralPath (Join-Path $arenaRepo 'ArenaMode.Client\Bots\ArenaEquatorTeamSpawns.cs')).Replace("`r`n", "`n")
function Invoke-EquatorCheck([string]$Name, [string]$Source = '') {
    $arenaArgs = @('run','--project',$arenaProject,'-c','Release')
    if ($Source) { $arenaArgs += "-p:RuntimeSource=$Source" }
    $arenaPrior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = (& dotnet @arenaArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $arenaPrior }
    $arenaOutput | Set-Content -LiteralPath (Join-Path $arenaEvidence ($Name + '.log'))
    return [pscustomobject]@{ Code=$arenaCode; Output=$arenaOutput }
}
$arenaResult = Invoke-EquatorCheck 'baseline'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('EQUATOR_TEAM_SPAWN_CHECKS_PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
# Isolated source mutations only; each must reach the named behavioral check.
$arenaFaults = @(
    @('missing-prepared-fallback', 'internal static bool IsRequired => ArenaSession.IsActive', 'internal static bool IsRequired => Points.Count > 0 && ArenaSession.IsActive', 'FAIL-CLOSED:'),
    @('cross-faction', 'spec.Usec == usec &&', 'true &&', 'CAPACITY-LOG:'),
    @('pending-reuse', 'return !Reservations.Values.Any', 'return true || !Reservations.Values.Any', 'PENDING:'),
    @('wrong-category', '&& point.Categories == (ESpawnCategoryMask.Player | ESpawnCategoryMask.Bot | ESpawnCategoryMask.BotPmc)', '', 'HEADERS: category'),
    @('unchecked-body', '&& ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)', '', 'ARRIVAL: wrong body'),
    @('actual-geometry', '&& FloorAndClear(player.Position) && Connected(player.Position)', '', 'ARRIVAL-BODY:'),
    @('player-arrival-bypass', '|| !_playerReservation.Accept(player)', '|| false', 'PLAYER-ARRIVAL:'),
    @('failed-load-not-cancelled', 'ArenaSession.ClearArmed("Equator team-box loading failed");', '', 'LOADING: sync failure'),
    @('stale-arm-cancelled', '|| lease.Epoch != ArenaSession.ArmEpoch || lease.Token != ArenaServerClient.PendingArmToken', '', 'REARM-TOKEN:'),
    @('capacity-bypass', 'if (capacity < size)', 'if (false)', 'CAPACITY: blocked slot'),
    @('initial-body-bypass', '|| !FloorAndClear(local.Position) || !Connected(local.Position)', '', 'INITIAL-BODY:')
)
try {
    foreach ($arenaFault in $arenaFaults) {
        if (($arenaSource.Split([string[]]@($arenaFault[1]), [StringSplitOptions]::None)).Count -ne 2) { throw "Mutation site changed: $($arenaFault[0])" }
        $arenaMutant = Join-Path $arenaEvidence ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaFault[1], $arenaFault[2]))
        $arenaResult = Invoke-EquatorCheck $arenaFault[0] $arenaMutant
        if ($arenaResult.Code -eq 0 -or !$arenaResult.Output.Contains($arenaFault[3]) -or $arenaResult.Output.Contains('error CS')) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
} finally {
    $arenaResult = Invoke-EquatorCheck 'restored'
    if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('EQUATOR_TEAM_SPAWN_CHECKS_PASSED')) { throw $arenaResult.Output }
}
Write-Output 'EQUATOR TEAM SPAWN CHECKS PASSED'
