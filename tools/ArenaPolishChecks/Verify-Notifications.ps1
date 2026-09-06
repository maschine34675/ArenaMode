$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& pwsh -NoProfile -File (Join-Path $repo 'tools/ArenaNotificationChecks/Verify-InstalledContracts.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Installed notification contracts failed' }
& pwsh -NoProfile -File (Join-Path $repo 'tools/ArenaNotificationChecks/Verify-Regression.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Notification presentation regressions failed' }
Write-Output 'ARENA_POLISH_NOTIFICATIONS_PASSED'
