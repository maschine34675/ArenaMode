param(
    [Parameter(Mandatory = $true)][ValidateSet('Geometry', 'Enclosure', 'Integration', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\polygon-checks'
$arenaNoDeploy = Join-Path $arenaResults 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'The no-deploy sentinel directory must not exist.' }
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$env:SPT_DATABASE = Join-Path $arenaRuntime 'SPT_Data\database'
$env:SPT_RUNTIME = $arenaRuntime

function Invoke-PolygonDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Polygon dotnet failure ($LASTEXITCODE): $($Arguments[0])" }
}
function Test-PolygonServer([string]$Filter) {
    Invoke-PolygonDotnet @('test', 'ArenaMode.Tests\ArenaMode.Tests.csproj', '-c', 'Release',
        "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", "-p:SPTUserMods=$arenaNoDeploy", '--nologo', '-v:minimal',
        '--filter', $Filter, '--logger', "trx;LogFileName=polygon-$Stage.trx", '--results-directory', $arenaResults)
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaResults "polygon-$Stage.trx")
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    $arenaUnpassed = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
    if ([int]$arenaCounts.total -le 0 -or [int]$arenaCounts.passed -ne [int]$arenaCounts.total -or $arenaUnpassed.Count -ne 0) {
        throw "Expected passing, unskipped polygon checks: $($arenaCounts.OuterXml)"
    }
    Write-Output "Polygon server evidence: total=$($arenaCounts.total), passed=$($arenaCounts.passed), unpassed=$($arenaUnpassed.Count)"
}
Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaResults "stage-$Stage.log") -Force | Out-Null
try {
    switch ($Stage) {
        'Geometry' {
            Test-PolygonServer 'FullyQualifiedName~ArenaAreaPolygonTests|FullyQualifiedName~ArenaAreaGeometryTests|FullyQualifiedName~CustomsMapConfigTests'
            Invoke-PolygonDotnet @('run', '--project', 'tools\PolygonAreaChecks\Capture\Capture.csproj', '-c', 'Release', "-p:SPTPath=$arenaSpt")
        }
        'Enclosure' {
            & (Join-Path $PSScriptRoot 'Enclosure\Verify.ps1')
        }
        'Integration' {
            Test-PolygonServer 'FullyQualifiedName~ArenaAreaServerTests|FullyQualifiedName~CustomsMapConfigTests'
            Invoke-PolygonDotnet @('run', '--project', 'tools\PolygonAreaChecks\Scope\Scope.csproj', '-c', 'Release', "-p:SPTPath=$arenaSpt")
            foreach ($arenaHarness in @('NativeMenuChecks', 'NativeAreaHookChecks', 'NativeMapViewChecks',
                'NativeModeSelectionChecks', 'LabsBotSpawnChecks', 'TeamDeathmatchRuntimeChecks', 'TeamGiveUpChecks')) {
                Invoke-PolygonDotnet @('run', '--project', "tools\$arenaHarness\$arenaHarness.csproj", '-c', 'Release', "-p:SPTPath=$arenaSpt")
            }
            Invoke-PolygonDotnet @('run', '--project', 'tools\ArenaAreaChecks\ClientContracts\ClientContracts.csproj', '-c', 'Release')
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\NegativeControls.ps1')
            & (Join-Path $PSScriptRoot 'NegativeControls.ps1') -SptRoot $arenaSpt
        }
        'Build' {
            # The final stage DOES deploy. All preceding server tests explicitly
            # use a nonexistent destination to leave the mirrored live install alone.
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Build -SptRoot $arenaSpt
        }
    }
    Write-Output ('ARENA_POLYGON_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
