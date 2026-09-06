#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Verify-Contracts.ps1') -SptPath $SptPath
& (Join-Path $PSScriptRoot 'Verify-Regression.ps1')
Write-Output 'ARENA_LASER_BRIGHTNESS_PASSED'
