param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo ('artifacts/chopshop-checks/' + [guid]::NewGuid().ToString('N'))
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
    Write-Output "CHOPSHOP_CHECK_PASSED $name"
}
# Installed map data is evidence, never rewritten or supplemented by this check.
# Chop Shop (location id arena_autoservice) ships only in an ArenaMapPort build that
# carries it (the unreleased feature/autoservice build 0.5.0+b7876d3 at the time of
# writing); the published 0.5.0 has the same version string and no Chop Shop. A missing
# location fails here, loudly, instead of silently skipping the installed-data part.
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_autoservice/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Chop Shop data is missing: $baseJson. Install an ArenaMapPort build that ships arena_autoservice."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_autoservice' -or !$map.Enabled) { throw 'Installed Chop Shop is missing/disabled.' }
$players = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Player' })
$bots = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if ($players.Count -lt 1 -or $bots.Count -lt 7 -or @($bots | Where-Object BotZoneName -NE 'BotZone_Main').Count) {
    throw 'Installed Chop Shop has insufficient generic spawns or unexpected bot zones.'
}
# The seven rows the Tactical refusal and the default of six participants were argued
# from; a changed set means those numbers must be measured again.
$expectedBots = @('arena_autoservice-bot-core-01','arena_autoservice-bot-core-02','arena_autoservice-bot-core-03',
    'arena_autoservice-bot-core-04','arena_autoservice-bot-core-05','arena_autoservice-bot-core-07','arena_autoservice-bot-born-00')
$botIds = @($bots | ForEach-Object { [string]$_.Id } | Sort-Object)
if (($botIds -join ',') -cne (($expectedBots | Sort-Object) -join ',')) {
    throw "Installed Chop Shop Bot/BotPmc rows changed: $($botIds -join ', ')."
}
if (@($players | Where-Object Infiltration -CNE 'ArenaAutoService').Count) { throw 'Installed Chop Shop player rows changed infiltration.' }
Write-Output "CHOPSHOP_INSTALLED_SPAWNS player=$($players.Count) bot=$($bots.Count)"
$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Check 'server' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo', '--filter',
        'FullyQualifiedName~ChopShopMapTests|FullyQualifiedName~ChopShopTeamSpawnTests|FullyQualifiedName~IcebergMapTests|FullyQualifiedName~BowlMapTests|FullyQualifiedName~AirPitMapTests|FullyQualifiedName~Bay5MapMigrationTests|FullyQualifiedName~MapModeAccessTests|FullyQualifiedName~OptionalTransportView|FullyQualifiedName~ExpandedFullMapRoute|FullyQualifiedName~UnavailableOptionalMap|FullyQualifiedName~DefaultsUseRealZoneAndSmallMapPopulation')
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
Write-Output "CHOPSHOP_INTEGRATION_CHECKS_PASSED logs=$output"
