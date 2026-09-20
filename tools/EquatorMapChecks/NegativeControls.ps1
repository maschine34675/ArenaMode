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
       From = 'if (ArenaMapCatalog.IsEquator(map!) && (pitFireTeam'; To = 'if (false && (pitFireTeam';
       Failure = 'mode switch updates the owned Equator row' }
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
Write-Output 'ARENA_EQUATOR_FAULT_CONTROLS_PASSED'
