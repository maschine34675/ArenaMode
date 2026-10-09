param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path $repo ('artifacts/map-mode-faults/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$sourcePath = Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$project = Join-Path $PSScriptRoot 'MapModeAdmissionChecks.csproj'
& dotnet run --project $project -c Release -- $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Production map admission/spawn baseline failed.' }
foreach ($fault in @(
    @{ Name='BlockAllPorts'; From='if (id == FullMapId)'; To='if (ArenaMapCatalog.IsOptionalMap(map!)) { reason = "old restriction"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='AllowWholeCustoms'; From='if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='CUSTOMS-AREA-ONLY' },
    @{ Name='EquatorTacticalCapacity'; From='if (ArenaMapCatalog.IsEquator(map!)'; To='if (false && ArenaMapCatalog.IsEquator(map!)'; Failure='EQUATOR-SQUAD' },
    @{ Name='BoundedAnyPftMode'; From='if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)'; To='if (false && pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)'; Failure='BOUNDED-ADAPTER' },
    @{ Name='WrongMapContour'; From='if (!string.Equals(map, candidate.MapId, StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='MAP-IDENTITY' },
    @{ Name='LegacyPopulation'; From='if (ArenaMapCatalog.RequiresModeFilteredView(map!)'; To='if (false && ArenaMapCatalog.RequiresModeFilteredView(map!)'; Failure='CURRENT-MODES' },
    @{ Name='AllowEquatorPftZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (false && pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; Failure='PFT-FULL' },
    @{ Name='BlockEquatorSoloZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (ArenaMapCatalog.IsEquator(map!)'; Failure='FULL-MAP' },
    @{ Name='BlockAllPftZombies'; From='if (pitFireTeam && ArenaMapCatalog.IsEquator(map!)'; To='if (pitFireTeam'; Failure='PFT-FULL' }
)) {
    if (-not $source.Contains($fault.From)) { throw "Missing fault anchor: $($fault.Name)" }
    $path = Join-Path $scratch ($fault.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($fault.From, $fault.To))
    $configuration = 'MapFault' + $fault.Name
    $build = & dotnet build $project -c $configuration "-p:ProductionAreaCatalogSource=$path" -v:q 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $scratch ($fault.Name + '-build.log')), $build)
    if ($LASTEXITCODE -ne 0) { throw "Fault did not compile: $($fault.Name)`n$build" }
    $output = & dotnet run --no-build --project $project -c $configuration -- $SptRoot 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $scratch ($fault.Name + '.log')), $output)
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($fault.Failure)) {
        throw "Fault did not reach the intended failing assertion: $($fault.Name)`n$output"
    }
    if ([IO.File]::ReadAllText($sourcePath) -cne $source) { throw 'Production source changed during fault controls.' }
    Write-Output "Rejected compiled map admission fault: $($fault.Name)"
}
Write-Output "Evidence: $scratch"
Write-Output 'MAP_MODE_ADMISSION_FAULTS_PASSED (9 compiled faults)'
