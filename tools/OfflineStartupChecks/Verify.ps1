param([ValidateSet('Focused', 'Helper', 'Regression')][string]$Stage = 'Focused')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out = Join-Path $repo 'artifacts/offline-startup-checks'
[IO.Directory]::CreateDirectory($out) | Out-Null
$noDeploy = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
$sentinel = Join-Path $out 'must-not-exist'
if (Test-Path -LiteralPath $sentinel) { throw 'No-deploy sentinel exists.' }
. (Join-Path $repo 'tools/Assert-ArenaTestRun.ps1')
. (Join-Path $repo 'tools/Read-ArenaInstalledSnapshot.ps1')
$snapshot = Read-ArenaInstalledSnapshot 'D:\SPT41'
$payloads = @('SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll', 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe')
$hashes = @{}
foreach ($payload in $payloads) { $hashes[$payload] = (Get-FileHash -LiteralPath (Join-Path 'D:\SPT41' $payload)).Hash }
Push-Location $repo
try {
    if ($Stage -eq 'Focused') {
        $trx = Join-Path $out 'focused.trx'
        if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx }
        & dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo -v:minimal `
            "-p:CustomAfterMicrosoftCommonTargets=$noDeploy" "-p:SPTUserMods=$sentinel" `
            --filter 'FullyQualifiedName~RewardOfflineSessionTests|FullyQualifiedName~OfflineSessionHelperTests|FullyQualifiedName~RewardStartupTests|FullyQualifiedName~RewardRestorationCheckpointTests|FullyQualifiedName~RewardRuntimeTests|FullyQualifiedName~RewardHelperProcessIdentityTests|FullyQualifiedName~ArenaDirectStartTests|FullyQualifiedName~ArenaSessionHelperProcessTests' `
            --logger 'trx;LogFileName=focused.trx' --results-directory $out *> (Join-Path $out 'focused.log')
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'focused.log') -Tail 55; throw 'Focused tests failed.' }
        $result = Assert-ArenaTestRun $trx -AllowedSkipReasons @() -RequireFamilies @('LostDisposableState', 'LateDiscardedReceipt', 'InterruptedDiscard', 'DiscardedTree')
        Write-Output "focused passed=$($result.Passed) skipped=$($result.Skipped)"
    } elseif ($Stage -eq 'Helper') {
        & dotnet build ArenaMode.Session/ArenaMode.Session.csproj -c Release --nologo -v:minimal `
            "-p:CustomAfterMicrosoftCommonTargets=$noDeploy" "-p:SPTUserMods=$sentinel" *> (Join-Path $out 'helper.log')
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'helper.log') -Tail 45; throw 'Shipping helper build failed.' }
    } else {
        & pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 *> (Join-Path $out 'regression.log')
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'regression.log') -Tail 50; throw 'Regression failed.' }
        Get-Content (Join-Path $out 'regression.log') -Tail 8
    }
    Assert-ArenaInstalledSnapshotUnchanged $snapshot 'D:\SPT41'
    foreach ($payload in $payloads) {
        if ((Get-FileHash -LiteralPath (Join-Path 'D:\SPT41' $payload)).Hash -ne $hashes[$payload]) { throw "Installed payload changed: $payload" }
    }
    if (Test-Path -LiteralPath $sentinel) { throw 'No-deploy sentinel was created.' }
    Write-Output ('OFFLINE_STARTUP_' + $Stage.ToUpperInvariant() + '_PASSED')
} finally { Pop-Location }
