param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot '../Assert-ArenaTestRun.ps1')
# Modelled on tools/BowlTeamBoxChecks: the installed snapshot proves that nothing
# below reached the live install, over and above the sentinel the deploy targets need.
$installedBefore = Read-ArenaInstalledSnapshot $SptRoot
$output = Join-Path $repo ('artifacts/iceberg-team-boxes/' + [guid]::NewGuid().ToString('N'))
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

# Installed-data contract. ArenaTeamBoxCatalog.Iceberg names the installed
# arena_iceberg-bot-core-01 row as its immutable donor and outside destination, and
# ArenaLocationMutator refuses any drift at raid time. Check the installed MapPort
# data here, loudly: Iceberg exists only in a MapPort build that ships it (the
# unreleased feature/iceberg build 0.4.0+32fda33 when this was written).
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_iceberg/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Iceberg data is missing: $baseJson. Install an ArenaMapPort build that ships arena_iceberg."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_iceberg' -or !$map.Enabled) { throw 'Installed Iceberg is missing/disabled.' }
$rows = @($map.SpawnPointParams)
if (@($rows | Where-Object { $_.Id -like 'arena-iceberg-tdm-v1-*' }).Count) {
    throw 'Installed Iceberg contains per-raid team-box markers; they belong to the protected clone only.'
}
$donors = @($rows | Where-Object { $_.Id -ceq 'arena_iceberg-bot-core-01' })
if ($donors.Count -ne 1) { throw "Installed Iceberg must hold exactly one arena_iceberg-bot-core-01 donor, found $($donors.Count)." }
$donor = $donors[0]
if ([single]$donor.Position.x -ne [single]-10.88 -or [single]$donor.Position.y -ne [single]3.25 -or [single]$donor.Position.z -ne [single]-80.66) {
    throw "Installed Iceberg donor moved: ($($donor.Position.x), $($donor.Position.y), $($donor.Position.z))."
}
$categories = @($donor.Categories | Sort-Object)
if ($donor.BotZoneName -cne 'BotZone_Main' -or $donor.CorePointId -ne 0 -or $donor.DelayToCanSpawnSec -ne 4 `
    -or ($categories -join ',') -cne 'Bot,BotPmc' -or $null -eq $donor.ColliderParams) {
    throw 'Installed Iceberg donor header no longer matches the server admission contract.'
}
# MapPort's own faction rows must agree with "USEC west / BEAR east" (independent
# literal bounds of the two clipped interiors, not read from the catalog).
function Inside([object]$row, [double[]]$box) {
    $p = $row.Position
    return $p.x -ge $box[0] -and $p.x -le $box[1] -and $p.y -ge $box[2] -and $p.y -le $box[3] -and $p.z -ge $box[4] -and $p.z -le $box[5]
}
$west = [double[]]@(-48.31, -36.392, 2.328, 10.271, -96.926, -76.014)
$east = [double[]]@(82.732, 114.444, -0.4, 5.0, -98.465, -78.223)
$usecRows = @($rows | Where-Object { $_.Categories -contains 'Player' -and $_.Sides -contains 'Usec' })
$bearRows = @($rows | Where-Object { $_.Categories -contains 'Player' -and $_.Sides -contains 'Bear' })
if (!$usecRows.Count -or @($usecRows | Where-Object { !(Inside $_ $west) }).Count) { throw 'Installed USEC player rows are not inside the west box.' }
if (!$bearRows.Count -or @($bearRows | Where-Object { !(Inside $_ $east) }).Count) { throw 'Installed BEAR player rows are not inside the east box.' }
$bots = @($rows | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if (@($bots | Where-Object { (Inside $_ $west) -or (Inside $_ $east) }).Count) { throw 'An installed bot row lies inside a team box.' }
Write-Output "ICEBERG_INSTALLED_DONOR arena_iceberg-bot-core-01 (-10.88, 3.25, -80.66); usec=$($usecRows.Count) bear=$($bearRows.Count) bots=$($bots.Count)"

$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Dotnet 'unit' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo',
        '--logger', 'trx;LogFileName=iceberg-teambox.trx', '--results-directory', $output)
    $run = Assert-ArenaTestRun -TrxPath (Join-Path $output 'iceberg-teambox.trx')
    Write-Output "ICEBERG_UNIT total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)"
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
Write-Output 'ICEBERG_TEAM_BOX_CHECKS_PASSED'
