param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo ('artifacts/iceberg-checks/' + [guid]::NewGuid().ToString('N'))
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
    Write-Output "ICEBERG_CHECK_PASSED $name"
}
# Installed map data is evidence, never rewritten or supplemented by this check.
# Iceberg ships only in an ArenaMapPort build that carries arena_iceberg (the
# unreleased feature/iceberg build 0.4.0+32fda33 at the time of writing); the
# published 0.4.0 has none. A missing location fails here, loudly, instead of
# silently skipping the installed-data part.
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_iceberg/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Iceberg data is missing: $baseJson. Install an ArenaMapPort build that ships arena_iceberg."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_iceberg' -or !$map.Enabled) { throw 'Installed Iceberg is missing/disabled.' }
$players = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Player' })
$bots = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if ($players.Count -lt 1 -or $bots.Count -lt 6 -or @($bots | Where-Object BotZoneName -NE 'BotZone_Main').Count) {
    throw 'Installed Iceberg has insufficient generic spawns or unexpected bot zones.'
}
if (@($players | Where-Object Infiltration -CNE 'ArenaIceberg').Count) { throw 'Installed Iceberg player rows changed infiltration.' }
Write-Output "ICEBERG_INSTALLED_SPAWNS player=$($players.Count) bot=$($bots.Count)"
$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Check 'server' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo', '--filter',
        'FullyQualifiedName~IcebergMapTests|FullyQualifiedName~BowlMapTests|FullyQualifiedName~AirPitMapTests|FullyQualifiedName~Bay5MapMigrationTests|FullyQualifiedName~MapModeAccessTests|FullyQualifiedName~OptionalTransportView|FullyQualifiedName~ExpandedFullMapRoute|FullyQualifiedName~UnavailableOptionalMap|FullyQualifiedName~DefaultsUseRealZoneAndSmallMapPopulation')
    foreach ($harness in @('NativeMenuChecks','AreaConfigChecks','AreaMapPreviewChecks','ParticipantMenuChecks',
        'PftPortedMapChecks','PortedCoverChecks','ArenaLightingChecks','SpawnShieldScopeChecks','ArenaExtractionChecks')) {
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
Write-Output "ICEBERG_INTEGRATION_CHECKS_PASSED logs=$output"
