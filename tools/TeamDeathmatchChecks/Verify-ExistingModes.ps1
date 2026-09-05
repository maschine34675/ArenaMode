$ErrorActionPreference = 'Stop'
$tdmRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tdmNoDeploy = Join-Path $tdmRepo '.unlazy\tdm\no-server-deploy'
if (Test-Path -LiteralPath $tdmNoDeploy) { throw 'No-deploy sentinel directory must not exist' }

& dotnet test (Join-Path $tdmRepo 'ArenaMode.Tests') -c Release --no-restore "-p:SPTUserMods=$tdmNoDeploy"
if ($LASTEXITCODE -ne 0) { throw 'Server/profile/scoreboard tests failed' }

foreach ($tdmCheck in @('PitFireTeamChecks', 'TeamArenaExpansionChecks', 'SoloTacticalSpawnChecks',
    'LabsBotSpawnChecks', 'ArenaTracerChecks', 'AnnouncerChecks', 'CorpseCleanupChecks',
    'NativeModeSelectionChecks', 'NativeSideViewChecks', 'NativeMapViewChecks', 'NativeMenuChecks')) {
    & dotnet run --project (Join-Path $tdmRepo ('tools\' + $tdmCheck)) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing mode regression failed: $tdmCheck" }
}
Write-Output 'ARENA_TDM_EXISTING_MODES_VERIFIED'
