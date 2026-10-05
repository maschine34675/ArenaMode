$ErrorActionPreference='Stop'
foreach($check in @('../DoubleJumpChecks/Verify.ps1','../ArenaAirControlChecks/Verify.ps1',
    '../ArenaAirControlChecks/Verify-Contracts.ps1','../ArenaHandlingChecks/Verify.ps1',
    '../TdmMatchMenuChecks/Run-Checks.ps1')) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot $check)
    if($LASTEXITCODE -ne 0) {throw "Integration failed: $check"}
}
Write-Output 'ARENA_BUNNYHOP_INTEGRATION_PASSED'
