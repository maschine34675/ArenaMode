param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $arenaRepo ('artifacts/airpit-faults/' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($OutputRoot)
$arenaTargets = Join-Path $PSScriptRoot 'NoDeploy.targets'
$arenaSentinel = Join-Path $OutputRoot 'must-not-deploy'
foreach ($fault in @(
    @{ Name = 'AirPitMissingPftSpawnAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapSpawns.cs';
       Property = 'PortedSource'; Harness = 'PftPortedMapChecks';
       Before = '&& !string.Equals(map, "arena_airpit", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'arena_airpit/tactical/False' },
    @{ Name = 'AirPitMissingPftCombatAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs';
       Property = 'CombatSource'; Harness = 'PortedCoverChecks';
       Before = '&& !string.Equals(world.LocationId, "arena_airpit", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'EMPTY-COVER arena_airpittactical' },
    @{ Name = 'BowlMissingPftSpawnAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapSpawns.cs';
       Property = 'PortedSource'; Harness = 'PftPortedMapChecks';
       Before = '&& !string.Equals(map, "arena_bowl", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'arena_bowl/tactical/False' },
    @{ Name = 'BowlMissingPftCombatAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs';
       Property = 'CombatSource'; Harness = 'PortedCoverChecks';
       Before = '&& !string.Equals(world.LocationId, "arena_bowl", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'EMPTY-COVER arena_bowltactical' },
    # PFT Tactical is admitted on Iceberg since ArenaMapPort's rows of 2026-11-12, so these
    # adapters run there in the mod; they are scoped by map, not mode, and their own
    # fixtures reach tactical first.
    @{ Name = 'IcebergMissingPftSpawnAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapSpawns.cs';
       Property = 'PortedSource'; Harness = 'PftPortedMapChecks';
       Before = '&& !string.Equals(map, "arena_iceberg", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'arena_iceberg/tactical/False' },
    @{ Name = 'IcebergMissingPftCombatAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs';
       Property = 'CombatSource'; Harness = 'PortedCoverChecks';
       Before = '&& !string.Equals(world.LocationId, "arena_iceberg", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'EMPTY-COVER arena_icebergtactical' },
    # Chop Shop refuses PFT Tactical and PFT Zombies, so these adapters never run on it in
    # the mod, but every port is listed there and the fixtures reach tactical first.
    @{ Name = 'AutoServiceMissingPftSpawnAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapSpawns.cs';
       Property = 'PortedSource'; Harness = 'PftPortedMapChecks';
       Before = '&& !string.Equals(map, "arena_autoservice", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'arena_autoservice/tactical/False' },
    @{ Name = 'AutoServiceMissingPftCombatAdapter'; File = 'ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs';
       Property = 'CombatSource'; Harness = 'PortedCoverChecks';
       Before = '&& !string.Equals(world.LocationId, "arena_autoservice", StringComparison.OrdinalIgnoreCase)'; After = '';
       Failure = 'EMPTY-COVER arena_autoservicetactical' }
)) {
    $source = [IO.File]::ReadAllText((Join-Path $arenaRepo $fault.File))
    if (!$source.Contains($fault.Before)) { throw "Missing fault anchor: $($fault.Name)" }
    $mutant = Join-Path $OutputRoot ($fault.Name + '.cs')
    [IO.File]::WriteAllText($mutant, $source.Replace($fault.Before, $fault.After))
    $log = Join-Path $OutputRoot ($fault.Name + '.log')
    $project = Join-Path $arenaRepo "tools/$($fault.Harness)/$($fault.Harness).csproj"
    $ErrorActionPreference = 'Continue'
    & dotnet run --project $project -c Release "-p:$($fault.Property)=$mutant" `
        "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" "-p:SPTUserMods=$arenaSentinel" *> $log
    $faultExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($faultExit -eq 0) { throw "Fault survived: $($fault.Name)" }
    if (Select-String -LiteralPath $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault failed compilation: $($fault.Name)" }
    if (!(Select-String -LiteralPath $log -SimpleMatch $fault.Failure -Quiet)) { throw "Wrong fault failure: $log" }
    if ([IO.File]::ReadAllText((Join-Path $arenaRepo $fault.File)) -cne $source) { throw 'Production changed during mutation test.' }
    Write-Output "Rejected compiled fault: $($fault.Name)"
    & dotnet run --project $project -c Release "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" "-p:SPTUserMods=$arenaSentinel"
    if ($LASTEXITCODE -ne 0) { throw "Production baseline failed after fault: $($fault.Name)" }
}
if (Test-Path -LiteralPath $arenaSentinel) { throw 'Negative controls created the no-deploy sentinel.' }
Write-Output 'AIRPIT_FAULT_CONTROLS_PASSED'
