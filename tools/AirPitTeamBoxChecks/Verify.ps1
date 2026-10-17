param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaBefore = Read-ArenaInstalledSnapshot $SptRoot
$arenaDatabaseBefore = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    & dotnet test (Join-Path $arenaRepo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --nologo --verbosity quiet `
        "-p:CustomAfterMicrosoftCommonTargets=$arenaRepo/tools/AirPitChecks/NoDeploy.targets" `
        "-p:SPTUserMods=$arenaRepo/artifacts/airpit-boxes-must-not-deploy"
    if ($LASTEXITCODE -ne 0) { throw 'Server/data regression suite failed.' }
    foreach ($arenaHarness in @('EquatorTeamSpawnChecks','MapModeAdmissionChecks','ParticipantMenuChecks','NativeMenuChecks')) {
        & dotnet run --project (Join-Path $arenaRepo "tools/$arenaHarness/$arenaHarness.csproj") -c Release -- $SptRoot
        if ($LASTEXITCODE -ne 0) { throw "$arenaHarness failed." }
    }
} finally {
    $env:SPT_DATABASE = $arenaDatabaseBefore
    Assert-ArenaInstalledSnapshotUnchanged $arenaBefore $SptRoot
}
Write-Output 'AIRPIT_TEAM_BOX_CHECKS_PASSED'
