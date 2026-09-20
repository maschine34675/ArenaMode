param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaNoDeploy = Join-Path $arenaRepo 'artifacts/equator-no-server-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'No-deploy sentinel must not exist.' }
$arenaPreviousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    & dotnet test (Join-Path $arenaRepo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release "-p:SPTUserMods=$arenaNoDeploy" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Arena server tests failed.' }
    foreach ($arenaHarness in @('NativeMenuChecks', 'NativeAreaHookChecks', 'NativeMapViewChecks', 'ArenaLabyrinthAccessChecks')) {
        & dotnet run --project (Join-Path $arenaRepo "tools/$arenaHarness/$arenaHarness.csproj") -c Release
        if ($LASTEXITCODE -ne 0) { throw "$arenaHarness failed." }
    }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'NegativeControls.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Equator negative controls failed.' }
    & (Join-Path $arenaRepo 'tools/DoubleJumpChecks/Build-Staged.ps1') -SptRoot $SptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Staged Arena client build failed.' }
    if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'Verification unexpectedly created server deployment directory.' }
}
finally { $env:SPT_DATABASE = $arenaPreviousDatabase }
Write-Output 'ARENA_EQUATOR_CHECKS_PASSED'
