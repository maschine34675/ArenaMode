$ErrorActionPreference='Stop'
$regRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$regStage = Join-Path $regRepo ('artifacts/announcer-delivery-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($regStage)
Push-Location $regRepo
try {
    # Pure source-linked projects: no native client/server deployment targets.
    foreach ($project in @('AnnouncerChecks','AnnouncerCombatChecks','AnnouncementProgressChecks',
        'AnnouncementIntegrationChecks','ShotAwardHistoryChecks','PitFireTeamChecks','TeamArenaExpansionChecks',
        'TeamGiveUpChecks')) {
        & dotnet run --project ('tools/'+$project) -c Release 2>&1 | Tee-Object (Join-Path $regStage ($project+'.log'))
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $project" }
    }
    foreach ($script in @('tools/ShotAwardIntegrationChecks/Verify.ps1','tools/TacticalPolishChecks/Verify.ps1',
        'tools/TdmMatchChecks/Run-Checks.ps1')) {
        & pwsh -NoProfile -File $script 2>&1 | Tee-Object (Join-Path $regStage ((Split-Path (Split-Path $script) -Leaf)+'.log'))
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $script" }
    }
    # Unit tests reference the server. Its deploy target is conditioned on the
    # destination existing, so provide a unique absent non-live SPTUserPath.
    $noDeploy = Join-Path $regStage 'not-installed'
    & dotnet test ArenaMode.Tests -c Release "-p:SPTUserPath=$noDeploy" --logger 'trx;LogFileName=units.trx' --results-directory $regStage 2>&1 |
        Tee-Object (Join-Path $regStage 'units.log')
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $noDeploy)) { throw 'Unit suite or no-deploy sentinel failed' }
    Write-Output "ANNOUNCER DELIVERY REGRESSIONS PASSED; evidence: $regStage"
} finally { Pop-Location }
