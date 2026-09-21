$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaScratch = Join-Path $arenaRepo ('artifacts/equator-faults/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
foreach ($arenaFault in @(
    @{ Name = 'MissingClientGuard'; Source = 'ArenaMode.Client/Session/ArenaMenuSelection.cs';
       Property = 'ProductionMenuSelectionSource';
       From = '|| BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ArenaMapCatalog.ArenaMapPortPluginId)'; To = '|| true';
       Failure = 'server config alone never offers Equator without MapPort client' },
    @{ Name = 'MissingModeGuard'; Source = 'ArenaMode.Shared/ArenaAreaCatalog.cs'; Property = 'ProductionAreaCatalogSource';
       From = 'if (ArenaMapCatalog.IsOptionalMap(map!) && (pitFireTeam'; To = 'if (false && (pitFireTeam';
       Failure = 'mode switch updates the owned Equator row' },
    @{ Name = 'MissingBay5ClientGuard'; Source = 'ArenaMode.Client/Session/ArenaMenuSelection.cs';
       Property = 'ProductionMenuSelectionSource';
       From = '!ArenaMapCatalog.IsOptionalMap(map)'; To = '!ArenaMapCatalog.IsEquator(map)';
       Failure = 'server config alone never offers Bay5 without MapPort client' },
    @{ Name = 'MissingBay5OwnedRow'; Source = 'ArenaMode.Shared/ArenaMapCatalog.cs'; Property = 'ProductionMapCatalogSource';
       From = '=> string.Equals(locationId, CustomsId, StringComparison.OrdinalIgnoreCase) || IsOptionalMap(locationId);';
       To = '=> string.Equals(locationId, CustomsId, StringComparison.OrdinalIgnoreCase) || IsEquator(locationId);';
       Failure = 'mode switch updates the owned Bay5 row independently alongside Equator' }
)) {
    $arenaSourcePath = Join-Path $arenaRepo $arenaFault.Source
    $arenaSource = [IO.File]::ReadAllText($arenaSourcePath)
    if (-not $arenaSource.Contains($arenaFault.From)) { throw "Missing fault anchor: $($arenaFault.Name)" }
    $arenaMutant = Join-Path $arenaScratch ($arenaFault.Name + '.cs')
    [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaFault.From, $arenaFault.To))
    $arenaOutput = & dotnet run --project (Join-Path $arenaRepo 'tools/NativeAreaHookChecks') `
        -c ('EquatorFault' + $arenaFault.Name) "-p:$($arenaFault.Property)=$arenaMutant" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $arenaOutput.Contains($arenaFault.Failure)) {
        throw "Fault did not fail at its intended assertion: $($arenaFault.Name)`n$arenaOutput"
    }
    if ([IO.File]::ReadAllText($arenaSourcePath) -cne $arenaSource) { throw 'Production source changed during fault controls.' }
    Write-Output "Rejected Equator fault: $($arenaFault.Name)"
}

$arenaSettingsPath = Join-Path $arenaRepo 'ArenaMode.Client/Config/Settings.cs'
$arenaSettings = [IO.File]::ReadAllText($arenaSettingsPath)
$arenaOldGuard = 'if (optionalMapsRevision.Value < ArenaMapCatalog.OptionalMapsRevision)'
if (-not $arenaSettings.Contains($arenaOldGuard)) { throw 'Missing client migration guard fault anchor.' }
$arenaMutant = Join-Path $arenaScratch 'OldBooleanMigrationGuard.cs'
[IO.File]::WriteAllText($arenaMutant, $arenaSettings.Replace($arenaOldGuard, 'if (!optionalMapsMigrated.Value)'))
$arenaOutput = & dotnet run --project (Join-Path $arenaRepo 'tools/AreaConfigChecks') `
    -c Bay5OldBooleanMigrationGuard "-p:SettingsSource=$arenaMutant" 2>&1 | Out-String
if ($LASTEXITCODE -eq 0 -or -not $arenaOutput.Contains('OPTIONAL-MAP-UPGRADE')) {
    throw "Old boolean migration fault did not fail at its intended assertion.`n$arenaOutput"
}
if ([IO.File]::ReadAllText($arenaSettingsPath) -cne $arenaSettings) { throw 'Production Settings changed during fault control.' }
Write-Output 'Rejected Bay5 fault: OldBooleanMigrationGuard'
Write-Output 'ARENA_EQUATOR_FAULT_CONTROLS_PASSED'
