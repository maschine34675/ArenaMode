param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaSentinel = Join-Path $arenaRepo ('artifacts/airpit-must-not-deploy-' + [guid]::NewGuid().ToString('N'))
$arenaTargets = Join-Path $PSScriptRoot 'NoDeploy.targets'
$arenaPreviousDatabase = $env:SPT_DATABASE
if (Test-Path -LiteralPath $arenaSentinel) { throw 'No-deploy sentinel must not exist.' }
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    $arenaFilter = 'FullyQualifiedName~AirPitMapTests|FullyQualifiedName~Bay5MapMigrationTests|FullyQualifiedName~EquatorMapConfigTests|FullyQualifiedName~MapModeAccessTests|FullyQualifiedName~ExpandedFullMapRoute|FullyQualifiedName~UnavailableOptionalMap|FullyQualifiedName~OptionalTransportView'
    & dotnet test (Join-Path $arenaRepo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" "-p:SPTUserMods=$arenaSentinel" --filter $arenaFilter
    if ($LASTEXITCODE -ne 0) { throw 'AirPit server regression tests failed.' }
    foreach ($arenaHarness in @('NativeMenuChecks', 'AreaConfigChecks', 'AreaMapPreviewChecks', 'PftPortedMapChecks', 'PortedCoverChecks')) {
        & dotnet run --project (Join-Path $arenaRepo "tools/$arenaHarness/$arenaHarness.csproj") -c Release `
            "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" "-p:SPTUserMods=$arenaSentinel"
        if ($LASTEXITCODE -ne 0) { throw "$arenaHarness failed." }
    }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'NegativeControls.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'AirPit compiled negative controls failed.' }
    if (Test-Path -LiteralPath $arenaSentinel) { throw 'Verification unexpectedly created the deployment sentinel.' }
}
finally {
    $env:SPT_DATABASE = $arenaPreviousDatabase
    if (Test-Path -LiteralPath $arenaSentinel) { throw 'No-deploy sentinel was created; inspect before any further build.' }
}
Write-Output 'AIRPIT_INTEGRATION_CHECKS_PASSED'
