$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo 'artifacts/direct-start-20261028/verification'
[IO.Directory]::CreateDirectory($run) | Out-Null
$safe = @("-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')", "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')")
function Check([string]$Name, [scriptblock]$Action) {
    & $Action *> (Join-Path $run ($Name + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "$Name failed; see $run" }
    Write-Output "PASS $Name"
}
Push-Location $repo
try {
    Check 'unit' { & pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 }
    Check 'release-build' { & dotnet build ArenaMode.sln -c Release --nologo @safe }
    Check 'direct-framework' { & powershell.exe -NoProfile -File tools/DirectStartChecks/Check-Net472.ps1 -Exe (Join-Path $repo 'ArenaMode.Session/bin/Release/maschine-ArenaMode.Session.exe') }
    Check 'offline-client' { & pwsh -NoProfile -File tools/OfflineSessionChecks/Verify.ps1 }
    Check 'client-contract' { & pwsh -NoProfile -File tools/ArenaSessionClientChecks/Verify.ps1 }
    Check 'helper-net472' { & pwsh -NoProfile -File tools/ArenaSessionHelperNet472Checks/Verify.ps1 -Configuration Release -KeepWork }
    Write-Output 'ARENA_DIRECT_START_PASSED'
} finally { Pop-Location }
