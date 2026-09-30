param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../PickupObjectiveChecks/Build-Staged.ps1') -SptRoot $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Incendiary staged build failed' }
Write-Output 'ARENA_MOLOTOV_BUILD_PASSED'
