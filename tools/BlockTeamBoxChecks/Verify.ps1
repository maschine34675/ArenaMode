param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot '../Assert-ArenaTestRun.ps1')
# Modelled on tools/ChopShopTeamBoxChecks: the installed snapshot proves that nothing
# below reached the live install, over and above the sentinel the deploy targets need.
$installedBefore = Read-ArenaInstalledSnapshot $SptRoot
$output = Join-Path $repo ('artifacts/block-team-boxes/' + [guid]::NewGuid().ToString('N'))
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

# Installed-data contract. ArenaTeamBoxCatalog.Yard names the installed
# arena_yard-bot-core-04 row as its immutable donor and outside destination, and
# ArenaLocationMutator refuses any drift at raid time. Check the installed MapPort
# data here, loudly: Block exists only in a MapPort build that ships it (the installed
# 0.6.0+1d8faa8 when this was written; the published 0.6.0 has the same version string
# and no Block).
$baseJson = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMapPort/db/arena_yard/base.json'
if (!(Test-Path -LiteralPath $baseJson)) {
    throw "Installed Block data is missing: $baseJson. Install an ArenaMapPort build that ships arena_yard."
}
$map = Get-Content -LiteralPath $baseJson -Raw | ConvertFrom-Json
if ($map.Id -cne 'arena_yard' -or !$map.Enabled) { throw 'Installed Block is missing/disabled.' }
$rows = @($map.SpawnPointParams)
if (@($rows | Where-Object { $_.Id -like 'arena-yard-tdm-v1-*' }).Count) {
    throw 'Installed Block contains per-raid team-box markers; they belong to the protected clone only.'
}
$donors = @($rows | Where-Object { $_.Id -ceq 'arena_yard-bot-core-04' })
if ($donors.Count -ne 1) { throw "Installed Block must hold exactly one arena_yard-bot-core-04 donor, found $($donors.Count)." }
$donor = $donors[0]
if ([single]$donor.Position.x -ne [single]-12.88 -or [single]$donor.Position.y -ne [single]6.17 -or [single]$donor.Position.z -ne [single]87.03) {
    throw "Installed Block donor moved: ($($donor.Position.x), $($donor.Position.y), $($donor.Position.z))."
}
$categories = @($donor.Categories | Sort-Object)
if ($donor.BotZoneName -cne 'BotZone_Main' -or $donor.CorePointId -ne 0 -or $donor.DelayToCanSpawnSec -ne 4 `
    -or ($categories -join ',') -cne 'Bot,BotPmc' -or $null -eq $donor.ColliderParams) {
    throw 'Installed Block donor header no longer matches the server admission contract.'
}
# MapPort's own faction rows must agree with "USEC south / BEAR north" (independent
# literal bounds of the two inset interiors from the Block decision record, not read from
# the catalog). Heights are the authored bands for the south box (6.3275 .. 11.9159) and
# the cut band of the north box (5.3275 .. 8.6, a deck at 9.16 lies above it).
function Inside([object]$row, [double[]]$box) {
    $p = $row.Position
    return $p.x -ge $box[0] -and $p.x -le $box[1] -and $p.y -ge $box[2] -and $p.y -le $box[3] -and $p.z -ge $box[4] -and $p.z -le $box[5]
}
$south = [double[]]@(-25.076, -5.347, 6.3275, 11.9159, 1.073, 16.374)
$north = [double[]]@(-14.738, -0.098, 5.3275, 8.6, 141.467, 155.341)
# Two Player rows carry Sides "All" (Sides says nothing about their box), so classify every
# Player row by the box that contains it: exactly one of the two, never both, never none.
$playerRows = @($rows | Where-Object { $_.Categories -contains 'Player' })
if ($playerRows.Count -ne 4) { throw "Installed Block has $($playerRows.Count) Player rows, expected 4." }
$southRows = @(); $northRows = @()
foreach ($row in $playerRows) {
    $inSouth = Inside $row $south
    $inNorth = Inside $row $north
    if ($inSouth -eq $inNorth) { throw "Installed Block Player row $($row.Id) is not inside exactly one team box." }
    if ($inSouth) { $southRows += $row } else { $northRows += $row }
}
if ($southRows.Count -ne 2 -or $northRows.Count -ne 2) {
    throw "Installed Block Player rows split $($southRows.Count)/$($northRows.Count) over the south/north box, expected 2/2."
}
# Faction rows sit in their own box: the Usec row in the south box, the Bear row in the north box,
# and each box holds one "All" row.
$usecRows = @($playerRows | Where-Object { $_.Sides -contains 'Usec' })
$bearRows = @($playerRows | Where-Object { $_.Sides -contains 'Bear' })
if ($usecRows.Count -ne 1 -or !(Inside $usecRows[0] $south)) { throw 'Installed USEC player row is not exactly one row inside the south box.' }
if ($bearRows.Count -ne 1 -or !(Inside $bearRows[0] $north)) { throw 'Installed BEAR player row is not exactly one row inside the north box.' }
if (@($southRows | Where-Object { $_.Sides -contains 'All' }).Count -ne 1) { throw 'Installed south box does not hold exactly one "All" Player row.' }
if (@($northRows | Where-Object { $_.Sides -contains 'All' }).Count -ne 1) { throw 'Installed north box does not hold exactly one "All" Player row.' }
$bots = @($rows | Where-Object { $_.Categories -contains 'Bot' -or $_.Categories -contains 'BotPmc' })
if (@($bots | Where-Object { (Inside $_ $south) -or (Inside $_ $north) }).Count) { throw 'An installed bot row lies inside a team box.' }
Write-Output "BLOCK_INSTALLED_DONOR arena_yard-bot-core-04 (-12.88, 6.17, 87.03); south=$($southRows.Count) north=$($northRows.Count) bots=$($bots.Count)"

$previousDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
    Run-Dotnet 'unit' @('test', "$repo/ArenaMode.Tests/ArenaMode.Tests.csproj", '-c', 'Release', '--nologo',
        '--logger', 'trx;LogFileName=block-teambox.trx', '--results-directory', $output)
    $run = Assert-ArenaTestRun -TrxPath (Join-Path $output 'block-teambox.trx')
    Write-Output "BLOCK_UNIT total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)"
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
Write-Output 'BLOCK_TEAM_BOX_CHECKS_PASSED'
