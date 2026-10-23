param(
    [Parameter(Mandatory = $true)][ValidateSet('Rules', 'Integration', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Assert-ArenaTestRun.ps1')
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\customs-checks'
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
# SPTUserMods is a global MSBuild property; the server's deploy target copies into it, so a path that
# must not exist keeps `dotnet test` from writing the live user/mods (the test project builds the server).
$arenaNoDeploy = Join-Path $arenaResults 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'The no-deploy sentinel must not exist.' }
$env:SPT_DATABASE = Join-Path $arenaRuntime 'SPT_Data\database'
$env:SPT_RUNTIME = $arenaRuntime

function Invoke-CustomsDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Customs dotnet failure ($LASTEXITCODE): $($Arguments[0])" }
}
function Test-CustomsServer([string]$Filter) {
    Invoke-CustomsDotnet @('test', 'ArenaMode.Tests\ArenaMode.Tests.csproj', '-c', 'Release',
        "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", "-p:SPTUserMods=$arenaNoDeploy", '--nologo', '-v:minimal',
        '--filter', $Filter, '--logger', "trx;LogFileName=customs-$Stage.trx", '--results-directory', $arenaResults)
    $arenaRun = Assert-ArenaTestRun -TrxPath (Join-Path $arenaResults "customs-$Stage.trx")
    Write-Output "Customs test evidence: total=$($arenaRun.Total), passed=$($arenaRun.Passed), skipped=$($arenaRun.Skipped)"
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
            # The Customs negative controls of 2026-09 were retired on 2026-10-23: their expected messages left
            # the menu checks (e61b6e3, 9c67ed7) and the area-catalog mutant lives on in CustomsAllModesChecks
            # and MapModeAdmissionChecks.
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Integration -SptRoot $arenaSpt
        }
        'Build' {
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Build -SptRoot $arenaSpt
        }
    }
    Write-Output ('ARENA_CUSTOMS_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
