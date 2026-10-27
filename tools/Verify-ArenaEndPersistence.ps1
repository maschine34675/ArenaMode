#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
. (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
$directory = Join-Path $repo 'artifacts/raid-end-persistence-20261027/focused'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$sentinel = Join-Path $directory 'must-not-exist'
if (Test-Path -LiteralPath $sentinel) { throw 'No-deploy sentinel exists.' }
$before = Read-ArenaInstalledSnapshot 'D:/SPT41'
$server = 'D:/SPT41/SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$hash = (Get-FileHash -LiteralPath $server).Hash
$trx = Join-Path $directory 'results.trx'
if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx }
try {
    & dotnet test (Join-Path $repo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --nologo -v:minimal `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$sentinel" '-p:SPTPath=D:/SPT41' '-p:SPTUserPath=D:/SPT41/SPT_Runtime' `
        --filter 'FullyQualifiedName~ArenaEndPersistenceTests|FullyQualifiedName~RewardLifecycleTests|FullyQualifiedName~RewardReturnControlTests|FullyQualifiedName~IsolationProtectionTests|FullyQualifiedName~ArenaAreaServerTests' `
        --logger 'trx;LogFileName=results.trx' --results-directory $directory *> (Join-Path $directory 'run.log')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $directory 'run.log') -Tail 60; throw 'End persistence tests failed.' }
    $result = Assert-ArenaTestRun -TrxPath $trx -AllowedSkipReasons @() -RequireFamilies @('ArenaEndPersistenceTests','RewardLifecycleTests','RewardReturnControlTests')
    "ARENA_END_PERSISTENCE_PASSED total=$($result.Total) passed=$($result.Passed)"
} finally {
    Assert-ArenaInstalledSnapshotUnchanged $before 'D:/SPT41'
    if ((Get-FileHash -LiteralPath $server).Hash -ne $hash -or (Test-Path -LiteralPath $sentinel)) { throw 'Offline check changed installed files.' }
}
