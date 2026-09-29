param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
 # Includes actual server-side scoreboard model/data contracts, without deploy.
 & pwsh -NoProfile -File tools/CustomsAreaRefreshChecks/Verify-Server.ps1
 if($LASTEXITCODE -ne 0){throw 'Server data/model regression failed'}
 & pwsh -NoProfile -File tools/UiRefreshChecks/Verify.ps1
 if($LASTEXITCODE -ne 0){throw 'HUD/menu integration or isolated client build failed'}
 Write-Output 'ARENA UI INTEGRATION CHECKS PASSED'
} finally {Pop-Location}
