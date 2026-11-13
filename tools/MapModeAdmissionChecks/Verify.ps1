param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path $repo ('artifacts/map-mode-faults/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$sourcePath = Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs'
$source = [IO.File]::ReadAllText($sourcePath)
# Faults on the other production files the harness links: Box = ArenaTeamBoxCatalog, Selector = BurstSpawnPointSelector.
$boxPath = Join-Path $repo 'ArenaMode.Shared/ArenaTeamBoxCatalog.cs'
$boxSource = [IO.File]::ReadAllText($boxPath)
$selectorPath = Join-Path $repo 'ArenaMode.Client/Bots/BurstSpawnPointSelector.cs'
$selectorSource = [IO.File]::ReadAllText($selectorPath)
$project = Join-Path $PSScriptRoot 'MapModeAdmissionChecks.csproj'
& dotnet run --project $project -c Release -- $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Production map admission/spawn baseline failed.' }
foreach ($fault in @(
    @{ Name='BlockAllPorts'; From='if (id == FullMapId)'; To='if (ArenaMapCatalog.IsOptionalMap(map!)) { reason = "old restriction"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='AllowWholeCustoms'; From='if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='CUSTOMS-AREA-ONLY' },
    @{ Name='ChopShopPftTactical'; From='if (ArenaMapCatalog.IsAutoService(map!) && pitFireTeam'; To='if (false && ArenaMapCatalog.IsAutoService(map!) && pitFireTeam'; Failure='PFT-FULL' },
    @{ Name='EquatorTacticalRefused'; From='if (id == FullMapId)'; To='if (ArenaMapCatalog.IsEquator(map!) && string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)) { reason = "refused"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='IcebergTacticalRefused'; From='if (id == FullMapId)'; To='if (ArenaMapCatalog.IsIceberg(map!) && string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)) { reason = "refused"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='ChopShopSoloTacticalRefused'; From='if (id == FullMapId)'; To='if (ArenaMapCatalog.IsAutoService(map!) && string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)) { reason = "refused"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='BoundedAnyPftMode'; From='if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)'; To='if (false && pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)'; Failure='BOUNDED-ADAPTER' },
    @{ Name='WrongMapContour'; From='if (!string.Equals(map, candidate.MapId, StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='MAP-IDENTITY' },
    @{ Name='LegacyPopulation'; From='if (ArenaMapCatalog.RequiresModeFilteredView(map!)'; To='if (false && ArenaMapCatalog.RequiresModeFilteredView(map!)'; Failure='CURRENT-MODES' },
    @{ Name='AllowEquatorPftZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (false && pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; Failure='PFT-FULL' },
    @{ Name='BlockEquatorSoloZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (ArenaMapCatalog.IsEquator(map!)'; Failure='FULL-MAP' },
    @{ Name='BlockAllPftZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (pitFireTeam'; Failure='PFT-FULL' },
    # A raised distance floor shrinks the usable rows of every cell: Equator's grid minimum (6 at 25 m, 4 at 30 m)
    # falls below five. It is the first assertion to see it (the baseline's earlier pins hold at 30 m).
    @{ Name='SpawnFloor30m'; Box='Selector'; From='MinSpawnDistance = 25f'; To='MinSpawnDistance = 30f'; Failure='EQUATOR-GRID' },
    # A rename of an exit literal in the team-box catalogue no longer matches the installed exits.
    @{ Name='RenamedExitLiteral'; Box='Box'; From='"Iceberg West Gate", "Iceberg East Gate"'; To='"Iceberg West Gate", "Iceberg East Portal"'; Failure='EXIT-NAMES' },
    # Bowl has a formation and no row in the mode matrix; its exits are compared by the pin's own loop.
    @{ Name='RenamedBowlExitLiteral'; Box='Box'; From='"Bowl BTR Gate", "Bowl Army Gate"'; To='"Bowl BTR Gate", "Bowl Army Portal"'; Failure='EXIT-NAMES' }
)) {
    $target = switch ($fault.Box) { 'Box' { @{ Text=$boxSource; Path=$boxPath; Property='ProductionTeamBoxCatalogSource' } }
        'Selector' { @{ Text=$selectorSource; Path=$selectorPath; Property='ProductionBurstSelectorSource' } }
        default { @{ Text=$source; Path=$sourcePath; Property='ProductionAreaCatalogSource' } } }
    $text = $target.Text
    if (-not $text.Contains($fault.From)) { throw "Missing fault anchor: $($fault.Name)" }
    if ($fault.Box -and ($text.Split([string[]]@($fault.From), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Fault anchor is not unique: $($fault.Name)" }
    $path = Join-Path $scratch ($fault.Name + '.cs')
    [IO.File]::WriteAllText($path, $text.Replace($fault.From, $fault.To))
    $configuration = 'MapFault' + $fault.Name
    $build = & dotnet build $project -c $configuration "-p:$($target.Property)=$path" -v:q 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $scratch ($fault.Name + '-build.log')), $build)
    if ($LASTEXITCODE -ne 0) { throw "Fault did not compile: $($fault.Name)`n$build" }
    $output = & dotnet run --no-build --project $project -c $configuration -- $SptRoot 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $scratch ($fault.Name + '.log')), $output)
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($fault.Failure)) {
        throw "Fault did not reach the intended failing assertion: $($fault.Name)`n$output"
    }
    if ([IO.File]::ReadAllText($sourcePath) -cne $source -or [IO.File]::ReadAllText($boxPath) -cne $boxSource -or [IO.File]::ReadAllText($selectorPath) -cne $selectorSource) { throw 'Production source changed during fault controls.' }
    Write-Output "Rejected compiled map admission fault: $($fault.Name)"
}
Write-Output "Evidence: $scratch"
Write-Output 'MAP_MODE_ADMISSION_FAULTS_PASSED (15 compiled faults)'
