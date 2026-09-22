param()
$ErrorActionPreference = 'Stop'
$arenaUiRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaUiEvidence = Join-Path $arenaUiRoot ('artifacts/ui-refresh-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaUiEvidence -Force | Out-Null
foreach ($arenaUiProject in @('RaidInfoPanelChecks', 'NativeArenaHudChecks', 'KillfeedTextChecks',
    'NativeMenuChecks', 'AreaTeamMenuChecks', 'AreaMapPreviewChecks', 'ArenaAssemblyChecks', 'SpectatorHealthChecks')) {
    $arenaUiOutput = & dotnet run --project (Join-Path $arenaUiRoot ('tools/' + $arenaUiProject)) -c Release 2>&1
    $arenaUiExit = $LASTEXITCODE
    $arenaUiOutput | Set-Content -LiteralPath (Join-Path $arenaUiEvidence ($arenaUiProject + '.log'))
    if ($arenaUiExit -ne 0) { throw ($arenaUiProject + ': ' + ($arenaUiOutput -join "`n")) }
    $arenaUiOutput | Select-Object -Last 3 | Write-Output
}
foreach ($arenaUiCheck in @('TdmMatchMenuChecks/Run-Checks.ps1', 'TacticalPolishChecks/Verify.ps1')) {
    $arenaUiOutput = & pwsh -NoProfile -File (Join-Path $arenaUiRoot ('tools/' + $arenaUiCheck)) 2>&1
    $arenaUiExit = $LASTEXITCODE
    $arenaUiOutput | Set-Content -LiteralPath (Join-Path $arenaUiEvidence (($arenaUiCheck -replace '/', '-') + '.log'))
    if ($arenaUiExit -ne 0) { throw ($arenaUiCheck + ': ' + ($arenaUiOutput -join "`n")) }
    $arenaUiOutput | Select-Object -Last 3 | Write-Output
}
$arenaUiBuild = & pwsh -NoProfile -File (Join-Path $arenaUiRoot 'tools/DoubleJumpChecks/Build-Staged.ps1') 2>&1
$arenaUiExit = $LASTEXITCODE
$arenaUiBuild | Set-Content -LiteralPath (Join-Path $arenaUiEvidence 'client-build.log')
if ($arenaUiExit -ne 0) { throw ($arenaUiBuild -join "`n") }
$arenaUiBuild | Select-Object -Last 2 | Write-Output
Write-Output ('Evidence: ' + $arenaUiEvidence)
Write-Output 'ARENA_UI_REFRESH_VERIFIED'
