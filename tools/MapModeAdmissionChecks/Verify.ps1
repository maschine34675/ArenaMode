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
    @{ Name='BlockWholeCustoms'; From='if (id == FullMapId)'; To='if (id == FullMapId && map == ArenaMapCatalog.CustomsId) { reason = "old restriction"; return false; } if (id == FullMapId)'; Failure='FULL-MAP' },
    @{ Name='EquatorTacticalCapacity'; From='if (ArenaMapCatalog.IsEquator(map!)'; To='if (false && ArenaMapCatalog.IsEquator(map!)'; Failure='EQUATOR-SQUAD' },
    @{ Name='BoundedAnyMode'; From='if (pitFireTeam || !string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='BOUNDED-ADAPTER' },
    @{ Name='WrongMapContour'; From='if (!string.Equals(map, candidate.MapId, StringComparison.OrdinalIgnoreCase))'; To='if (false)'; Failure='MAP-IDENTITY' },
    @{ Name='LegacyPopulation'; From='if (ArenaMapCatalog.RequiresModeFilteredView(map!)'; To='if (false && ArenaMapCatalog.RequiresModeFilteredView(map!)'; Failure='CURRENT-MODES' }
)) {
    if (-not $source.Contains($fault.From)) { throw "Missing fault anchor: $($fault.Name)" }
    $path = Join-Path $scratch ($fault.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($fault.From, $fault.To))
    $output = & dotnet run --project $project -c ('MapFault' + $fault.Name) "-p:ProductionAreaCatalogSource=$path" -- $SptRoot 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($fault.Failure)) {
        throw "Fault did not reach the intended failing assertion: $($fault.Name)`n$output"
    }
    if ([IO.File]::ReadAllText($sourcePath) -cne $source) { throw 'Production source changed during fault controls.' }
    Write-Output "Rejected compiled map admission fault: $($fault.Name)"
}
Write-Output 'MAP_MODE_ADMISSION_FAULTS_PASSED (6 compiled faults)'
