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
# carries it (the unreleased feature/autoservice builds, 0.5.0+160523d with the sixteen
# Bot rows at the time of writing); the published 0.5.0 has the same version string and
# no Chop Shop. A missing location fails here, loudly, instead of silently skipping the
# installed-data part.
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_autoservice/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Chop Shop data is missing: $baseJson. Install an ArenaMapPort build that ships arena_autoservice."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_autoservice' -or !$map.Enabled) { throw 'Installed Chop Shop is missing/disabled.' }
$players = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Player' })
$bots = @($map.SpawnPointParams | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
# Four Player rows and at least the sixteen Bot/BotPmc rows of ArenaMapPort 2026-11-12 (seven
# before). The decisions rest on the counts, not on the ids: the Tactical rule and the
# default of eight are pinned by MapModeAdmissionChecks with the production selector
# (CHOPSHOP-YARD-CENTER), so a later MapPort row change needs no re-listing of ids here.
if ($players.Count -ne 4) { throw "Installed Chop Shop has $($players.Count) Player rows, expected 4." }
if (@($bots | Where-Object BotZoneName -NE 'BotZone_Main').Count) { throw 'Installed Chop Shop has a Bot/BotPmc row outside BotZone_Main.' }
if ($bots.Count -lt 16) { throw "Installed Chop Shop has $($bots.Count) Bot/BotPmc rows, expected at least 16 (ArenaMapPort 0.5.0+160523d)." }
# The donor row of the team boxes and the Tactical anchor, with its exact floats.
$donor = @($map.SpawnPointParams | Where-Object Id -CEQ 'arena_autoservice-bot-core-03')
if ($donor.Count -ne 1 -or [double]$donor[0].Position.x -ne -367.44 -or [double]$donor[0].Position.y -ne -3.79 -or [double]$donor[0].Position.z -ne 492.11) {
    throw 'Installed Chop Shop donor row arena_autoservice-bot-core-03 is missing or moved.'
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
