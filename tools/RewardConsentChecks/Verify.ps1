# Private reward control: real synthetic profiles and transports, no live deployment.
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$safe = @("-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')",
    "-p:SPTUserMods=$(Join-Path $repo 'artifacts\reward-private-consent\must-not-exist')")
Push-Location $repo
try {
    & pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness RewardMenuStateChecks,TdmMatchMenuChecks,RewardTransferChecks,RewardControlTransferChecks
    if ($LASTEXITCODE -ne 0) { throw 'Reward regression verification failed.' }
    foreach ($project in @('ArenaMode.Client/ArenaMode.Client.csproj', 'ArenaMode.Session/ArenaMode.Session.csproj')) {
        & dotnet build $project -c Release --nologo @safe
        if ($LASTEXITCODE -ne 0) { throw "No-deploy build failed: $project" }
    }
    Write-Output 'REWARD_CONSENT_VERIFIED'
}
finally { Pop-Location }
