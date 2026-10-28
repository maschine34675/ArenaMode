$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo 'artifacts/entry-restart-hardening-20261028/verification'
[IO.Directory]::CreateDirectory($run) | Out-Null
$safe = @("-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')", "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')")
function Check([string]$Name, [scriptblock]$Action) {
    & $Action *> (Join-Path $run ($Name + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "$Name failed; see $run" }
    Write-Output "PASS $Name"
}
Push-Location $repo
try {
    Check 'unit' { & dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo @safe --logger 'trx;LogFileName=all.trx' --results-directory $run }
    Check 'helper-launch' { & pwsh -NoProfile -File tools/ArenaHelperLaunchChecks/Verify.ps1 }
    Check 'offline-client' { & pwsh -NoProfile -File tools/OfflineSessionChecks/Verify.ps1 }
    Check 'client-contract' { & pwsh -NoProfile -File tools/ArenaSessionClientChecks/Verify.ps1 }
    Check 'helper-net472' { & pwsh -NoProfile -File tools/ArenaSessionHelperNet472Checks/Verify.ps1 -Configuration Release -KeepWork }
    Check 'release-build' { & dotnet build ArenaMode.sln -c Release --nologo @safe }
    Write-Output 'ENTRY_RESTART_HARDENING_PASSED'
} finally { Pop-Location }
