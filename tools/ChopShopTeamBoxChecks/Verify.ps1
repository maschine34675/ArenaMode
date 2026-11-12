param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot '../Assert-ArenaTestRun.ps1')
# Modelled on tools/IcebergTeamBoxChecks: the installed snapshot proves that nothing
# below reached the live install, over and above the sentinel the deploy targets need.
$installedBefore = Read-ArenaInstalledSnapshot $SptRoot
$output = Join-Path $repo ('artifacts/chopshop-team-boxes/' + [guid]::NewGuid().ToString('N'))
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

# Installed-data contract. ArenaTeamBoxCatalog.AutoService names the installed
# arena_autoservice-bot-core-03 row as its immutable donor and outside destination, and
# ArenaLocationMutator refuses any drift at raid time. Check the installed MapPort
# data here, loudly: Chop Shop exists only in a MapPort build that ships it (the
# unreleased feature/autoservice build 0.5.0+b7876d3 when this was written).
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_autoservice/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Chop Shop data is missing: $baseJson. Install an ArenaMapPort build that ships arena_autoservice."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_autoservice' -or !$map.Enabled) { throw 'Installed Chop Shop is missing/disabled.' }
$rows = @($map.SpawnPointParams)
if (@($rows | Where-Object { $_.Id -like 'arena-autoservice-tdm-v1-*' }).Count) {
    throw 'Installed Chop Shop contains per-raid team-box markers; they belong to the protected clone only.'
}
$donors = @($rows | Where-Object { $_.Id -ceq 'arena_autoservice-bot-core-03' })
if ($donors.Count -ne 1) { throw "Installed Chop Shop must hold exactly one arena_autoservice-bot-core-03 donor, found $($donors.Count)." }
$donor = $donors[0]
if ([single]$donor.Position.x -ne [single]-367.44 -or [single]$donor.Position.y -ne [single]-3.79 -or [single]$donor.Position.z -ne [single]492.11) {
    throw "Installed Chop Shop donor moved: ($($donor.Position.x), $($donor.Position.y), $($donor.Position.z))."
}
$categories = @($donor.Categories | Sort-Object)
if ($donor.BotZoneName -cne 'BotZone_Main' -or $donor.CorePointId -ne 0 -or $donor.DelayToCanSpawnSec -ne 4 `
    -or ($categories -join ',') -cne 'Bot,BotPmc' -or $null -eq $donor.ColliderParams) {
    throw 'Installed Chop Shop donor header no longer matches the server admission contract.'
}
# MapPort's own faction rows must agree with "USEC east / BEAR west" (independent
# literal bounds of the two clipped interiors, not read from the catalog). Heights are
# the measured cut of 12.11.2026 (-4.8 .. -1.0), not the authored bounds.
function Inside([object]$row, [double[]]$box) {
    $p = $row.Position
    return $p.x -ge $box[0] -and $p.x -le $box[1] -and $p.y -ge $box[2] -and $p.y -le $box[3] -and $p.z -ge $box[4] -and $p.z -le $box[5]
}
$east = [double[]]@(-343.587, -332.999, -4.8, -1.0, 473.821, 511.764)
$west = [double[]]@(-413.635, -402.769, -4.8, -1.0, 473.498, 511.441)
$usecRows = @($rows | Where-Object { $_.Categories -contains 'Player' -and $_.Sides -contains 'Usec' })
$bearRows = @($rows | Where-Object { $_.Categories -contains 'Player' -and $_.Sides -contains 'Bear' })
if (!$usecRows.Count -or @($usecRows | Where-Object { !(Inside $_ $east) }).Count) { throw 'Installed USEC player rows are not inside the east box.' }
if (!$bearRows.Count -or @($bearRows | Where-Object { !(Inside $_ $west) }).Count) { throw 'Installed BEAR player rows are not inside the west box.' }
$bots = @($rows | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if (@($bots | Where-Object { (Inside $_ $east) -or (Inside $_ $west) }).Count) { throw 'An installed bot row lies inside a team box.' }
Write-Output "CHOPSHOP_INSTALLED_DONOR arena_autoservice-bot-core-03 (-367.44, -3.79, 492.11); usec=$($usecRows.Count) bear=$($bearRows.Count) bots=$($bots.Count)"

$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Dotnet 'unit' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo',
        '--logger', 'trx;LogFileName=chopshop-teambox.trx', '--results-directory', $output)
    $run = Assert-ArenaTestRun -TrxPath (Join-Path $output 'chopshop-teambox.trx')
    Write-Output "CHOPSHOP_UNIT total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)"
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
Write-Output 'CHOPSHOP_TEAM_BOX_CHECKS_PASSED'
