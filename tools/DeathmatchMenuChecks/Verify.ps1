$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
foreach ($project in @('DeathmatchMenuChecks','NativeModeSelectionChecks','NativeMenuChecks','TeamGiveUpChecks','TdmMatchMenuChecks','AreaTeamMenuChecks')) {
    & dotnet run --project (Join-Path $repo ("tools/" + $project)) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Failed: $project" }
}
& (Join-Path $repo 'tools/TdmMatchMenuChecks/Run-Checks.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Menu fault controls failed' }
& (Join-Path $repo 'tools/TdmMatchChecks/Run-Checks.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Terminal result safety regression failed' }
Write-Output 'DEATHMATCH_MENU_CHECKS_PASSED'
