#Requires -Version 7.0
param(
    [ValidateSet('Engine', 'Restoration', 'Api')][string]$Group = 'Engine',
    [string]$BaselinePath,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
. (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
$families = switch ($Group) {
    'Engine' { @('RewardRecoveryTests') }
    'Restoration' { @('RewardRestorationCheckpointTests', 'RewardLifecycleTests', 'RewardOfflineSessionTests', 'RewardRuntimeTests') }
    'Api' { @('RewardRecoveryApiTests') }
}
$filter = ($families | ForEach-Object { 'FullyQualifiedName~' + $_ }) -join '|'
$directory = Join-Path $repo ('artifacts/reward-recovery-verification/' + $Group)
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$sentinel = Join-Path $directory 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $sentinel) { throw 'The no-deploy sentinel must not exist.' }
$trx = Join-Path $directory 'results.trx'
if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx }
$clientBefore = Read-ArenaInstalledSnapshot $SptRoot
$server = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$serverBefore = (Get-FileHash -LiteralPath $server).Hash
$previousBaseline = $env:SPT_REWARD_RECOVERY_BASELINE
Push-Location $repo
try {
    if ($Group -eq 'Engine') {
        if (-not $BaselinePath -or -not (Test-Path -LiteralPath $BaselinePath -PathType Container)) {
            throw 'Engine acceptance requires the private baseline directory; pass -BaselinePath.'
        }
        $env:SPT_REWARD_RECOVERY_BASELINE = [IO.Path]::GetFullPath($BaselinePath)
    }
    $log = Join-Path $directory 'run.log'
    & dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo -v:minimal `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$sentinel" "-p:SPTPath=$SptRoot" "-p:SPTUserPath=$(Join-Path $SptRoot 'SPT_Runtime')" `
        --filter $filter --logger 'trx;LogFileName=results.trx' --results-directory $directory *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 45; throw "Reward recovery $Group tests failed." }
    $result = Assert-ArenaTestRun -TrxPath $trx -AllowedSkipReasons @() -RequireFamilies $families
    Write-Output "REWARD_RECOVERY_${Group}_PASSED total=$($result.Total) passed=$($result.Passed) skipped=$($result.Skipped)"
}
finally {
    Pop-Location
    $env:SPT_REWARD_RECOVERY_BASELINE = $previousBaseline
    Assert-ArenaInstalledSnapshotUnchanged $clientBefore $SptRoot
    if ((Get-FileHash -LiteralPath $server).Hash -ne $serverBefore) { throw 'Offline verification changed the installed server DLL.' }
    if (Test-Path -LiteralPath $sentinel) { throw 'Offline verification deployed into the sentinel.' }
}
