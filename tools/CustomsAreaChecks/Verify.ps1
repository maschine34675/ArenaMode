param(
    [Parameter(Mandatory = $true)][ValidateSet('Rules', 'Integration', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\customs-checks'
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$env:SPT_DATABASE = Join-Path $arenaRuntime 'SPT_Data\database'
$env:SPT_RUNTIME = $arenaRuntime

function Invoke-CustomsDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Customs dotnet failure ($LASTEXITCODE): $($Arguments[0])" }
}
function Test-CustomsServer([string]$Filter) {
    Invoke-CustomsDotnet @('test', 'ArenaMode.Tests\ArenaMode.Tests.csproj', '-c', 'Release',
        "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", '--nologo', '-v:minimal',
        '--filter', $Filter, '--logger', "trx;LogFileName=customs-$Stage.trx", '--results-directory', $arenaResults)
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaResults "customs-$Stage.trx")
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    $arenaUnpassed = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
    if ([int]$arenaCounts.total -le 0 -or [int]$arenaCounts.passed -ne [int]$arenaCounts.total -or $arenaUnpassed.Count -ne 0) {
        throw "Expected passing, unskipped Customs tests: $($arenaCounts.OuterXml)"
    }
    Write-Output "Customs test evidence: total=$($arenaCounts.total), passed=$($arenaCounts.passed), unpassed=$($arenaUnpassed.Count)"
}

Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaResults "stage-$Stage.log") -Force | Out-Null
try {
    switch ($Stage) {
        'Rules' {
            Test-CustomsServer 'FullyQualifiedName~Customs|FullyQualifiedName~ArenaAreaGeometryTests'
            & (Join-Path $PSScriptRoot 'SpawnContracts\Verify.ps1') -NativeRoot (Join-Path $arenaSpt 'Development\_decompiled')
        }
        'Integration' {
            Test-CustomsServer 'FullyQualifiedName~Customs|FullyQualifiedName~ArenaAreaServerTests'
            foreach ($arenaHarness in @('NativeMenuChecks', 'NativeMapViewChecks', 'NativeModeSelectionChecks',
                    'LabsBotSpawnChecks', 'TeamDeathmatchRuntimeChecks', 'TeamGiveUpChecks')) {
                Invoke-CustomsDotnet @('run', '--project', "tools\$arenaHarness\$arenaHarness.csproj", '-c', 'Release', "-p:SPTPath=$arenaSpt")
            }
            & (Join-Path $PSScriptRoot 'NegativeControls.ps1') -SptRoot $arenaSpt
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Integration -SptRoot $arenaSpt
        }
        'Build' {
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Build -SptRoot $arenaSpt
        }
    }
    Write-Output ('ARENA_CUSTOMS_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
