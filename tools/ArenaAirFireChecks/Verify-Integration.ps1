$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $arenaRepo
try {
    & pwsh -NoProfile -File tools/DoubleJumpChecks/Verify.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Existing double-jump regression failed' }
    & pwsh -NoProfile -File tools/TdmMatchMenuChecks/Run-Checks.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Shared settings/native menu regression failed' }
    & pwsh -NoProfile -File tools/DoubleJumpChecks/Build-Staged.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Integrated staged client build failed' }
    Write-Output 'ARENA_AIR_MOVEMENT_INTEGRATION_PASSED'
}
finally { Pop-Location }
