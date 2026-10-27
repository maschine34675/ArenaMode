#Requires -Version 7.0
param([ValidateSet('Labels','Regression')][string]$Stage = 'Regression')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $repo "artifacts/killfeed-faction-20261027/$Stage"
[void][IO.Directory]::CreateDirectory($evidence)
$harness = if ($Stage -eq 'Labels') { 'RoleLabelChecks' } else { 'KillfeedTextChecks,TeamDeathmatchRuntimeChecks,TeamGiveUpChecks' }
$arguments = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Run-ArenaVerification.ps1'),'-Harness',$harness)
if ($Stage -eq 'Labels') { $arguments += '-SkipUnitTests' }
& pwsh @arguments *> (Join-Path $evidence 'checks.log')
$result = $LASTEXITCODE
Get-Content (Join-Path $evidence 'checks.log') -Tail 8
if ($result -ne 0) { throw "Killfeed $Stage failed" }
foreach ($name in $harness.Split(',')) {
    Copy-Item (Join-Path $repo "artifacts/run-arena-verification/$name.log") $evidence
}
if ($Stage -eq 'Regression') {
    Copy-Item (Join-Path $repo 'artifacts/run-arena-verification/run-arena-verification.trx') $evidence
    . (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
    $before = Read-ArenaInstalledSnapshot 'D:\SPT41'
    $sentinel = Join-Path $evidence 'must-not-exist'
    if (Test-Path $sentinel) { throw 'No-deploy sentinel exists' }
    foreach ($project in @('ArenaMode.Client','ArenaMode.BigBrainCompat')) {
        & dotnet build (Join-Path $repo "$project/$project.csproj") -c Release --nologo `
            "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'AirPitChecks/NoDeploy.targets')" `
            "-p:SPTUserMods=$sentinel" *> (Join-Path $evidence "$project-build.log")
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $evidence "$project-build.log") -Tail 20; throw "$project build failed" }
        Assert-ArenaInstalledSnapshotUnchanged $before 'D:\SPT41'
    }
}
Write-Output "KILLFEED_FACTION_PASSED $Stage"
