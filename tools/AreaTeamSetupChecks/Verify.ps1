param([ValidateSet('Focused','Integration')][string]$Stage = 'Focused', [string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $arenaRepo
try {
    foreach ($arenaCheck in @('AreaTeamSpawnSurvey','AreaTeamServerChecks','AreaTeamMenuChecks','AreaTeamRuntimeChecks')) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File "tools\$arenaCheck\Verify.ps1" -SptRoot $SptRoot
        if ($LASTEXITCODE -ne 0) { throw "$arenaCheck failed ($LASTEXITCODE)." }
    }
    foreach ($arenaProject in @('tools\TeamDeathmatchRuntimeChecks\TeamDeathmatchRuntimeChecks.csproj',
        'tools\ArenaAreaChecks\ClientContracts\ClientContracts.csproj',
        'tools\PolygonAreaChecks\Enclosure\Enclosure.csproj')) {
        & dotnet run --project $arenaProject -c Release "-p:SPTPath=$SptRoot"
        if ($LASTEXITCODE -ne 0) { throw "$arenaProject failed ($LASTEXITCODE)." }
    }
    if ($Stage -eq 'Integration') {
        & powershell -NoProfile -ExecutionPolicy Bypass -File tools\CustomsContourChecks\Verify.ps1 -Stage Integration -SptRoot $SptRoot
        if ($LASTEXITCODE -ne 0) { throw "Existing full regression suite failed ($LASTEXITCODE)." }
        Write-Output 'ARENA_TEAM_SETUP_INTEGRATION_PASSED'
    }
    Write-Output 'ARENA_TEAM_SETUP_CHECKS_PASSED'
}
finally { Pop-Location }
