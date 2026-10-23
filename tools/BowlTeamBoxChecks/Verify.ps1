param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
# Absorbs tools/AirPitTeamBoxChecks (2026-10-23): the installed snapshot proves that nothing below
# reached the live install, over and above the sentinel the deploy targets would need.
$installedBefore = Read-ArenaInstalledSnapshot $SptRoot
$output = Join-Path $repo 'artifacts/bowl-team-boxes-20261023/offline'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$targets = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
$sentinel = Join-Path $output 'must-not-deploy'
function Run-Dotnet([string]$name, [string[]]$arguments, [string[]]$programArguments = @()) {
    $log = Join-Path $output ($name + '.log')
    $commandArguments = $arguments + @("-p:CustomAfterMicrosoftCommonTargets=$targets", "-p:SPTUserMods=$sentinel")
    if ($programArguments.Count) { $commandArguments += @('--') + $programArguments }
    & dotnet @commandArguments *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content $log -Tail 35; throw "$name failed: $log" }
    Get-Content $log | Select-String 'PASSED|Bestanden|passed|assertions|ALL_MAP_NATIVE_CANDIDATES|Fehler|Error\(s\)' | ForEach-Object Line
}
$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Dotnet 'unit' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo',
        '--logger', 'trx;LogFileName=bowl-teambox.trx', '--results-directory', $output)
    foreach ($harness in @('EquatorTeamSpawnChecks','SpawnGateChecks')) {
        $log = Join-Path $output ($harness + '.log')
        & pwsh -NoProfile -File "$repo/tools/$harness/Verify.ps1" *> $log
        if ($LASTEXITCODE -ne 0) { Get-Content $log -Tail 35; throw "$harness failed: $log" }
        Get-Content $log | Select-String 'PASSED|assertions' | ForEach-Object Line
    }
    foreach ($harness in @('ParticipantMenuChecks','NativeMenuChecks','TeamDeathmatchRuntimeChecks','SpawnShieldScopeChecks')) {
        Run-Dotnet $harness @('run', '--project', "$repo/tools/$harness/$harness.csproj", '-c', 'Release')
    }
    Run-Dotnet 'MapModeAdmissionChecks' @('run', '--project', "$repo/tools/MapModeAdmissionChecks/MapModeAdmissionChecks.csproj", '-c', 'Release') @($SptRoot)
    Run-Dotnet 'pickups' @('run', '--project', "$repo/tools/ArenaPickupChecks/ArenaPickupChecks.csproj", '-c', 'Release') @($SptRoot)
    foreach ($project in @('ArenaMode.Client','ArenaMode.BigBrainCompat','ArenaMode.Server')) {
        Run-Dotnet $project @('build', "$repo/$project/$project.csproj", '-c', 'Release', '--nologo')
    }
    if (Test-Path -LiteralPath $sentinel) { throw 'Offline checks unexpectedly deployed files.' }
} finally {
    $env:SPT_DATABASE = $previousDatabase
    Assert-ArenaInstalledSnapshotUnchanged $installedBefore $SptRoot
}
Write-Output 'BOWL_TEAM_BOX_CHECKS_PASSED'
