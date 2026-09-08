$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $arenaRepo
try {
    foreach ($script in @(
        'tools/ArenaAssemblyChecks/Verify-Regression.ps1',
        'tools/AreaTeamMenuChecks/Verify.ps1',
        'tools/TdmWarmupChecks/Verify.ps1')) {
        & pwsh -NoProfile -File $script
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $script" }
    }
    & pwsh -NoProfile -File tools/NativeAreaSelectionChecks/Verify.ps1 -Stage Integration
    if ($LASTEXITCODE -ne 0) { throw 'Native area integration failed' }
    foreach ($project in @('tools/NativeModeSelectionChecks', 'tools/AnnouncerChecks', 'tools/PitFireTeamChecks', 'tools/TeamArenaExpansionChecks')) {
        & dotnet run --project $project -c Release
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $project" }
    }
    # Existing server/profile tests build the server project. Its deploy target
    # only writes to an existing SPTUserMods path; use a unique absent sentinel.
    $arenaNoDeploy = Join-Path $arenaRepo ('.unlazy/tdm-match/no-server-deploy-' + [guid]::NewGuid().ToString('N'))
    & dotnet test ArenaMode.Tests -c Release --no-restore "-p:SPTUserMods=$arenaNoDeploy"
    if ($LASTEXITCODE -ne 0) { throw 'Server/profile/scoreboard regressions failed' }
    if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'Unexpected server deploy sentinel was created' }
    Write-Output 'TDM_MATCH_REGRESSIONS_PASSED'
}
finally { Pop-Location }
