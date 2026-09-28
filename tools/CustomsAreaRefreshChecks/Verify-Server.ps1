param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\customs-refresh-server'
$arenaNoDeploy = Join-Path $arenaResults 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'The no-deploy sentinel must not exist.' }
$arenaDatabase = Join-Path $arenaRuntime 'SPT_Data\database'
if (!(Test-Path -LiteralPath (Join-Path $arenaDatabase 'locations\bigmap\base.json'))) {
    throw 'The installed Customs database is required; skipping real-data cases is not success.'
}
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$arenaPreviousDatabase = $env:SPT_DATABASE
$arenaPreviousRuntime = $env:SPT_RUNTIME
$env:SPT_DATABASE = $arenaDatabase
$env:SPT_RUNTIME = $arenaRuntime

function Get-AreaTeamFileHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return '<absent>' }
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}

$arenaLive = @(
    (Join-Path $arenaSpt 'BepInEx\plugins\maschine-ArenaMode.Client.dll'),
    (Join-Path $arenaSpt 'BepInEx\plugins\maschine-Redeploy.Core.dll'),
    (Join-Path $arenaRuntime 'user\mods\ArenaMode\maschine-ArenaMode.Server.dll'))
$arenaBefore = @{}
foreach ($arenaPath in $arenaLive) { $arenaBefore[$arenaPath] = Get-AreaTeamFileHash $arenaPath }
$arenaTrxName = 'area-team-' + [Guid]::NewGuid().ToString('N') + '.trx'
Push-Location $arenaRepo
try {
    # SPTUserMods is a global MSBuild property. The mod's AfterBuild deploy target
    # checks this nonexistent sentinel, never the installed user/mods directory.
    & dotnet test 'ArenaMode.Tests\ArenaMode.Tests.csproj' -c Release `
        "-p:SPTPath=$arenaSpt" "-p:SPTUserPath=$arenaRuntime" "-p:SPTUserMods=$arenaNoDeploy" `
        --logger "trx;LogFileName=$arenaTrxName" --results-directory $arenaResults --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Area team server tests failed ($LASTEXITCODE)." }
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaResults $arenaTrxName)
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    $arenaUnpassed = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' })
    if ([int]$arenaCounts.total -le 0 -or [int]$arenaCounts.passed -lt ([int]$arenaCounts.total - 1) -or $arenaUnpassed.Count -ne 0) {
        throw "Expected executed and unskipped tests: $($arenaCounts.OuterXml)"
    }
    $skipped=@($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
    if(@($skipped | Where-Object { $_.testName -ne 'ArenaMode.Tests.BotTierScalePatchTests.InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline' }).Count -ne 0){throw 'Unexpected skipped test'}
    foreach ($arenaExpected in @('AreaTeamRouteFreezesEchoesAndReplaysOnlyTheExactSize',
        'AreaTeamInstalledCloneHasCapacityForEitherLocalFactionAndPreservesAllNativeMarkers',
        'AreaTeamTamperingAfterNativeMutationBlocksClientRelease', 'AreaTeamCatalogTests')) {
        if (@($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -like "*$arenaExpected*" }).Count -eq 0) {
            throw "Required test family was not executed: $arenaExpected"
        }
    }
    if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'Verification unexpectedly created the no-deploy sentinel.' }
    foreach ($arenaPath in $arenaLive) {
        if ((Get-AreaTeamFileHash $arenaPath) -ne $arenaBefore[$arenaPath]) { throw "Verification changed a live DLL: $arenaPath" }
    }
    Write-Output "CUSTOMS AREA SERVER CHECKS PASSED total=$($arenaCounts.total) passed=$($arenaCounts.passed) skipped=$([int]$arenaCounts.total-[int]$arenaCounts.passed) trx=$arenaTrxName"
}
finally {
    Pop-Location
    $env:SPT_DATABASE = $arenaPreviousDatabase
    $env:SPT_RUNTIME = $arenaPreviousRuntime
}
