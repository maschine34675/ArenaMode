param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo ('artifacts/bowl-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$targets = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
$sentinel = Join-Path $output 'must-not-deploy'
function Run-Check([string]$name, [string[]]$arguments, [string[]]$programArguments = @()) {
    $log = Join-Path $output ($name + '.log')
    $commandArguments = $arguments + @("-p:CustomAfterMicrosoftCommonTargets=$targets", "-p:SPTUserMods=$sentinel")
    if ($programArguments.Count) { $commandArguments += @('--') + $programArguments }
    & dotnet @commandArguments *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 45; throw "$name failed: $log" }
    Get-Content -LiteralPath $log | Select-String -Pattern 'PASSED|passed|Bestanden|assertions|ALL_MAP_NATIVE_CANDIDATES' | ForEach-Object Line
    Write-Output "BOWL_CHECK_PASSED $name"
}
# Installed map data is evidence, never rewritten or supplemented by this check.
$map = Get-Content -LiteralPath (Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_bowl/base.json') -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_bowl' -or !$map.Enabled) { throw 'Installed Bowl is missing/disabled.' }
$players = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Player' })
$bots = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if ($players.Count -lt 1 -or $bots.Count -lt 6 -or @($bots | Where-Object BotZoneName -NE 'BotZone_Main').Count) {
    throw 'Installed Bowl has insufficient generic spawns or unexpected bot zones.'
}
Write-Output "BOWL_INSTALLED_SPAWNS player=$($players.Count) bot=$($bots.Count)"
$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Check 'server' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo', '--filter',
        'FullyQualifiedName~BowlMapTests|FullyQualifiedName~AirPitMapTests|FullyQualifiedName~Bay5MapMigrationTests|FullyQualifiedName~MapModeAccessTests|FullyQualifiedName~OptionalTransportView|FullyQualifiedName~ExpandedFullMapRoute|FullyQualifiedName~UnavailableOptionalMap')
    foreach ($harness in @('NativeMenuChecks','AreaConfigChecks','AreaMapPreviewChecks','ParticipantMenuChecks',
        'PftPortedMapChecks','PortedCoverChecks','ArenaLightingChecks','SpawnShieldScopeChecks')) {
        Run-Check $harness @('run', '--project', "$repo/tools/$harness/$harness.csproj", '-c', 'Release')
    }
    # This production harness also reads the installed Bot/BotPmc positions;
    # virtual geometry checks are not proof of native clearance or reachability.
    Run-Check 'ArenaPickupChecks' @('run', '--project', "$repo/tools/ArenaPickupChecks/ArenaPickupChecks.csproj", '-c', 'Release') @($SptRoot)
    $faultLog = Join-Path $output 'pft-negative-controls.log'
    & pwsh -NoProfile -File "$repo/tools/AirPitChecks/NegativeControls.ps1" *> $faultLog
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $faultLog -Tail 40; throw 'PFT fault controls failed.' }
    Get-Content -LiteralPath $faultLog | Select-String 'Rejected compiled fault' | ForEach-Object Line
} finally {
    $env:SPT_DATABASE = $previousDatabase
    if (Test-Path -LiteralPath $sentinel) { throw 'Checks unexpectedly deployed files.' }
}
Write-Output "BOWL_INTEGRATION_CHECKS_PASSED logs=$output"
