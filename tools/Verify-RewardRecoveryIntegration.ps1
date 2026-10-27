#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$BaselinePath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $BaselinePath -PathType Container)) { throw 'Private baseline directory missing.' }
$previousBaseline = $env:SPT_REWARD_RECOVERY_BASELINE
try {
    $env:SPT_REWARD_RECOVERY_BASELINE = [IO.Path]::GetFullPath($BaselinePath)
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Run-ArenaVerification.ps1') -Harness RewardMenuStateChecks
    if ($LASTEXITCODE -ne 0) { throw 'Recovery integration regression failed.' }
    Write-Output 'REWARD_RECOVERY_INTEGRATION_PASSED'
}
finally { $env:SPT_REWARD_RECOVERY_BASELINE = $previousBaseline }
