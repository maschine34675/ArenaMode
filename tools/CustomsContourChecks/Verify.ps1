param(
    [Parameter(Mandatory = $true)][ValidateSet('Policy', 'Runtime', 'Integration', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptRoot
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\customs-contour'
$arenaNoDeploy = Join-Path $arenaResults 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'The no-deploy sentinel must not exist.' }
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$env:SPT_DATABASE = Join-Path $arenaRuntime 'SPT_Data\database'
$env:SPT_RUNTIME = $arenaRuntime

function Invoke-ContourDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Contour verification dotnet failure ($LASTEXITCODE)." }
}
function Test-ContourServer([string]$Filter) {
    $arenaArgs = @('test', 'ArenaMode.Tests\ArenaMode.Tests.csproj', '-c', 'Release',
        "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", "-p:SPTUserMods=$arenaNoDeploy", '--nologo', '-v:minimal',
        '--logger', "trx;LogFileName=contour-$Stage.trx", '--results-directory', $arenaResults)
    if ($Filter) { $arenaArgs += @('--filter', $Filter) }
    Invoke-ContourDotnet $arenaArgs
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaResults "contour-$Stage.trx")
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    $arenaUnpassed = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
    if ([int]$arenaCounts.total -le 0 -or [int]$arenaCounts.passed -ne [int]$arenaCounts.total -or $arenaUnpassed.Count -ne 0) {
        throw "Expected executed and unskipped contour tests: $($arenaCounts.OuterXml)"
    }
    Write-Output "Contour test evidence: total=$($arenaCounts.total), passed=$($arenaCounts.passed), unpassed=$($arenaUnpassed.Count)"
}
function Run-ContourHarness([string]$Project) {
    Invoke-ContourDotnet @('run', '--project', $Project, '-c', 'Release', "-p:SPTPath=$arenaSpt")
}
function Get-ContourHash([string]$Path) {
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}

Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaResults "$Stage.log") -Force | Out-Null
$arenaLive = @(
    (Join-Path $arenaSpt 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll'),
    (Join-Path $arenaSpt 'BepInEx\plugins\maschine-Redeploy.Core.dll'),
    (Join-Path $arenaRuntime 'user\mods\ArenaMode\maschine-ArenaMode.Server.dll'))
$arenaBefore = @{}
foreach ($arenaPath in $arenaLive) { $arenaBefore[$arenaPath] = Get-ContourHash $arenaPath }
try {
    switch ($Stage) {
        'Policy' { Test-ContourServer 'FullyQualifiedName~CustomsContour' }
        'Runtime' {
            Run-ContourHarness 'tools\PolygonAreaChecks\Enclosure\Enclosure.csproj'
            Run-ContourHarness 'tools\ArenaAreaChecks\ClientContracts\ClientContracts.csproj'
        }
        'Integration' {
            Test-ContourServer ''
            foreach ($arenaHarness in @('NativeMenuChecks', 'NativeAreaHookChecks', 'NativeMapViewChecks',
                'NativeModeSelectionChecks', 'LabsBotSpawnChecks', 'TeamDeathmatchRuntimeChecks', 'TeamGiveUpChecks')) {
                Run-ContourHarness "tools\$arenaHarness\$arenaHarness.csproj"
            }
            Run-ContourHarness 'tools\PolygonAreaChecks\Capture\Capture.csproj'
            Run-ContourHarness 'tools\PolygonAreaChecks\Scope\Scope.csproj'
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\NegativeControls.ps1')
            & (Join-Path $PSScriptRoot '..\PolygonAreaChecks\NegativeControls.ps1') -SptRoot $arenaSpt
        }
        'Build' {
            # All automatic deployment targets write only to this fresh staging
            # tree. Junctions provide references; neither is an output directory.
            $arenaStage = Join-Path $arenaResults ('staging-' + [Guid]::NewGuid().ToString('N'))
            $arenaData = New-Item -ItemType Directory -Path (Join-Path $arenaStage 'EscapeFromTarkov_Data')
            $arenaPlugins = New-Item -ItemType Directory -Path (Join-Path $arenaStage 'BepInEx\plugins')
            $arenaMods = New-Item -ItemType Directory -Path (Join-Path $arenaStage 'SPT_Runtime\user\mods')
            New-Item -ItemType Junction -Path (Join-Path $arenaData.FullName 'Managed') -Target (Join-Path $arenaSpt 'EscapeFromTarkov_Data\Managed') | Out-Null
            New-Item -ItemType Junction -Path (Join-Path $arenaPlugins.FullName 'spt') -Target (Join-Path $arenaSpt 'BepInEx\plugins\spt') | Out-Null
            $arenaBuildArgs = @('-c', 'Release', "-p:SPTPath=$arenaStage", "-p:SPTUserPath=$(Join-Path $arenaStage 'SPT_Runtime')", '--nologo', '-v:minimal')
            Invoke-ContourDotnet (@('build', 'ArenaMode.Client\ArenaMode.Client.csproj') + $arenaBuildArgs)
            Invoke-ContourDotnet (@('build', 'ArenaMode.Server\ArenaMode.Server.csproj') + $arenaBuildArgs)
            $arenaPairs = @(
                @('ArenaMode.Client\bin\Release\maschine-ArenaMode.Client.dll', (Join-Path $arenaPlugins.FullName 'maschine-ArenaMode.Client.dll')),
                @('build\server\Release\maschine-ArenaMode.Server\maschine-ArenaMode.Server.dll', (Join-Path $arenaMods.FullName 'ArenaMode\maschine-ArenaMode.Server.dll')))
            $arenaArtifacts = foreach ($arenaPair in $arenaPairs) {
                $arenaBuilt = Join-Path $arenaRepo $arenaPair[0]
                $arenaHash = Get-ContourHash $arenaBuilt
                if ($arenaHash -ne (Get-ContourHash $arenaPair[1])) { throw "Staging mismatch: $($arenaPair[0])" }
                [pscustomobject]@{ Built = $arenaBuilt; Staged = $arenaPair[1]; SHA256 = $arenaHash }
            }
            $arenaArtifacts | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaResults 'staged-build.json')
            $arenaArtifacts | Format-Table -AutoSize
        }
    }
    foreach ($arenaPath in $arenaLive) {
        if ((Get-ContourHash $arenaPath) -ne $arenaBefore[$arenaPath]) { throw "Verification altered a live DLL: $arenaPath" }
    }
    Write-Output ('CUSTOMS_CONTOUR_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
