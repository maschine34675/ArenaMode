$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
foreach ($script in @('tools/WeaponVariantChecks/Verify.ps1', 'tools/TeamLaserChecks/Verify-Brightness.ps1')) {
    & pwsh -NoProfile -File (Join-Path $repo $script)
    if ($LASTEXITCODE -ne 0) { throw "Leaf verification failed: $script" }
}
# The test project builds the server, whose deploy target writes the live user/mods unless the no-deploy
# targets and a sentinel that must not exist are passed (AGENTS.md, build/deploy).
& dotnet test (Join-Path $repo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --no-restore --verbosity quiet `
    "-p:CustomAfterMicrosoftCommonTargets=$repo/tools/AirPitChecks/NoDeploy.targets" "-p:SPTUserMods=$repo/artifacts/weapon-laser-must-not-deploy"
if ($LASTEXITCODE -ne 0) { throw 'Existing server, catalogue and Gun Game tests failed' }
foreach ($project in @('tools/ArenaTracerChecks', 'tools/TeamGiveUpChecks', 'tools/TeamDeathmatchChecks', 'tools/TeamDeathmatchRuntimeChecks', 'tools/TeamArenaExpansionChecks', 'tools/TeamArenaGoalHandoffChecks')) {
    & dotnet run --project (Join-Path $repo $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing client regression failed: $project" }
}
Write-Output 'ARENA_WEAPON_LASER_INTEGRATION_PASSED'
