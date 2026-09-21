param([string]$SptPath=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot '../ArenaInfiniteAmmoChecks/Verify.ps1') -Mode Backpack -SptPath $SptPath
