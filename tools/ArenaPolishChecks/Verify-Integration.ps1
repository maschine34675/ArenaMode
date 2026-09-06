$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& dotnet test (Join-Path $repo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Server/catalogue integration tests failed' }
foreach ($project in @('tools/ArenaTracerChecks', 'tools/TeamGiveUpChecks', 'tools/TeamDeathmatchRuntimeChecks',
    'tools/TeamArenaExpansionChecks', 'tools/TeamArenaGoalHandoffChecks')) {
    & dotnet run --project (Join-Path $repo $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing client regression failed: $project" }
}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Deployment.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Final deployment verification failed' }
Write-Output 'ARENA_POLISH_INTEGRATION_PASSED'
